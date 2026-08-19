// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Takes one bounded pass over one account's outbox, and settles every send it claims.</summary>
/// <remarks>
/// <para>
/// A pass names one account, because that is where failure is isolated: a provider that is unreachable stalls the mail
/// of the mailbox it serves and no other, and one claim spanning accounts would put them behind each other.
/// </para>
/// <para>
/// The sends it claims are attempted one at a time. Each opens a connection to the same submission server, and a
/// provider counting connections is one that starts refusing them, so the parallelism a pass would buy is parallelism
/// against a single endpoint. What bounds the work instead is the batch: what the pass leaves is claimed by the next
/// one, oldest first.
/// </para>
/// <para>
/// It is the same pass whether a signal or an account's own synchronization run asked for it, which is what makes the
/// signal an accelerator rather than a second mechanism: a signal that is refused, lost, or never raised costs a send
/// the wait until that run and nothing more.
/// </para>
/// <para>
/// One send's failure never ends the pass. A submission server refusing one message is usually refusing every message,
/// so the sends behind it fail quickly beside it and the account's next run is what defers them; a message that is
/// broken on its own must not stop the ones that are not.
/// </para>
/// <para>
/// That holds for a send whose attempt could not even be written down. An attempt records its own answer, so what is
/// left to fail here is the recording itself — a database that went away while the outcome was being committed — and
/// such a send is reported as <see cref="MailOutboxDeliveryOutcome.NotRecorded" /> rather than raised through the loop.
/// Raising would leave every send behind it in the batch claimed until its lease expired, which is the delay this pass
/// exists to avoid, over a failure that says nothing about them.
/// </para>
/// </remarks>
public sealed class MailOutboxPass
{
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly MailOutboxDelivery delivery;
    private readonly OutgoingMailFilingPass filings;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly MailOutboxSettings settings;

    /// <summary>Initializes the pass from the outbox it claims out of and the attempt it runs.</summary>
    /// <param name="outgoingEmails">Claims the batch this pass settles.</param>
    /// <param name="delivery">Attempts one claimed send.</param>
    /// <param name="filings">Keeps the mailbox's own copies of these messages in step with what the pass does.</param>
    /// <param name="transportSecurityPolicyReader">Supplies the policy every submission obeys, and says whether the account submits at all.</param>
    /// <param name="settings">Bounds how much one pass claims and how long it holds it.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailOutboxPass(
        IOutgoingEmailStore outgoingEmails,
        MailOutboxDelivery delivery,
        OutgoingMailFilingPass filings,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicyReader);
        ArgumentNullException.ThrowIfNull(settings);

        this.outgoingEmails = outgoingEmails;
        this.delivery = delivery;
        this.filings = filings;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.settings = settings;
    }

    /// <summary>Claims one batch of the account's due sends and attempts each of them.</summary>
    /// <param name="accountId">The account whose outbox is drained.</param>
    /// <param name="stoppingToken">Stops each attempt when the host is shutting down.</param>
    /// <returns>What each claimed send ended in, and whether there is more waiting behind the batch.</returns>
    /// <remarks>
    /// Cancellation does not end the pass early. Every claimed send is still reached, because reaching it is what gives
    /// its lease back — a pass abandoned mid-batch would leave the sends behind the one that was running held until
    /// their leases expired.
    /// </remarks>
    public async Task<MailOutboxPassReport> RunAsync(MailAccountId accountId, CancellationToken stoppingToken)
    {
        // An account with no submission endpoint has nothing to drain and no policy to drain it under. Asking first
        // keeps a read-only account from claiming work it could never attempt.
        if (this.transportSecurityPolicyReader.GetDeliveryPolicy(accountId) is not { } transportSecurityPolicy)
        {
            return MailOutboxPassReport.Empty;
        }

        var markedUnknownCount = await this.outgoingEmails.MarkUnknownOutcomesAsync(accountId, stoppingToken);

        // Before the claim rather than after it, so a send that is waiting for an instant still ahead is mirrored while
        // it waits rather than after whatever eventually takes it. A send this claim is about to take is not waiting at
        // all and is mirrored nowhere.
        var filingResults = new List<OutgoingMailFilingResult>(
            await this.filings.MirrorWaitingSendsAsync(accountId, stoppingToken));

        var claimed = await this.outgoingEmails.ClaimAsync(
            OutgoingEmailClaimRequest.Create(accountId, this.settings.MaxDeliveriesPerPass, this.settings.LeaseDuration),
            stoppingToken);

        var results = new List<MailOutboxDeliveryResult>(claimed.Count);
        foreach (var send in claimed)
        {
            var result = await this.AttemptAsync(send, transportSecurityPolicy, stoppingToken);
            results.Add(result);

            filingResults.AddRange(await this.FileCopiesOfAsync(result, stoppingToken));
        }

        return new MailOutboxPassReport(
            results,
            filingResults,
            markedUnknownCount,
            claimed.Count >= this.settings.MaxDeliveriesPerPass);
    }

    /// <summary>Brings the mailbox's own copies of one settled send up to date, and never lets that end the pass.</summary>
    /// <remarks>
    /// A copy that could not be filed says nothing about whether the message was delivered, so it must not stop the
    /// sends behind it in the batch — which is the same reason a send whose outcome could not be recorded does not. A
    /// send that is still queued reaches this and does nothing, because nothing about its copies has changed.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Filing a copy of a message that was already delivered must not end the pass; every failure the filer itself classifies is already a result, so what is caught here is a store or a resolver that failed outright, and the record still says what became of the send.")]
    private async Task<IReadOnlyList<OutgoingMailFilingResult>> FileCopiesOfAsync(
        MailOutboxDeliveryResult result,
        CancellationToken stoppingToken)
    {
        try
        {
            return await this.filings.SettleFiledCopiesAsync(result.OutgoingEmailId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping, which says nothing about the copy: filing never reached a mail server, so reporting
            // a failure here would leave a delivered send recorded as one whose copy could not be filed. The next pass
            // settles it.
            return [];
        }
        catch (Exception failure)
        {
            // No place is named, because none was chosen: what can fail past the filer's own catches is the read of
            // the record itself, which happens before the withdrawal and the append are even decided between. Reporting
            // it against the sent copy would tag a counter and a log line with a place this failure never reached.
            return
            [
                new OutgoingMailFilingResult(
                    result.OutgoingEmailId,
                    Filing: default,
                    OutgoingMailFilingOutcome.Failed,
                    (failure as MailFathomException)?.ErrorCode
                        ?? MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly),
            ];
        }
    }

    /// <summary>Attempts one claimed send, and reports a failure the attempt could not record instead of raising it.</summary>
    /// <remarks>
    /// What is carried out is the code and nothing else, which is the same bound every other ending of a send obeys:
    /// what a store or a driver wrote into an exception is not MailFathom's own text and has no place in a line about
    /// somebody's message. A failure of the pass itself — the claim, the sweep — is not caught here and reaches its
    /// caller whole.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One send whose outcome could not be written down must not leave the sends behind it in the batch claimed until their leases expire; the record stands where the failed write left it and the next pass claims it again.")]
    private async Task<MailOutboxDeliveryResult> AttemptAsync(
        ClaimedOutgoingEmail send,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken stoppingToken)
    {
        try
        {
            return await this.delivery.DeliverAsync(send, transportSecurityPolicy, stoppingToken);
        }
        catch (Exception failure)
        {
            return new MailOutboxDeliveryResult(
                send.Record.Id,
                MailOutboxDeliveryOutcome.NotRecorded,
                (failure as MailFathomException)?.ErrorCode,
                ReplyCode: null,
                send.Record.AttemptCount);
        }
    }
}
