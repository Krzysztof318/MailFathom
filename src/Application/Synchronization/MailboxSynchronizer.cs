// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Synchronization;

/// <summary>Coordinates read-only mailbox folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly MailFolderResolver folderResolver;
    private readonly IMailboxSessionFactory mailboxSessionFactory;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly IMailSynchronizationWindowReader synchronizationWindowReader;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IPersistenceSessionFactory persistenceSessionFactory;
    private readonly IEmailMetadataRepository metadataRepository;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailMimeReader mimeReader;
    private readonly MailboxReconciler reconciler;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(
        MailFolderResolver folderResolver,
        IMailboxSessionFactory mailboxSessionFactory,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        IMailSynchronizationWindowReader synchronizationWindowReader,
        ISynchronizationCheckpointStore checkpointStore,
        IPersistenceSessionFactory persistenceSessionFactory,
        IEmailMetadataRepository metadataRepository,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader,
        MailboxReconciler reconciler,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        this.folderResolver = folderResolver;
        this.mailboxSessionFactory = mailboxSessionFactory;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.synchronizationWindowReader = synchronizationWindowReader;
        this.checkpointStore = checkpointStore;
        this.persistenceSessionFactory = persistenceSessionFactory;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.mimeReader = mimeReader;
        this.reconciler = reconciler;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Synchronizes one configured folder alias without mutating remote mailbox flags.</summary>
    /// <param name="accountId">The account to synchronize.</param>
    /// <param name="folderMapping">What configuration says the alias names.</param>
    /// <param name="cancellationToken">Cancels the run between remote reads and local writes.</param>
    /// <returns>The bounded progress this run committed, or the reason the alias named no remote folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderMapping" /> is <see langword="null" />.</exception>
    /// <exception cref="MailTransportSecurityPolicyViolationException">
    /// Thrown when the account's configured transport security policy is unsafe, before any connection is attempted.
    /// </exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded local retries could not resolve. Progress already
    /// committed by this run stays durable, and the next run rereads the committed checkpoint before deciding again.
    /// </exception>
    /// <exception cref="MailboxUnavailableException">
    /// Thrown when the mail server did not serve the run within its configured resilience budget. Progress already
    /// committed stays durable and the next run resumes from the persisted checkpoint.
    /// </exception>
    /// <exception cref="MailboxFolderRecreatedException">
    /// Thrown when a recovered connection reselected the folder with a different UIDVALIDITY, so the identities this
    /// run was working with no longer name the same emails. The next run starts the folder over.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The alias is resolved against the server's advertised folders before anything is read, so a run always works
    /// under the binding that is durable at the moment it starts. An alias the server advertises no folder for ends
    /// this run and no other.
    /// </para>
    /// <para>
    /// The account's synchronization window is read at the same moment as its transport security policy and bounds
    /// every batch the run requests. Excluded mail is left out by the server, so it costs no fetch, no MIME read, and
    /// no local write, and the folder checkpoint still advances across the excluded range so a run ends instead of
    /// rescanning it on every interval.
    /// </para>
    /// </remarks>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(
        MailAccountId accountId,
        MailFolderMapping folderMapping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderMapping);

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(accountId);

        var resolutionResult = await this.folderResolver.ResolveAsync(
            accountId,
            folderMapping,
            transportSecurityPolicy,
            cancellationToken);

        if (resolutionResult.Resolution is not { } folder)
        {
            return MailboxSynchronizationResult.FolderNotResolved(resolutionResult.Outcome);
        }

        return await this.SynchronizeResolvedFolderAsync(
            accountId,
            folder,
            transportSecurityPolicy,
            this.synchronizationWindowReader.GetWindow(accountId),
            cancellationToken);
    }

    private async Task<MailboxSynchronizationResult> SynchronizeResolvedFolderAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailSynchronizationWindow synchronizationWindow,
        CancellationToken cancellationToken)
    {
        var persistedCheckpoint =
            await this.checkpointStore.GetCheckpointAsync(accountId, folder.Id, cancellationToken);

        await using var mailboxSession = await this.mailboxSessionFactory.OpenReadOnlyAsync(
            accountId,
            folder,
            transportSecurityPolicy,
            cancellationToken);

        var uidValidity = await mailboxSession.GetUidValidityAsync(cancellationToken);
        var checkpoint = persistedCheckpoint?.UidValidity == uidValidity
            ? persistedCheckpoint
            : SynchronizationCheckpoint.None(uidValidity);

        var storedCount = 0;
        var skippedOversizedCount = 0;
        var unreadableMimeCount = 0;
        var hasMore = true;
        var inspectedBatchCount = 0;

        while (hasMore && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
        {
            inspectedBatchCount++;

            var batch = await mailboxSession.GetEmailBatchAfterAsync(
                checkpoint.LastSeenUid,
                this.options.MaxMetadataBatchSize,
                synchronizationWindow,
                cancellationToken);
            foreach (var metadata in batch.Emails.OrderBy(email => email.OccurrenceId.Uid.Value))
            {
                var occurrence = await this.StoreOccurrenceAsync(mailboxSession, metadata, cancellationToken);
                if (occurrence.Availability == StoredEmailContentAvailability.Available)
                {
                    storedCount++;
                }
                else
                {
                    skippedOversizedCount++;
                }

                if (occurrence.MimeCouldNotBeRead)
                {
                    unreadableMimeCount++;
                }
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                var advancedCheckpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());
                await this.CommitCheckpointAsync(
                    accountId,
                    folder,
                    persistedCheckpoint,
                    advancedCheckpoint,
                    cancellationToken);

                checkpoint = advancedCheckpoint;
                persistedCheckpoint = advancedCheckpoint;
            }

            hasMore = batch.HasMore;
        }

        // The backward pass runs last and over the same open session, so it costs no second connection and inspects
        // only what the forward pass has already committed. It is deliberately not gated on the forward pass having
        // finished its folder: a mailbox whose backfill spans many runs must still notice a deletion in the part of it
        // that is already stored.
        var reconciliation = await this.reconciler.ReconcileAsync(
            mailboxSession,
            accountId,
            folder,
            uidValidity,
            cancellationToken);

        return MailboxSynchronizationResult.Synchronized(
            storedCount,
            skippedOversizedCount,
            unreadableMimeCount,
            hasMore,
            checkpoint,
            reconciliation);
    }

    private async Task<OccurrenceSynchronizationOutcome> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
        }

        var fetch = await mailboxSession.FetchEmailContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
        if (fetch is not { Outcome: RemoteEmailContentFetchOutcome.Retrieved, Content: { } content })
        {
            // The advertised size understated the payload, so the occurrence is recorded without content instead of
            // being silently skipped past by the checkpoint.
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
        }

        // Enrichment reads the payload this run already fetched, so it costs no second IMAP round trip and cannot reach
        // the remote \Seen flag. A message nobody can parse is counted and stepped over: the occurrence is stored with
        // only what the server's envelope reported, and the folder checkpoint still advances past it.
        var extraction = await this.mimeReader.ReadMetadataAsync(content, cancellationToken);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                var storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    extraction.Metadata,
                    StoredEmailContentAvailability.Available,
                    attemptCancellationToken);
                await this.contentStore.SaveContentAsync(
                    persistenceSession,
                    storedEmailId,
                    content,
                    attemptCancellationToken);
            },
            cancellationToken);

        return new OccurrenceSynchronizationOutcome(
            StoredEmailContentAvailability.Available,
            extraction.Outcome != EmailMimeExtractionOutcome.Extracted);
    }

    private async Task<OccurrenceSynchronizationOutcome> RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    extractedMetadata: null,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    attemptCancellationToken);
            },
            cancellationToken);

        // An occurrence whose content was never retrieved has no MIME to read, so it is neither enriched nor counted as
        // unreadable.
        return new OccurrenceSynchronizationOutcome(
            StoredEmailContentAvailability.ExceededSizeLimit,
            MimeCouldNotBeRead: false);
    }

    // A checkpoint advance is attempted once rather than retried: the intended progress was derived from the state read
    // at the start of the run, so a competing advance invalidates the decision itself instead of only the write.
    private async Task CommitCheckpointAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession =
            await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        await this.checkpointStore.SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folder.Id,
            expectedCheckpoint,
            checkpoint,
            cancellationToken);

        if (await persistenceSession.CommitAsync(cancellationToken) == PersistenceCommitResult.ConcurrencyConflict)
        {
            throw new PersistenceConcurrencyConflictException(
                $"Synchronization progress for folder {folder.Alias.Value} was changed by another writer before this run committed its advance.");
        }
    }

    /// <summary>States what one occurrence's turn through the run produced.</summary>
    /// <param name="Availability">Whether the occurrence was stored with its content or as metadata only.</param>
    /// <param name="MimeCouldNotBeRead">Whether enrichment refused the payload that was stored.</param>
    private readonly record struct OccurrenceSynchronizationOutcome(
        StoredEmailContentAvailability Availability,
        bool MimeCouldNotBeRead);
}

/// <summary>States whether a synchronization run reached its folder at all.</summary>
public enum MailboxSynchronizationOutcome
{
    /// <summary>The run synchronized the folder the alias is bound to.</summary>
    Synchronized = 0,

    /// <summary>The server advertised no folder matching the alias, so this folder was not synchronized.</summary>
    FolderAliasUnresolved = 1,

    /// <summary>Several advertised folders matched the alias, so this folder was not synchronized until the operator says which one it means.</summary>
    FolderAliasAmbiguous = 2,
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
/// <param name="Outcome">Whether the run reached a folder.</param>
/// <param name="StoredEmailCount">How many occurrences were stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
/// <param name="UnreadableMimeEmailCount">How many stored occurrences carried MIME that enrichment could not read.</param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed emails when the run's batch budget ran out.</param>
/// <param name="Checkpoint">The progress the run ended on, which is present exactly when <paramref name="Outcome" /> is <see cref="MailboxSynchronizationOutcome.Synchronized" />.</param>
/// <param name="Reconciliation">What the run's backward pass found among the emails already stored for this folder.</param>
public sealed record MailboxSynchronizationResult(
    MailboxSynchronizationOutcome Outcome,
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    int UnreadableMimeEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint? Checkpoint,
    MailboxReconciliationResult Reconciliation)
{
    /// <summary>Reports a run that reached its folder.</summary>
    /// <param name="storedEmailCount">How many occurrences were stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
    /// <param name="unreadableMimeEmailCount">How many stored occurrences carried unreadable MIME.</param>
    /// <param name="hasMoreEmails">Whether unprocessed emails remain.</param>
    /// <param name="checkpoint">The progress the run ended on.</param>
    /// <param name="reconciliation">What the run's backward pass found.</param>
    /// <returns>A synchronized result.</returns>
    public static MailboxSynchronizationResult Synchronized(
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails,
        SynchronizationCheckpoint checkpoint,
        MailboxReconciliationResult reconciliation) =>
        new(
            MailboxSynchronizationOutcome.Synchronized,
            storedEmailCount,
            skippedOversizedEmailCount,
            unreadableMimeEmailCount,
            hasMoreEmails,
            checkpoint,
            reconciliation);

    /// <summary>Reports a configured alias that named no single advertised folder.</summary>
    /// <param name="resolutionOutcome">Why resolution produced no binding.</param>
    /// <returns>An unsynchronized result, which describes no progress because none was attempted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="resolutionOutcome" /> reports a binding that this result cannot describe.</exception>
    /// <remarks>
    /// The two reasons stay distinct all the way to the worker's log, because they ask the operator for different
    /// things: one to correct an alias that names nothing, the other to name a path where a role is not unique.
    /// </remarks>
    public static MailboxSynchronizationResult FolderNotResolved(MailFolderResolutionOutcome resolutionOutcome)
    {
        var outcome = resolutionOutcome switch
        {
            MailFolderResolutionOutcome.NoAdvertisedFolderMatched => MailboxSynchronizationOutcome.FolderAliasUnresolved,
            MailFolderResolutionOutcome.AdvertisedFoldersAreAmbiguous => MailboxSynchronizationOutcome.FolderAliasAmbiguous,
            _ => throw new ArgumentOutOfRangeException(
                nameof(resolutionOutcome),
                resolutionOutcome,
                "A resolved folder is reported through Synchronized rather than as an unresolved alias."),
        };

        return new MailboxSynchronizationResult(
            outcome,
            0,
            0,
            0,
            HasMoreEmails: false,
            Checkpoint: null,
            MailboxReconciliationResult.NothingToReconcile);
    }
}
