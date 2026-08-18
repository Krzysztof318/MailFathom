// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Carries one claimed send from the record that describes it to the ending that settles it.</summary>
/// <remarks>
/// <para>
/// The whole sequence is arranged around one instant: the moment the message body may begin to reach a submission
/// server. Everything before it is repeatable at no cost, because nothing has left the deployment; everything after it
/// is not, because a message transmitted twice is a second copy in somebody's mailbox and cannot be withdrawn. So the
/// record is moved to <see cref="OutgoingEmailStage.TransmissionBegun" /> and committed <em>before</em> the envelope is
/// offered, and a process that dies anywhere past that point leaves a statement that says the outcome is unknown.
/// </para>
/// <para>
/// Being pessimistic there would strand every send whose server was briefly unreachable, so the attempt earns the
/// record back where it can prove it. A submission server is offered the body only once it has accepted at least one
/// address, so an envelope that accepted nobody is proof that nothing was transmitted — and that is the one case in
/// which the record is taken back to <see cref="OutgoingEmailStage.Recorded" /> and attempted again.
/// </para>
/// <para>
/// A server that answered settles the question by itself, whatever it said: an acceptance means the message was taken
/// and either refusal means it was not, so neither needs the ledger. The ledger decides only where the server said
/// nothing at all.
/// </para>
/// <para>
/// A transmission the server acknowledged does not always finish the send. An address it refused for now is one the
/// next attempt offers again, and that attempt transmits to the outstanding addresses alone, so nobody receives the
/// message twice and nobody is quietly dropped from it. The send is <see cref="OutgoingEmailStage.Sent" /> only once
/// nothing is outstanding, which is what that stage has always meant.
/// </para>
/// <para>
/// Nothing here retries. One attempt reaches the server once, the delivery dependency's own pipeline repeats what is
/// safe to repeat inside that attempt, and when the next attempt happens is a jittered backoff written onto the record.
/// A loop here would be a second retry layer around the same submission, which is the storm both of those are shaped to
/// avoid.
/// </para>
/// </remarks>
public sealed class MailOutboxDelivery
{
    private readonly IMailDeliverySessionFactory sessions;
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly IEmailContentStore contentStore;
    private readonly IOutgoingSenderIdentityReader senderIdentities;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailOutboxSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the attempt from the session it submits through and the record it settles.</summary>
    /// <param name="sessions">Opens the one session able to reach a submission server.</param>
    /// <param name="outgoingEmails">Holds the durable record this attempt advances.</param>
    /// <param name="contentStore">Holds the stored MIME the attempt transmits.</param>
    /// <param name="senderIdentities">Resolves the address the account's mail is sent from.</param>
    /// <param name="commitPolicy">Commits each movement of the record.</param>
    /// <param name="settings">Bounds the attempt and says when the next one may happen.</param>
    /// <param name="timeProvider">Measures the attempt's budget and stamps what it records.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailOutboxDelivery(
        IMailDeliverySessionFactory sessions,
        IOutgoingEmailStore outgoingEmails,
        IEmailContentStore contentStore,
        IOutgoingSenderIdentityReader senderIdentities,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailOutboxSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(senderIdentities);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.sessions = sessions;
        this.outgoingEmails = outgoingEmails;
        this.contentStore = contentStore;
        this.senderIdentities = senderIdentities;
        this.commitPolicy = commitPolicy;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <summary>Attempts one claimed send and records what became of it.</summary>
    /// <param name="claimed">The send this attempt holds, with the lease it holds it under.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the submission obeys.</param>
    /// <param name="stoppingToken">Stops the attempt when the host is shutting down.</param>
    /// <returns>What the attempt did, which is already durable by the time it is returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claimed" /> or <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It does not raise for a send that failed. Everything the attempt learned is written onto the record first, so a
    /// caller reads the outcome rather than catching it — which is what lets one send's failure leave the sends beside
    /// it untouched.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every way an attempt can end has to reach the record, because a submission that left the deployment and was not written down is the one failure this design exists to remove; the failure is classified into a recorded code rather than swallowed.")]
    public async Task<MailOutboxDeliveryResult> DeliverAsync(
        ClaimedOutgoingEmail claimed,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        var envelope = new MailEnvelopeLedger();

        try
        {
            return await this.SubmitAsync(claimed, transportSecurityPolicy, envelope, stoppingToken);
        }
        catch (OutgoingEmailLeaseLostException)
        {
            return LeaseLost(claimed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested
            && !envelope.MayHaveReachedRecipients)
        {
            return await this.RecoverAsync(claimed, () => this.ReleaseForShutdownAsync(claimed));
        }
        catch (Exception failure)
        {
            return await this.RecoverAsync(
                claimed,
                () => this.RecordUnansweredAsync(claimed, envelope, FailureCodeOf(failure)));
        }
    }

    /// <summary>Runs the attempt itself, from the checks that need no server to the ending the server's answer decides.</summary>
    private async Task<MailOutboxDeliveryResult> SubmitAsync(
        ClaimedOutgoingEmail claimed,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailEnvelopeLedger envelope,
        CancellationToken stoppingToken)
    {
        var record = claimed.Record;

        if (this.senderIdentities.FindSenderIdentity(record.AccountId) is not { } sender)
        {
            // The account configures no address to send from, so there is no reverse path to write and no later
            // attempt can invent one.
            return await this.RefuseAsync(
                claimed,
                outcomes: [],
                MailFathomErrorCode.OutgoingEmailSenderUnconfigured,
                replyCode: null);
        }

        var outstanding = record.OutstandingRecipients;
        if (outstanding.Count == 0)
        {
            // Every address is settled and none of them was reached by an acknowledged transmission, so the send is
            // finished with nothing left to offer it to.
            return await this.RefuseAsync(claimed, outcomes: [], MailFathomErrorCode.OutgoingEmailRefused, replyCode: null);
        }

        var content = await this.contentStore.FindOutgoingContentAsync(record.Id, stoppingToken);
        if (content is null || content.RawMime.IsEmpty)
        {
            // The record and its message are written in one transaction, so a record without one describes a send that
            // can never happen rather than a message still on its way.
            return await this.RefuseAsync(
                claimed,
                outcomes: [],
                MailFathomErrorCode.OutgoingEmailDeliveryFailedUnexpectedly,
                replyCode: null);
        }

        using var attemptBudget = new CancellationTokenSource(this.settings.AttemptTimeout, this.timeProvider);
        using var attemptToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, attemptBudget.Token);

        await using var session = await this.sessions.OpenForDeliveryAsync(
            record.AccountId,
            transportSecurityPolicy,
            attemptToken.Token);

        if (!session.Capabilities.PermitsMessageOfSize(record.MimeByteLength))
        {
            // Asked before the body is offered rather than discovered after the whole of it crossed the network. The
            // answer will not change while this server advertises what it advertises, so the send ends here.
            return await this.RefuseAsync(
                claimed,
                outcomes: [],
                MailFathomErrorCode.OutgoingEmailBoundExceeded,
                replyCode: null);
        }

        await this.CommitAsync(
            (writeSession, token) => this.outgoingEmails.RecordTransmissionBegunAsync(
                writeSession,
                claimed.Lease,
                record.Id,
                token));

        var transmission = await session.TransmitAsync(
            MailTransmissionRequest.Create(sender.Address, outstanding, content.RawMime),
            envelope,
            attemptToken.Token);

        return await this.SettleAnsweredAsync(claimed, envelope, transmission);
    }

    /// <summary>Settles a send the server answered about, which is the case where nothing is left to infer.</summary>
    private Task<MailOutboxDeliveryResult> SettleAnsweredAsync(
        ClaimedOutgoingEmail claimed,
        MailEnvelopeLedger envelope,
        MailTransmission transmission)
    {
        var acknowledged = transmission.Outcome == MailTransmissionOutcome.Accepted;
        var outcomes = this.ReadRecipientOutcomes(claimed, envelope, acknowledged);

        return transmission.Outcome switch
        {
            MailTransmissionOutcome.Accepted => this.CompleteAsync(claimed, outcomes, transmission.ReplyCode),
            MailTransmissionOutcome.RefusedPermanently => this.RefuseAsync(
                claimed,
                outcomes,
                MailFathomErrorCode.OutgoingEmailRefused,
                transmission.ReplyCode),

            // A temporary refusal is the server stating that it did not take the message, so the same bytes offered
            // again reach nobody twice — whatever the envelope had already accepted.
            _ => this.DeferOrExhaustAsync(
                claimed,
                outcomes,
                MailFathomErrorCode.MailDeliveryUnavailable,
                transmission.ReplyCode),
        };
    }

    /// <summary>Finishes an acknowledged transmission, or leaves the addresses it did not reach for the next attempt.</summary>
    private async Task<MailOutboxDeliveryResult> CompleteAsync(
        ClaimedOutgoingEmail claimed,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        int? replyCode)
    {
        if (outcomes.Any(outcome => outcome.IsOutstanding))
        {
            // The message reached the addresses the server took and the rest were refused for now. The record cannot be
            // Sent, which would say nothing more is owed, so it goes back for an attempt that offers only those.
            return await this.DeferOrExhaustAsync(claimed, outcomes, failure: null, replyCode);
        }

        await this.CommitAsync(async (session, token) =>
        {
            await this.WriteRecipientOutcomesAsync(session, claimed, outcomes, token);

            await this.outgoingEmails.AdvanceAsync(
                session,
                claimed.Lease,
                claimed.Record.Id,
                OutgoingEmailStage.Sent,
                replyCode,
                token);
        });

        return Result(claimed, MailOutboxDeliveryOutcome.Sent, failure: null, replyCode);
    }

    /// <summary>Ends a send nothing will offer again, with the reason on the record.</summary>
    private async Task<MailOutboxDeliveryResult> RefuseAsync(
        ClaimedOutgoingEmail claimed,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        MailFathomErrorCode failure,
        int? replyCode)
    {
        await this.CommitAsync(async (session, token) =>
        {
            await this.WriteRecipientOutcomesAsync(session, claimed, outcomes, token);
            await this.outgoingEmails.RecordFailureAsync(session, claimed.Lease, claimed.Record.Id, failure, token);

            await this.outgoingEmails.AdvanceAsync(
                session,
                claimed.Lease,
                claimed.Record.Id,
                OutgoingEmailStage.Refused,
                replyCode,
                token);
        });

        return Result(claimed, MailOutboxDeliveryOutcome.Refused, failure, replyCode);
    }

    /// <summary>Gives a send back for another attempt, or ends it when it has spent the attempts it was allowed.</summary>
    /// <remarks>
    /// The bound is read from the record's own attempt count, which the claim wrote before this attempt began, so a
    /// send that kills the process on every attempt reaches the same ending as one that merely fails.
    /// </remarks>
    private async Task<MailOutboxDeliveryResult> DeferOrExhaustAsync(
        ClaimedOutgoingEmail claimed,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        MailFathomErrorCode? failure,
        int? replyCode)
    {
        if (claimed.Record.AttemptCount >= this.settings.MaxAttempts)
        {
            return await this.RefuseAsync(
                claimed,
                outcomes,
                MailFathomErrorCode.OutgoingEmailAttemptsExhausted,
                replyCode);
        }

        var availableAt = this.timeProvider.GetUtcNow() + JitteredRetryBackoff.DelayBeforeNextAttempt(
            this.settings.RetryBaseDelay,
            this.settings.RetryMaxDelay,
            claimed.Record.AttemptCount);

        await this.CommitAsync(async (session, token) =>
        {
            await this.WriteRecipientOutcomesAsync(session, claimed, outcomes, token);

            await this.outgoingEmails.DeferAsync(
                session,
                claimed.Lease,
                claimed.Record.Id,
                availableAt,
                failure,
                token);
        });

        return Result(claimed, MailOutboxDeliveryOutcome.Deferred, failure, replyCode);
    }

    /// <summary>Records an attempt the server never answered, and decides from the envelope what that leaves.</summary>
    private Task<MailOutboxDeliveryResult> RecordUnansweredAsync(
        ClaimedOutgoingEmail claimed,
        MailEnvelopeLedger envelope,
        MailFathomErrorCode failure)
    {
        var outcomes = this.ReadRecipientOutcomes(claimed, envelope, transmissionAcknowledged: false);

        // A submission server is offered the body only after it accepts an address, so an envelope that accepted
        // nobody — including one that was never offered at all — proves the message reached no one.
        return envelope.MayHaveReachedRecipients
            ? this.RecordUnknownOutcomeAsync(claimed, outcomes)
            : this.DeferOrExhaustAsync(claimed, outcomes, failure, replyCode: null);
    }

    /// <summary>Leaves a send where nobody can say what its recipients received, visibly and without offering it again.</summary>
    /// <remarks>
    /// The stage is not moved. It already says the transmission began and was never answered, which is exactly the
    /// state this record is in; moving it to a terminal stage would claim knowledge of an outcome nobody has, and no
    /// claim reaches a record there, so it stands until a person decides what to do with it.
    /// </remarks>
    private async Task<MailOutboxDeliveryResult> RecordUnknownOutcomeAsync(
        ClaimedOutgoingEmail claimed,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes)
    {
        await this.CommitAsync(async (session, token) =>
        {
            await this.WriteRecipientOutcomesAsync(session, claimed, outcomes, token);

            await this.outgoingEmails.RecordFailureAsync(
                session,
                claimed.Lease,
                claimed.Record.Id,
                MailFathomErrorCode.OutgoingEmailOutcomeUnknown,
                token);
        });

        return Result(
            claimed,
            MailOutboxDeliveryOutcome.OutcomeUnknown,
            MailFathomErrorCode.OutgoingEmailOutcomeUnknown,
            replyCode: null);
    }

    /// <summary>Gives a send back unfinished because the host stopped, holding no attempt against it.</summary>
    private async Task<MailOutboxDeliveryResult> ReleaseForShutdownAsync(ClaimedOutgoingEmail claimed)
    {
        await this.CommitAsync(
            (session, token) => this.outgoingEmails.ReleaseAsync(session, claimed.Lease, claimed.Record.Id, token));

        return Result(claimed, MailOutboxDeliveryOutcome.ReleasedForShutdown, failure: null, replyCode: null);
    }

    /// <summary>Runs a recovery write, and reports a lease that had already moved on instead of raising over it.</summary>
    /// <remarks>
    /// The recovery paths write to the same record the failed attempt was writing to, so they meet the same refusal. A
    /// lease that moved on means the attempt holding the record now is the one whose answer counts, which is not a
    /// second failure to report.
    /// </remarks>
    private async Task<MailOutboxDeliveryResult> RecoverAsync(
        ClaimedOutgoingEmail claimed,
        Func<Task<MailOutboxDeliveryResult>> recordAsync)
    {
        try
        {
            return await recordAsync();
        }
        catch (OutgoingEmailLeaseLostException)
        {
            return LeaseLost(claimed);
        }
    }

    /// <summary>Reads what the server said about each address this attempt offered, as outcomes to write down.</summary>
    /// <remarks>
    /// A recipient is settled as delivered only where the transmission itself was acknowledged. An envelope accepted by
    /// a session that then failed delivered nothing, and treating it as delivery would quietly drop that recipient from
    /// every later attempt.
    /// </remarks>
    private IReadOnlyList<OutgoingRecipientOutcome> ReadRecipientOutcomes(
        ClaimedOutgoingEmail claimed,
        MailEnvelopeLedger envelope,
        bool transmissionAcknowledged)
    {
        if (envelope.Replies.Count == 0)
        {
            return [];
        }

        var answeredAt = this.timeProvider.GetUtcNow();
        var offeredByAddress = claimed.Record.Recipients
            .Select(outcome => outcome.Recipient)
            .ToDictionary(recipient => recipient.Address.NormalizedAddress);

        return
        [
            .. envelope.Replies
                .Where(reply => offeredByAddress.ContainsKey(reply.Address.NormalizedAddress))
                .Select(reply => OutgoingRecipientOutcome.Answered(
                    offeredByAddress[reply.Address.NormalizedAddress],
                    StatusOf(reply, transmissionAcknowledged),
                    reply.ReplyCode,
                    answeredAt)),
        ];
    }

    private Task WriteRecipientOutcomesAsync(
        IPersistenceSession session,
        ClaimedOutgoingEmail claimed,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken) => outcomes.Count == 0
        ? Task.CompletedTask
        : this.outgoingEmails.RecordRecipientOutcomesAsync(
            session,
            claimed.Lease,
            claimed.Record.Id,
            outcomes,
            cancellationToken);

    /// <summary>Commits one movement of the record, and never lets a stopping host lose an answer already given.</summary>
    /// <remarks>
    /// The write runs outside the caller's cancellation on purpose. What it records is a submission that has already
    /// happened outside this deployment, and a shutdown that abandoned the write would leave the record describing an
    /// attempt that never reached the point it actually reached.
    /// </remarks>
    private Task CommitAsync(Func<IPersistenceSession, CancellationToken, Task> stageChangesAsync) =>
        this.commitPolicy.CommitAsync(stageChangesAsync, CancellationToken.None);

    private static OutgoingRecipientStatus StatusOf(MailRecipientReply reply, bool transmissionAcknowledged) =>
        reply.Acceptance switch
        {
            MailRecipientAcceptance.Accepted => transmissionAcknowledged
                ? OutgoingRecipientStatus.Accepted
                : OutgoingRecipientStatus.Pending,
            MailRecipientAcceptance.RefusedPermanently => OutgoingRecipientStatus.Refused,

            // A temporary refusal is a recipient the next attempt offers again, which is what Pending already means;
            // the reply that deferred them is recorded beside the status rather than encoded in it.
            _ => OutgoingRecipientStatus.Pending,
        };

    /// <summary>Names the code that stands for whatever ended an attempt without a server's answer.</summary>
    /// <remarks>
    /// A first-party failure already carries the code an operator looks up, and a spent budget of either kind is a
    /// submission endpoint that did not serve the operation. What is left is genuinely unaccounted for and says so
    /// rather than borrowing a code that would mislead.
    /// </remarks>
    private static MailFathomErrorCode FailureCodeOf(Exception failure) => failure switch
    {
        MailFathomException named => named.ErrorCode,
        TimeoutException or OperationCanceledException => MailFathomErrorCode.MailDeliveryUnavailable,
        _ => MailFathomErrorCode.OutgoingEmailDeliveryFailedUnexpectedly,
    };

    private static MailOutboxDeliveryResult LeaseLost(ClaimedOutgoingEmail claimed) =>
        Result(claimed, MailOutboxDeliveryOutcome.LeaseLost, failure: null, replyCode: null);

    private static MailOutboxDeliveryResult Result(
        ClaimedOutgoingEmail claimed,
        MailOutboxDeliveryOutcome outcome,
        MailFathomErrorCode? failure,
        int? replyCode) =>
        new(claimed.Record.Id, outcome, failure, replyCode, claimed.Record.AttemptCount);
}
