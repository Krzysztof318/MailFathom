// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
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
    private readonly IMailboxMutationReconciliationStore mutationStore;
    private readonly MailboxReconciler reconciler;
    private readonly IEmailEmbeddingBacklog embeddingBacklog;
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
        IMailboxMutationReconciliationStore mutationStore,
        MailboxReconciler reconciler,
        IEmailEmbeddingBacklog embeddingBacklog,
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
        this.mutationStore = mutationStore;
        this.reconciler = reconciler;
        this.embeddingBacklog = embeddingBacklog;
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
        var relocatedCount = 0;
        var hasMore = true;
        var inspectedBatchCount = 0;
        var suppressedChanges = new List<SuppressedMailboxChange>();

        while (hasMore && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
        {
            inspectedBatchCount++;

            var batch = await mailboxSession.GetEmailBatchAfterAsync(
                checkpoint.LastSeenUid,
                this.options.MaxMetadataBatchSize,
                synchronizationWindow,
                cancellationToken);
            var placements = await this.ReadPlacementsInBatchAsync(
                accountId,
                folder,
                uidValidity,
                batch,
                cancellationToken);

            foreach (var metadata in batch.Emails.OrderBy(email => email.OccurrenceId.Uid.Value))
            {
                var placement = FindPlacementOf(placements, folder, metadata.OccurrenceId);

                if (placement is { } relocation
                    && relocation.Request.Mutation == MailboxMutation.Relocate
                    && await this.TryCarryRelocatedEmailAsync(relocation, metadata.OccurrenceId, cancellationToken))
                {
                    relocatedCount++;
                    suppressedChanges.Add(new SuppressedMailboxChange(
                        MailboxChangeKind.EmailAppearedInFolder,
                        relocation.Request.Mutation,
                        relocation.Request.StoredEmailId,
                        relocation.Id));

                    continue;
                }

                // A copy is stored like any other discovery, because the email it duplicates stays where it was and
                // nothing is carried across. What the record settles is only whose act the arrival was.
                var copy = placement is { } candidate && candidate.Request.Mutation == MailboxMutation.Copy
                    ? candidate
                    : null;

                var occurrence = await this.StoreOccurrenceAsync(mailboxSession, metadata, copy, cancellationToken);
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

                if (copy is not null)
                {
                    suppressedChanges.Add(new SuppressedMailboxChange(
                        MailboxChangeKind.EmailAppearedInFolder,
                        copy.Request.Mutation,
                        occurrence.StoredEmailId,
                        copy.Id));
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
            checkpoint.ReconciledThroughModSeq,
            cancellationToken);

        checkpoint = await this.RecordReconciledModSeqAsync(
            accountId,
            folder,
            persistedCheckpoint,
            checkpoint,
            reconciliation.ReconciledThroughModSeq,
            cancellationToken);

        return MailboxSynchronizationResult.Synchronized(
            folder,
            storedCount,
            skippedOversizedCount,
            unreadableMimeCount,
            relocatedCount,
            hasMore,
            checkpoint,
            reconciliation,
            [.. suppressedChanges, .. reconciliation.SuppressedChanges]);
    }

    /// <summary>Reads the mutations whose destination is this folder and whose placement is one of the UIDs this batch discovered.</summary>
    /// <remarks>
    /// A batch that discovered nothing asks nothing. The read is otherwise made once for the whole batch, so recognizing
    /// MailFathom's own work costs one query per batch on a folder nobody writes into and never one per message.
    /// </remarks>
    private async Task<IReadOnlyList<MailboxMutationRecord>> ReadPlacementsInBatchAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        RemoteEmailMetadataBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.Emails.Count == 0)
        {
            return [];
        }

        return await this.mutationStore.ReadPlacementsAtAsync(
            accountId,
            folder.RemotePath,
            uidValidity,
            [.. batch.Emails.Select(static email => email.OccurrenceId.Uid)],
            cancellationToken);
    }

    /// <summary>Finds the mutation whose reported placement is the occurrence this discovery sits at, if any is.</summary>
    /// <remarks>
    /// The first match is taken. Every condition of the read is restated by the record itself rather than trusted, so a
    /// query widened later cannot quietly widen what counts as MailFathom's own work.
    /// </remarks>
    private static MailboxMutationRecord? FindPlacementOf(
        IReadOnlyList<MailboxMutationRecord> placements,
        MailFolderResolution folder,
        EmailOccurrenceId occurrenceId) =>
        placements.FirstOrDefault(candidate =>
            candidate.AccountsForPlacementAt(folder.RemotePath, occurrenceId.UidValidity, occurrenceId.Uid));

    /// <summary>Carries the local email a relocation moved onto the occurrence it was discovered at, rather than storing a second one.</summary>
    /// <returns><see langword="true" /> when the discovery was this mutation's own and needs nothing further; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// It runs before the payload is fetched, so recognizing a relocation costs no <c>FETCH</c> and no MIME read: the
    /// message is the one already stored, and everything derived from it is keyed by the local identity that is being
    /// carried across rather than by the occurrence it sits at.
    /// </para>
    /// <para>
    /// A discovery whose destination occurrence another row already occupies falls through to the ordinary path, as one
    /// that matches nothing does. That is the same treatment mail a person moved in their own client gets, which is
    /// what keeps this change invisible to every mailbox MailFathom is not writing to — and it leaves the record
    /// unobserved, so nothing claims a change was accounted for that no local row reflects.
    /// </para>
    /// </remarks>
    private async Task<bool> TryCarryRelocatedEmailAsync(
        MailboxMutationRecord record,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        var carried = false;

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                carried = await this.metadataRepository.TryCarryToOccurrenceAsync(
                    persistenceSession,
                    record.Request.StoredEmailId,
                    occurrenceId,
                    attemptCancellationToken);

                if (carried)
                {
                    await this.mutationStore.RecordPlacementObservedAsync(
                        persistenceSession,
                        record.Id,
                        this.timeProvider.GetUtcNow(),
                        attemptCancellationToken);
                }
            },
            cancellationToken);

        return carried;
    }

    /// <summary>Commits the modification sequence a completed backward pass reached, and leaves the checkpoint alone otherwise.</summary>
    /// <remarks>
    /// It is a separate commit from the forward pass's because the two record different things: the forward advance
    /// says which mail has been retrieved, and this says how much of the folder's history the backward pass no longer
    /// has to re-inspect. It is committed under the same compare, so a competing writer ends this run exactly as it
    /// ends a forward advance; what the next run loses by starting without the sequence is one full window scan.
    /// </remarks>
    private async Task<SynchronizationCheckpoint> RecordReconciledModSeqAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        SynchronizationCheckpoint? persistedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        ulong? reconciledThroughModSeq,
        CancellationToken cancellationToken)
    {
        if (reconciledThroughModSeq is not { } modSeq)
        {
            return checkpoint;
        }

        var reconciledCheckpoint = checkpoint.ReconciledThrough(modSeq);
        if (ReferenceEquals(reconciledCheckpoint, checkpoint))
        {
            return checkpoint;
        }

        await this.CommitCheckpointAsync(
            accountId,
            folder,
            persistedCheckpoint,
            reconciledCheckpoint,
            cancellationToken);

        return reconciledCheckpoint;
    }

    /// <summary>Stores one discovered occurrence, settling the copy that placed it in the same transaction where there was one.</summary>
    /// <remarks>
    /// The placement observation belongs in the write that stores the email rather than beside it. A copy whose arrival
    /// was recorded but whose row was rolled back would answer for a change no local state reflects, and the next run
    /// would store the message as an arrival nobody accounted for.
    /// </remarks>
    private async Task<OccurrenceSynchronizationOutcome> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        MailboxMutationRecord? placement,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            return await this.RecordOversizedOccurrenceAsync(metadata, placement, cancellationToken);
        }

        var fetch = await mailboxSession.FetchEmailContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
        if (fetch is not { Outcome: RemoteEmailContentFetchOutcome.Retrieved, Content: { } content })
        {
            // The advertised size understated the payload, so the occurrence is recorded without content instead of
            // being silently skipped past by the checkpoint.
            return await this.RecordOversizedOccurrenceAsync(metadata, placement, cancellationToken);
        }

        // Enrichment reads the payload this run already fetched, so it costs no second IMAP round trip and cannot reach
        // the remote \Seen flag. A message nobody can parse is counted and stepped over: the occurrence is stored with
        // only what the server's envelope reported, and the folder checkpoint still advances past it.
        var extraction = await this.mimeReader.ReadMetadataAsync(content, cancellationToken);

        var storedEmailId = default(StoredEmailId);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
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

                await this.ObservePlacementAsync(persistenceSession, placement, attemptCancellationToken);
            },
            cancellationToken);

        // Offered after the commit and never inside it, which is what keeps a provider outage out of this run: the
        // message and the passages the chunk writer derived beside it are durable by now, so the worker consumes
        // committed state and nothing it does can extend or fail the transaction that produced it. A refusal by a full
        // backlog is deliberately not an error here — the message is stored, and the backfill is what reaches mail the
        // live path did not. The identity is the one the commit resolved, which is the same value this outcome reports.
        this.embeddingBacklog.TryEnqueue(storedEmailId);

        return new OccurrenceSynchronizationOutcome(
            storedEmailId,
            StoredEmailContentAvailability.Available,
            extraction.Outcome != EmailMimeExtractionOutcome.Extracted);
    }

    private async Task<OccurrenceSynchronizationOutcome> RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        MailboxMutationRecord? placement,
        CancellationToken cancellationToken)
    {
        var storedEmailId = default(StoredEmailId);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    extractedMetadata: null,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    attemptCancellationToken);

                await this.ObservePlacementAsync(persistenceSession, placement, attemptCancellationToken);
            },
            cancellationToken);

        // An occurrence whose content was never retrieved has no MIME to read, so it is neither enriched nor counted as
        // unreadable.
        return new OccurrenceSynchronizationOutcome(
            storedEmailId,
            StoredEmailContentAvailability.ExceededSizeLimit,
            MimeCouldNotBeRead: false);
    }

    /// <summary>Writes down that the occurrence a copy created has been met, where this discovery was one.</summary>
    private async Task ObservePlacementAsync(
        IPersistenceSession persistenceSession,
        MailboxMutationRecord? placement,
        CancellationToken cancellationToken)
    {
        if (placement is null)
        {
            return;
        }

        await this.mutationStore.RecordPlacementObservedAsync(
            persistenceSession,
            placement.Id,
            this.timeProvider.GetUtcNow(),
            cancellationToken);
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
    /// <param name="StoredEmailId">The local email the occurrence was stored as, which a suppressed arrival is named by.</param>
    /// <param name="Availability">Whether the occurrence was stored with its content or as metadata only.</param>
    /// <param name="MimeCouldNotBeRead">Whether enrichment refused the payload that was stored.</param>
    private readonly record struct OccurrenceSynchronizationOutcome(
        StoredEmailId StoredEmailId,
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
/// <param name="Folder">The binding the run worked under, which is present exactly when <paramref name="Outcome" /> is <see cref="MailboxSynchronizationOutcome.Synchronized" />.</param>
/// <param name="StoredEmailCount">How many occurrences were stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
/// <param name="UnreadableMimeEmailCount">How many stored occurrences carried MIME that enrichment could not read.</param>
/// <param name="RelocatedEmailCount">
/// How many discovered occurrences were an email MailFathom had relocated into this folder, and so carried the existing
/// local email across instead of storing a second one. They are counted apart from the stored ones because no mail
/// arrived: the mailbox holds exactly what it held before the run.
/// </param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed emails when the run's batch budget ran out.</param>
/// <param name="Checkpoint">The progress the run ended on, which is present exactly when <paramref name="Outcome" /> is <see cref="MailboxSynchronizationOutcome.Synchronized" />.</param>
/// <param name="Reconciliation">What the run's backward pass found among the emails already stored for this folder.</param>
/// <param name="SuppressedChanges">
/// Every change this run discovered and did not raise, because a durable mutation record said MailFathom had made it —
/// both passes together, since one relocation arrives as an appearance in the forward pass and a disappearance in the
/// backward one. Without it a rule that files mail would match the mail it had just filed, indefinitely.
/// </param>
/// <remarks>
/// The binding is reported because resolution happens inside the run and a caller that wants to keep watching the
/// folder afterwards must watch the remote folder the alias actually resolved to. Re-resolving it outside the run would
/// cost a second listing and could answer differently, which is how an alias ends up watched in one place and
/// synchronized in another.
/// </remarks>
public sealed record MailboxSynchronizationResult(
    MailboxSynchronizationOutcome Outcome,
    MailFolderResolution? Folder,
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    int UnreadableMimeEmailCount,
    int RelocatedEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint? Checkpoint,
    MailboxReconciliationResult Reconciliation,
    IReadOnlyList<SuppressedMailboxChange> SuppressedChanges)
{
    /// <summary>Reports a run that reached its folder.</summary>
    /// <param name="folder">The binding the run worked under.</param>
    /// <param name="storedEmailCount">How many occurrences were stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
    /// <param name="unreadableMimeEmailCount">How many stored occurrences carried unreadable MIME.</param>
    /// <param name="relocatedEmailCount">How many discovered occurrences carried an existing local email across.</param>
    /// <param name="hasMoreEmails">Whether unprocessed emails remain.</param>
    /// <param name="checkpoint">The progress the run ended on.</param>
    /// <param name="reconciliation">What the run's backward pass found.</param>
    /// <param name="suppressedChanges">The changes the run recognized as MailFathom's own and did not raise.</param>
    /// <returns>A synchronized result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> or <paramref name="suppressedChanges" /> is <see langword="null" />.</exception>
    public static MailboxSynchronizationResult Synchronized(
        MailFolderResolution folder,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        int relocatedEmailCount,
        bool hasMoreEmails,
        SynchronizationCheckpoint checkpoint,
        MailboxReconciliationResult reconciliation,
        IReadOnlyList<SuppressedMailboxChange> suppressedChanges)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(suppressedChanges);

        return new MailboxSynchronizationResult(
            MailboxSynchronizationOutcome.Synchronized,
            folder,
            storedEmailCount,
            skippedOversizedEmailCount,
            unreadableMimeEmailCount,
            relocatedEmailCount,
            hasMoreEmails,
            checkpoint,
            reconciliation,
            suppressedChanges);
    }

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
            Folder: null,
            0,
            0,
            0,
            0,
            HasMoreEmails: false,
            Checkpoint: null,
            MailboxReconciliationResult.NothingToReconcile,
            SuppressedChanges: []);
    }
}
