// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Performs a mutation by writing it down first and then carrying the record as far as the server lets it get.</summary>
/// <remarks>
/// <para>
/// The order is the whole point. The record is durable before a connection is opened, so a process that dies anywhere in
/// the sequence leaves a statement of what was being attempted and how far it got. What a later run does with that
/// statement is decided here and nowhere else: a completed mutation is answered without touching the server, a mutation
/// whose unrepeatable command was issued and never acknowledged is never issued again, and everything else resumes from
/// the stage the record names.
/// </para>
/// <para>
/// Which commands a resumed attempt skips is not decided here. That depends on what the connection advertises, which is
/// the write session's business, so the stage travels into it through the journal and the session continues from it.
/// </para>
/// </remarks>
public sealed class MailboxMutationPerformer : IMailboxMutationPerformer
{
    private readonly IMailboxMutationRecordStore store;
    private readonly IMailboxWriteSessionFactory writeSessionFactory;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IMailboxMutationAuditTrail auditTrail;
    private readonly ClientSignals signals;
    private readonly IMailFolderResolutionStore folderResolutions;
    private readonly int maximumAttempts;

    /// <summary>Initializes the performer from the record store, the write session it acts through, and its attempt bound.</summary>
    /// <param name="store">Keeps the durable record every mutation is written to.</param>
    /// <param name="writeSessionFactory">Opens the one session able to change a mailbox.</param>
    /// <param name="commitPolicy">Commits the record's first write, retrying an optimistic conflict.</param>
    /// <param name="auditTrail">Keeps the history a finished mutation leaves behind, where the account asked for one.</param>
    /// <param name="signals">Tells an open client that a change it may be drawing as pending has settled.</param>
    /// <param name="folderResolutions">Names the alias the folder a change files into is currently bound to.</param>
    /// <param name="options">Supplies how many attempts one mutation may spend.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured attempt bound is below one.</exception>
    public MailboxMutationPerformer(
        IMailboxMutationRecordStore store,
        IMailboxWriteSessionFactory writeSessionFactory,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IMailboxMutationAuditTrail auditTrail,
        ClientSignals signals,
        IMailFolderResolutionStore folderResolutions,
        MailboxMutationOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(writeSessionFactory);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(folderResolutions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumAttempts, 1, nameof(options));

        this.store = store;
        this.writeSessionFactory = writeSessionFactory;
        this.commitPolicy = commitPolicy;
        this.auditTrail = auditTrail;
        this.signals = signals;
        this.folderResolutions = folderResolutions;
        this.maximumAttempts = options.MaximumAttempts;
    }

    /// <inheritdoc />
    public async Task<MailboxMutationOutcome> PerformAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);
        RequireFolderCarriesOccurrence(request, folder);

        var outcome = await this.PerformThroughRecordAsync(
            request,
            folder,
            transportSecurityPolicy,
            cancellationToken);

        await this.AnnounceSettledChangeAsync(request, folder, outcome, cancellationToken);

        return outcome;
    }

    /// <summary>Tells an open client that a change it may be showing as pending has stopped being pending.</summary>
    /// <remarks>
    /// <para>
    /// Every status this answers with is the mailbox having settled the change — performed, already performed,
    /// abandoned, or an outcome nobody can establish — and each of those alters what a client draws for that message.
    /// A change the server merely deferred leaves here as an exception rather than as a status, so a run that reached
    /// no conclusion announces nothing and the pending row on the screen stays the true one.
    /// </para>
    /// <para>
    /// A change that names a destination moves two folders rather than one, so both are announced: the folder the
    /// occurrence was in when the record was written, and the folder it was filed into. Announcing only the first would
    /// leave somebody watching the folder mail is being filed into waiting for the next thing that happens to re-read
    /// it. Which of the two the mailbox actually reached is not distinguished, because an unacknowledged placement may
    /// have landed and a client re-reads either folder the same way.
    /// </para>
    /// <para>
    /// A flag this deployment did write is stated rather than pointed at, so the star or the read mark the person just
    /// asked for lands on their screen without a read behind it. Only the flag that was asked for is stated: the
    /// request carries one of the two, and reporting the other would be a guess about a value nobody wrote. Every
    /// status that is not the change having been made says <c>mail.changed</c> instead — an abandoned or unknown
    /// outcome is precisely the case where what the client drew as pending is not what the mailbox holds, so it re-reads
    /// rather than being told a state nothing established.
    /// </para>
    /// </remarks>
    private async Task AnnounceSettledChangeAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailboxMutationOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (!this.signals.Reaches)
        {
            return;
        }

        if (WrittenFlagsOf(request, outcome) is { } writtenFlags)
        {
            this.signals.Publish(ClientSignal.MailFlagsChanged(request.Account, folder.Alias, [writtenFlags]));

            return;
        }

        this.signals.Publish(ClientSignal.MailChanged(request.Account, folder.Alias, [request.StoredEmailId]));

        if (request.DestinationPath is not { } destination)
        {
            return;
        }

        // The request names the destination the way an IMAP command is issued against it, and a client knows a folder by
        // its alias alone, so the binding is what turns one into the other. A destination no alias is bound to — a
        // folder this deployment maps and does not mirror — announces nothing, there being no folder a client holds
        // mail for.
        var landedIn = await this.folderResolutions.GetAliasBoundToAsync(
            request.Account,
            destination,
            cancellationToken);

        if (landedIn is { } destinationAlias)
        {
            this.signals.Publish(
                ClientSignal.MailChanged(request.Account, destinationAlias, [request.StoredEmailId]));
        }
    }

    /// <summary>Where a settled change left one of the two server flags, or nothing for a change that left neither.</summary>
    /// <remarks>
    /// The keyword mutations are deliberately not here. They are flag writes as far as provenance is concerned, but the
    /// statement carries the two flags a client draws and nothing else, so a settled keyword change is a re-read like
    /// every other mutation rather than a statement with nothing in it.
    /// </remarks>
    private static SignalledEmailFlags? WrittenFlagsOf(
        MailboxMutationRequest request,
        MailboxMutationOutcome outcome)
    {
        if (outcome.Status is not (MailboxMutationStatus.Performed or MailboxMutationStatus.AlreadyPerformed))
        {
            return null;
        }

        if (request.Mutation == MailboxMutation.SetSeen && request.DesiredSeenState is { } isSeen)
        {
            return new SignalledEmailFlags(request.StoredEmailId, isSeen, IsFlagged: null);
        }

        if (request.Mutation == MailboxMutation.SetFlagged && request.DesiredFlaggedState is { } isFlagged)
        {
            return new SignalledEmailFlags(request.StoredEmailId, IsSeen: null, isFlagged);
        }

        return null;
    }

    private async Task<MailboxMutationOutcome> PerformThroughRecordAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        var record = await this.OpenRecordAsync(request, cancellationToken);

        if (SettledOutcomeOf(record) is { } settledOutcome)
        {
            if (settledOutcome.Status == MailboxMutationStatus.OutcomeUnknown)
            {
                await this.RecordUnknownOutcomeAsync(record, folder, cancellationToken);
            }

            return settledOutcome;
        }

        var journal = this.OpenJournal(record, folder);

        // The bound is checked before the attempt is counted, so a mutation whose every attempt crashed the process
        // reaches its terminal stage on the next run rather than counting a further attempt it will not survive either.
        if (record.AttemptCount >= this.maximumAttempts)
        {
            await journal.AbandonAsync(
                record.LastFailure ?? MailFathomErrorCode.MailboxMutationAttemptsExhausted,
                cancellationToken);

            return new MailboxMutationOutcome(
                record.Id,
                MailboxMutationStatus.Abandoned,
                journal.Placement);
        }

        await journal.CountAttemptAsync(cancellationToken);

        return await this.AttemptAsync(request, folder, transportSecurityPolicy, journal, cancellationToken);
    }

    /// <summary>Answers from the record alone where the record has already settled what asking again means.</summary>
    private static MailboxMutationOutcome? SettledOutcomeOf(MailboxMutationRecord record) => record.Stage switch
    {
        MailboxMutationStage.Completed => new MailboxMutationOutcome(
            record.Id,
            MailboxMutationStatus.AlreadyPerformed,
            record.Placement),
        MailboxMutationStage.Abandoned => new MailboxMutationOutcome(
            record.Id,
            MailboxMutationStatus.Abandoned,
            record.Placement),

        // Withdrawn between a pass reading the account's outstanding work and this attempt. The read already excludes
        // it, and this is what keeps the exclusion from being the only thing standing between somebody's withdrawal and
        // a command going out for it anyway.
        MailboxMutationStage.Cancelled => new MailboxMutationOutcome(
            record.Id,
            MailboxMutationStatus.Withdrawn,
            record.Placement),

        // The command that would have placed the email went out and its answer never came back. Issuing it again
        // would put a second message in the destination folder, and nothing there afterwards says whether the first
        // one landed, so the record stays where it is and stays visible.
        MailboxMutationStage.PlacementIssued => new MailboxMutationOutcome(
            record.Id,
            MailboxMutationStatus.OutcomeUnknown,
            record.Placement),
        _ => null,
    };

    /// <summary>Refuses a binding that is not the one the request's occurrence was read under.</summary>
    /// <remarks>
    /// The session would refuse it too, but only after a connection had been opened and a folder selected. Refusing here
    /// keeps a caller's mistake from costing a login, and keeps it from reaching a mailbox at all.
    /// </remarks>
    private static void RequireFolderCarriesOccurrence(MailboxMutationRequest request, MailFolderResolution folder)
    {
        if (folder.Id != request.Occurrence.FolderResolutionId)
        {
            throw new ArgumentException(
                "The folder binding does not carry the occurrence the mutation was requested for.",
                nameof(folder));
        }
    }

    /// <summary>Writes onto the record why a mutation stuck at an unacknowledged placement is stuck.</summary>
    /// <remarks>
    /// Without this the one stage that exists for a person to resolve would be the only one carrying no reason at all,
    /// so an operator reading the outstanding mutations would see it as merely old. It is written once: a record that
    /// already names this failure is left alone, so asking repeatedly costs no write.
    /// </remarks>
    private async Task RecordUnknownOutcomeAsync(
        MailboxMutationRecord record,
        MailFolderResolution folder,
        CancellationToken cancellationToken)
    {
        if (record.LastFailure == MailFathomErrorCode.MailboxMutationOutcomeUnknown)
        {
            return;
        }

        await this.OpenJournal(record, folder)
            .RecordFailureAsync(MailFathomErrorCode.MailboxMutationOutcomeUnknown, cancellationToken);
    }

    /// <summary>Opens the single writer of one record, which is also what appends its audit entry when it ends.</summary>
    private MailboxMutationJournal OpenJournal(MailboxMutationRecord record, MailFolderResolution folder) =>
        new(this.store, this.commitPolicy, this.auditTrail, record, folder);

    private async Task<MailboxMutationOutcome> AttemptAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        try
        {
            var placement = await this.PerformThroughSessionAsync(
                request,
                folder,
                transportSecurityPolicy,
                journal,
                cancellationToken);

            await journal.CompleteAsync(placement, cancellationToken);

            return new MailboxMutationOutcome(journal.RecordId, MailboxMutationStatus.Performed, placement);
        }
        catch (MailboxMutationRefusedException refusal)
        {
            // A server that advertises no way to carry the change safely will advertise none tomorrow either, and a
            // folder it does not have is not one it is about to grow, so both are terminal on their first occurrence
            // rather than after the attempt bound has been spent finding that out one login at a time.
            await journal.AbandonAsync(refusal.ErrorCode, cancellationToken);

            throw;
        }
        catch (MailFathomException failure)
        {
            await this.RecordAttemptFailureAsync(journal, failure.ErrorCode, cancellationToken);

            throw;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            await this.RecordAttemptFailureAsync(
                journal,
                MailFathomErrorCode.MailboxMutationFailedUnexpectedly,
                cancellationToken);

            throw;
        }
    }

    private async Task RecordAttemptFailureAsync(
        MailboxMutationJournal journal,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        if (journal.AttemptCount >= this.maximumAttempts)
        {
            await journal.AbandonAsync(failure, cancellationToken);

            return;
        }

        await journal.RecordFailureAsync(failure, cancellationToken);
    }

    /// <summary>Issues the change the request names, resuming from whatever stage the journal carries into the session.</summary>
    private async Task<RemoteEmailPlacement> PerformThroughSessionAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        await using var session = await this.writeSessionFactory.OpenForWritingAsync(
            request.Occurrence.AccountId,
            folder,
            transportSecurityPolicy,
            cancellationToken);

        // A closed enumeration's members are not compile-time constants, so the mutation is dispatched by comparison
        // rather than by a switch over cases. The request already guarantees one of them matches.
        if (request.Mutation == MailboxMutation.Relocate)
        {
            return await session.RelocateAsync(
                request.Occurrence,
                request.DestinationPath!.Value,
                journal,
                cancellationToken);
        }

        if (request.Mutation == MailboxMutation.Copy)
        {
            return await session.CopyAsync(
                request.Occurrence,
                request.DestinationPath!.Value,
                journal,
                cancellationToken);
        }

        if (request.Mutation == MailboxMutation.Delete)
        {
            await session.DeleteAsync(request.Occurrence, journal, cancellationToken);

            return RemoteEmailPlacement.NotReported();
        }

        if (request.Mutation == MailboxMutation.SetSeen)
        {
            await session.SetSeenAsync(
                request.Occurrence,
                request.DesiredSeenState!.Value,
                journal,
                cancellationToken);

            return RemoteEmailPlacement.NotReported();
        }

        if (request.Mutation == MailboxMutation.SetFlagged)
        {
            await session.SetFlaggedAsync(
                request.Occurrence,
                request.DesiredFlaggedState!.Value,
                journal,
                cancellationToken);

            return RemoteEmailPlacement.NotReported();
        }

        if (request.Mutation == MailboxMutation.AddKeywords)
        {
            await session.AddKeywordsAsync(request.Occurrence, request.Keywords!, journal, cancellationToken);

            return RemoteEmailPlacement.NotReported();
        }

        if (request.Mutation == MailboxMutation.RemoveKeywords)
        {
            await session.RemoveKeywordsAsync(request.Occurrence, request.Keywords!, journal, cancellationToken);

            return RemoteEmailPlacement.NotReported();
        }

        await session.SetKeywordsAsync(request.Occurrence, request.Keywords!, journal, cancellationToken);

        return RemoteEmailPlacement.NotReported();
    }

    private async Task<MailboxMutationRecord> OpenRecordAsync(
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        MailboxMutationRecord? openedRecord = null;

        await this.commitPolicy.CommitAsync(
            async (session, token) => openedRecord = await this.store.OpenAsync(session, request, heldUntil: null, token),
            cancellationToken);

        return openedRecord
            ?? throw new InvalidOperationException("The mutation record store committed without producing a record.");
    }
}
