// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>Composes the message one occasion of a recurring send calls for, and writes it into the outbox.</summary>
/// <remarks>
/// <para>
/// The occasion produces an ordinary send and nothing more: an outgoing record with its own identity, its own attempts,
/// its own failure, and its own ending. Nothing here transmits, and the delivery that follows is the one every other
/// message gets — which is what makes one Monday's provider outage cost that Monday's message and no other's.
/// </para>
/// <para>
/// Which occasion this is running for is read from the schedule rather than carried in the payload, because a recurring
/// dispatch repeats one piece of work and the occasion is the schedule's to decide. The most recent occasion at or
/// before now is the one composed, so a run that started late still produces the message that was due rather than a
/// message for a moment that has not come, and two runs reaching the same occasion compose one identity — which the
/// outbox answers with the record the first one wrote.
/// </para>
/// <para>
/// One occurrence is in flight at a time, and this is where that is enforced rather than assumed. The message the last
/// occasion produced is asked about first, and while it is still queued this occasion is answered instead of started:
/// a weekly message whose provider has been unreachable all week must not put a second week's copy behind the first,
/// and a message whose outcome nobody knows must not be followed by another until somebody has looked at it.
/// </para>
/// <para>
/// The message is due at the occasion rather than at the moment it was composed, which is what leaves the deployment's
/// lateness bound holding for a repetition exactly as it holds for a message somebody scheduled by hand: an occasion
/// reached long afterwards — because nothing was running, or because the queue was full — is delivered while it is
/// still timely and stands where an operator can see it when it is not.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once, which is what the queue asks of every handler.
/// </para>
/// </remarks>
public sealed class RecurringSendOccurrenceHandler : IJobHandler
{
    private readonly IRecurringSendStore recurringSends;
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly IEmailContentStore contentStore;
    private readonly IAuthoredEmailComposer composer;
    private readonly MailOutbox outbox;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the handler from the declaration it reads and the outbox it writes into.</summary>
    /// <param name="recurringSends">Reads the declaration and records the occasion it has now produced a message for.</param>
    /// <param name="outgoingEmails">Answers what became of the message the previous occasion produced.</param>
    /// <param name="contentStore">Holds the draft this occasion's message is composed from.</param>
    /// <param name="composer">Gives this occasion's message an identity and a date of its own.</param>
    /// <param name="outbox">Writes the occurrence down and says the account has something to send.</param>
    /// <param name="retryPolicy">Commits the occasion this declaration has now reached.</param>
    /// <param name="timeProvider">Says which occasion has come round.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public RecurringSendOccurrenceHandler(
        IRecurringSendStore recurringSends,
        IOutgoingEmailStore outgoingEmails,
        IEmailContentStore contentStore,
        IAuthoredEmailComposer composer,
        MailOutbox outbox,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(recurringSends);
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.recurringSends = recurringSends;
        this.outgoingEmails = outgoingEmails;
        this.contentStore = contentStore;
        this.composer = composer;
        this.outbox = outbox;
        this.retryPolicy = retryPolicy;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.SendRecurringOccurrence;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the declaration holds no draft to compose from, which is a stored payload that was damaged rather than an occasion to pass over.</exception>
    /// <exception cref="MailSubmissionRefusedException">Thrown when this occasion's message is one the deployment will not send, so the occasion reaches the queue's own failure path instead of being dropped.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not RecurringSendJobPayload recurringSend)
        {
            throw new ArgumentException(
                $"A '{JobType.SendRecurringOccurrence}' job carries a payload naming one recurring send.",
                nameof(payload));
        }

        var declarationId = recurringSend.ToRecurringSendId();
        var declaration = await this.recurringSends.FindAsync(declarationId, cancellationToken);

        // A declaration stopped between the dispatch and this attempt produces nothing, which is the whole of what
        // stopping one means: the occasion had come, and the owner said no further message.
        if (declaration is null || !declaration.IsActive)
        {
            return;
        }

        if (!JobRecurrence.TryParse(declaration.Schedule, out var recurrence, out _)
            || recurrence!.LatestOccurrenceAtOrBefore(this.timeProvider.GetUtcNow()) is not { } occurrence)
        {
            return;
        }

        if (await this.IsPreviousOccurrenceInFlightAsync(declaration, cancellationToken))
        {
            return;
        }

        var record = await this.ComposeOccurrenceAsync(declaration, occurrence, recurrence.ZoneName, cancellationToken);

        await this.retryPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.recurringSends.RecordOccurrenceAsync(
                session,
                declarationId,
                occurrence,
                record.Id,
                attemptCancellationToken),
            cancellationToken);
    }

    /// <summary>Composes this occasion's message from the stored draft and writes it into the outbox.</summary>
    private async Task<OutgoingEmailRecord> ComposeOccurrenceAsync(
        RecurringSend declaration,
        DateTimeOffset occurrence,
        string zoneName,
        CancellationToken cancellationToken)
    {
        var draft = await this.contentStore.FindRecurringSendDraftAsync(declaration.Id, cancellationToken);

        if (draft is null || draft.RawMime.IsEmpty)
        {
            // The declaration and its draft are written in one transaction, so a declaration without one describes
            // occasions that could never produce a message. It is raised rather than passed over, because a repetition
            // that silently produced nothing every week is the failure nobody would notice.
            throw new InvalidOperationException(
                $"Recurring send {declaration.Id} holds no draft to compose an occurrence from.");
        }

        var composition = this.composer.RecomposeAsOccurrence(
            declaration.AccountId,
            OutgoingEmailRequester.Schedule(declaration.Id, occurrence),
            declaration.Recipients,
            draft.RawMime,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } composed)
        {
            // What refuses an occasion here is the deployment's own bounds, applied again against the message this
            // occasion actually became rather than against the one somebody wrote weeks ago. The occasion fails
            // visibly through the queue, which is where an operator already looks for work that stopped.
            throw MailSubmissionRefusedException.From(composition.Refusal!);
        }

        var opened = await this.outbox.EnqueueAsync(
            composed.Request.HeldUntil(ZonedInstant.Restore(occurrence, zoneName)),
            composed.RawMime,
            cancellationToken);

        return opened.Record;
    }

    /// <summary>Answers whether the message the previous occasion produced is still on its way.</summary>
    /// <remarks>
    /// A record the outbox no longer holds is not in flight. That is what an erased or manually removed row produces,
    /// and treating it as still going would stop the repetition forever over a record nobody can point at.
    /// </remarks>
    private async Task<bool> IsPreviousOccurrenceInFlightAsync(
        RecurringSend declaration,
        CancellationToken cancellationToken)
    {
        if (declaration.LastOccurrenceEmailId is not { } previous)
        {
            return false;
        }

        var record = await this.outgoingEmails.FindAsync(previous, cancellationToken);

        return record is not null && !record.IsTerminal;
    }
}
