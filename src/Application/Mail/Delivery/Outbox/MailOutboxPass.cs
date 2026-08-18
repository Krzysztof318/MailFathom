// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

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
/// </remarks>
public sealed class MailOutboxPass
{
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly MailOutboxDelivery delivery;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly MailOutboxSettings settings;

    /// <summary>Initializes the pass from the outbox it claims out of and the attempt it runs.</summary>
    /// <param name="outgoingEmails">Claims the batch this pass settles.</param>
    /// <param name="delivery">Attempts one claimed send.</param>
    /// <param name="transportSecurityPolicyReader">Supplies the policy every submission obeys, and says whether the account submits at all.</param>
    /// <param name="settings">Bounds how much one pass claims and how long it holds it.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailOutboxPass(
        IOutgoingEmailStore outgoingEmails,
        MailOutboxDelivery delivery,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicyReader);
        ArgumentNullException.ThrowIfNull(settings);

        this.outgoingEmails = outgoingEmails;
        this.delivery = delivery;
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

        var claimed = await this.outgoingEmails.ClaimAsync(
            OutgoingEmailClaimRequest.Create(accountId, this.settings.MaxDeliveriesPerPass, this.settings.LeaseDuration),
            stoppingToken);

        var results = new List<MailOutboxDeliveryResult>(claimed.Count);
        foreach (var send in claimed)
        {
            results.Add(await this.delivery.DeliverAsync(send, transportSecurityPolicy, stoppingToken));
        }

        return new MailOutboxPassReport(
            results,
            markedUnknownCount,
            claimed.Count >= this.settings.MaxDeliveriesPerPass);
    }
}
