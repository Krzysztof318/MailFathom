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
    private readonly IStoredEmailContentInventory contentInventory;
    private readonly RawMimeMemoryBudget rawMimeMemoryBudget;
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
        IStoredEmailContentInventory contentInventory,
        RawMimeMemoryBudget rawMimeMemoryBudget,
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
        this.contentInventory = contentInventory;
        this.rawMimeMemoryBudget = rawMimeMemoryBudget;
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

        var budget = new SynchronizationContentBudget(
            this.options.MaxContentBytesPerRun,
            await this.contentInventory.GetStoredContentBytesAsync(cancellationToken),
            this.options.MaxStoredContentBytes);

        var storedCount = 0;
        var skippedOversizedCount = 0;
        var deferredForStorageCount = 0;
        var unreadableMimeCount = 0;
        var relocatedCount = 0;
        var hasMore = true;
        var stoppedForContentBudget = false;
        var inspectedBatchCount = 0;
        var suppressedChanges = new List<SuppressedMailboxChange>();

        while (hasMore && !stoppedForContentBudget && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
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

            var processedThroughUid = default(ImapUid?);

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
                    processedThroughUid = metadata.OccurrenceId.Uid;

                    continue;
                }

                // The budget is tested before the occurrence is touched rather than while it is being stored, so a run
                // that runs out of bytes ends between two emails and never halfway through one. An email above the size
                // limit is exempt because it costs no fetch at all, and stopping the run for one would leave a
                // checkpoint stuck in front of a message no budget will ever cover.
                if (this.WouldFetchContentOf(metadata) && !budget.HasRunBudgetFor(this.AssumedContentCostOf(metadata)))
                {
                    stoppedForContentBudget = true;

                    break;
                }

                // A copy is stored like any other discovery, because the email it duplicates stays where it was and a
                // second live occurrence is a second local email under ADR 0008. What the record settles is only whose
                // act the arrival was.
                var copy = placement is { } candidate && candidate.Request.Mutation == MailboxMutation.Copy
                    ? candidate
                    : null;

                var occurrence = await this.StoreOccurrenceAsync(mailboxSession, metadata, copy, budget, cancellationToken);
                processedThroughUid = metadata.OccurrenceId.Uid;

                if (occurrence.Availability is null)
                {
                    // The folder stopped holding the occurrence between the batch that described it and the fetch. There
                    // is no message to record and nothing local to correct, so the checkpoint simply moves past it.
                    continue;
                }

                switch (occurrence.Availability)
                {
                    case StoredEmailContentAvailability.Available:
                        storedCount++;

                        break;

                    case StoredEmailContentAvailability.AwaitingStorageHeadroom:
                        deferredForStorageCount++;

                        break;

                    default:
                        skippedOversizedCount++;

                        break;
                }

                if (occurrence.MimeCouldNotBeRead)
                {
                    unreadableMimeCount++;
                }

                if (copy is not null && occurrence.StoredEmailId is { } storedEmailId)
                {
                    suppressedChanges.Add(new SuppressedMailboxChange(
                        MailboxChangeKind.EmailAppearedInFolder,
                        copy.Request.Mutation,
                        storedEmailId,
                        copy.Id));
                }
            }

            // A run stopped by its byte budget checkpoints through the last occurrence it actually handled rather than
            // through the cursor the batch reported, which covers emails this run never reached. The two are the same
            // whenever the batch was worked through, and only a truncated batch tells them apart.
            var advanceThroughUid = stoppedForContentBudget ? processedThroughUid : batch.InspectedThroughUid;

            if (advanceThroughUid is { } inspectedThroughUid)
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

            hasMore = stoppedForContentBudget || batch.HasMore;
        }

        // The backward pass runs over the same open session, so it costs no second connection and inspects only what the
        // forward pass has already committed. It is deliberately not gated on the forward pass having finished its
        // folder: a mailbox whose backfill spans many runs must still notice a deletion in the part of it that is
        // already stored.
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

        var refill = await this.RefillDeferredContentAsync(
            mailboxSession,
            accountId,
            folder,
            uidValidity,
            budget,
            cancellationToken);

        return MailboxSynchronizationResult.Synchronized(
            folder,
            storedCount,
            skippedOversizedCount,
            unreadableMimeCount + refill.UnreadableMimeEmailCount,
            relocatedCount,
            hasMore,
            checkpoint,
            reconciliation,
            [.. suppressedChanges, .. reconciliation.SuppressedChanges],
            new MailboxContentVolume(
                budget.FetchedBytes,
                budget.StoredBytes,
                budget.StoredContentBytesAtRunStart + budget.StoredBytes,
                deferredForStorageCount,
                refill.RefilledEmailCount,
                stoppedForContentBudget || refill.StoppedForContentBudget));
    }

    /// <summary>Determines whether an occurrence would cost this run a payload retrieval at all.</summary>
    /// <remarks>
    /// An email above the size limit is recorded from its envelope alone, so neither byte budget applies to it. Where
    /// the server advertised no size the answer is yes, because the only way to find out what it costs is to fetch it.
    /// </remarks>
    private bool WouldFetchContentOf(RemoteEmailMetadata metadata) =>
        metadata.SizeOctets <= this.options.MaxRawMimeBytes;

    /// <summary>States what one occurrence is assumed to cost before anything has read its payload.</summary>
    /// <remarks>
    /// A server is not obliged to report a size, and IMAP's own answer for one that does not is silence rather than a
    /// number. Treating that as nothing would exempt the message from both byte bounds — a server reporting no size for
    /// any message would let one run fetch a mailbox without limit and walk past the storage ceiling one message at a
    /// time — so an unreported size is charged the most a fetch of it could cost instead.
    /// </remarks>
    private long AssumedContentCostOf(RemoteEmailMetadata metadata) =>
        metadata.SizeOctets > 0 ? metadata.SizeOctets : this.options.MaxRawMimeBytes;

    /// <summary>Fetches the content of occurrences an earlier run recorded without theirs, as far as this run's limits allow.</summary>
    /// <remarks>
    /// <para>
    /// It runs after the forward and backward passes and never before them. New mail comes first because discovering it
    /// is what keeps the mailbox's timeline current, and the backward pass comes before this one because an occurrence
    /// the folder has stopped holding must be settled before anything asks the server for its body.
    /// </para>
    /// <para>
    /// Each refill is an ordinary store of the occurrence the row already names, so the content write is the same
    /// idempotent one a first discovery makes and the row is not duplicated. Nothing here touches the checkpoint: the
    /// forward pass already moved past these occurrences, and this pass closes the gap it left behind rather than
    /// walking the folder again.
    /// </para>
    /// </remarks>
    private async Task<DeferredContentRefill> RefillDeferredContentAsync(
        IMailboxSession mailboxSession,
        MailAccountId accountId,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        SynchronizationContentBudget budget,
        CancellationToken cancellationToken)
    {
        var awaiting = await this.contentInventory.GetEmailsAwaitingContentAsync(
            accountId,
            folder.Id,
            uidValidity,
            this.options.MaxMetadataBatchSize,
            cancellationToken);

        var refilledCount = 0;
        var unreadableMimeCount = 0;
        var stoppedForContentBudget = false;

        foreach (var metadata in awaiting)
        {
            if (!budget.HasStorageHeadroomFor(this.AssumedContentCostOf(metadata)))
            {
                break;
            }

            if (!budget.HasRunBudgetFor(this.AssumedContentCostOf(metadata)))
            {
                stoppedForContentBudget = true;

                break;
            }

            var occurrence = await this.StoreOccurrenceAsync(
                mailboxSession,
                metadata,
                placement: null,
                budget,
                cancellationToken);

            if (occurrence.Availability != StoredEmailContentAvailability.Available)
            {
                continue;
            }

            refilledCount++;

            if (occurrence.MimeCouldNotBeRead)
            {
                unreadableMimeCount++;
            }
        }

        return new DeferredContentRefill(refilledCount, unreadableMimeCount, stoppedForContentBudget);
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
        SynchronizationContentBudget budget,
        CancellationToken cancellationToken)
    {
        if (!this.WouldFetchContentOf(metadata))
        {
            return await this.RecordOccurrenceWithoutContentAsync(
                metadata,
                placement,
                StoredEmailContentAvailability.ExceededSizeLimit,
                cancellationToken);
        }

        // Storage is checked before the fetch rather than before the write, because a payload retrieved into a full
        // store would have cost the network read and the buffer for nothing. The occurrence is still recorded, so the
        // gap is queryable and a later run with room fetches exactly what this one left.
        if (!budget.HasStorageHeadroomFor(this.AssumedContentCostOf(metadata)))
        {
            return await this.RecordOccurrenceWithoutContentAsync(
                metadata,
                placement,
                StoredEmailContentAvailability.AwaitingStorageHeadroom,
                cancellationToken);
        }

        // The reservation spans the fetch, the MIME read, and the commit, because the payload is referenced throughout
        // and released only once the transaction that stored it has ended. What is reserved is the advertised size,
        // which a server that understated it can exceed by up to the size limit; a server that advertised no size at
        // all is charged that limit outright, since nothing short of the fetch says what it costs.
        using var reservation = await this.rawMimeMemoryBudget.ReserveAsync(
            this.AssumedContentCostOf(metadata),
            cancellationToken);

        var fetch = await mailboxSession.FetchEmailContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);

        if (fetch.Outcome == RemoteEmailContentFetchOutcome.NoLongerHeld)
        {
            return OccurrenceSynchronizationOutcome.NoLongerHeld;
        }

        if (fetch is not { Outcome: RemoteEmailContentFetchOutcome.Retrieved, Content: { } content })
        {
            // The advertised size understated the payload, so the occurrence is recorded without content instead of
            // being silently skipped past by the checkpoint. The run is charged the size limit, because that is where
            // the stream was abandoned and therefore what it read off the wire.
            budget.RecordFetched(this.options.MaxRawMimeBytes);

            return await this.RecordOccurrenceWithoutContentAsync(
                metadata,
                placement,
                StoredEmailContentAvailability.ExceededSizeLimit,
                cancellationToken);
        }

        budget.RecordFetched(content.RawMime.Length);

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

        budget.RecordStored(content.RawMime.Length);

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

    /// <summary>Records one occurrence from its envelope alone, with the reason its payload is not stored beside it.</summary>
    private async Task<OccurrenceSynchronizationOutcome> RecordOccurrenceWithoutContentAsync(
        RemoteEmailMetadata metadata,
        MailboxMutationRecord? placement,
        StoredEmailContentAvailability availability,
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
                    availability,
                    attemptCancellationToken);

                await this.ObservePlacementAsync(persistenceSession, placement, attemptCancellationToken);
            },
            cancellationToken);

        // An occurrence whose content was never retrieved has no MIME to read, so it is neither enriched nor counted as
        // unreadable.
        return new OccurrenceSynchronizationOutcome(storedEmailId, availability, MimeCouldNotBeRead: false);
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
    /// <param name="Availability">
    /// Whether the occurrence was stored with its content or as metadata only, and why. It is absent exactly when the
    /// folder had stopped holding the occurrence, which is the one case where nothing was written at all.
    /// </param>
    /// <param name="MimeCouldNotBeRead">Whether enrichment refused the payload that was stored.</param>
    private readonly record struct OccurrenceSynchronizationOutcome(
        StoredEmailId? StoredEmailId,
        StoredEmailContentAvailability? Availability,
        bool MimeCouldNotBeRead)
    {
        /// <summary>Reports an occurrence the folder no longer held when its payload was asked for.</summary>
        public static OccurrenceSynchronizationOutcome NoLongerHeld { get; } =
            new(StoredEmailId: null, Availability: null, MimeCouldNotBeRead: false);
    }

    /// <summary>States what the pass that closes earlier storage gaps did.</summary>
    /// <param name="RefilledEmailCount">How many occurrences had their content fetched and stored by this pass.</param>
    /// <param name="UnreadableMimeEmailCount">How many of those carried MIME that enrichment could not read.</param>
    /// <param name="StoppedForContentBudget">Whether the pass ended because the run had spent the bytes it may fetch.</param>
    private readonly record struct DeferredContentRefill(
        int RefilledEmailCount,
        int UnreadableMimeEmailCount,
        bool StoppedForContentBudget);
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
/// <param name="ContentVolume">How many bytes of mail content the run moved, and which of its byte limits it reached.</param>
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
    IReadOnlyList<SuppressedMailboxChange> SuppressedChanges,
    MailboxContentVolume ContentVolume)
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
    /// <param name="contentVolume">How many bytes the run moved and which of its byte limits it reached.</param>
    /// <returns>A synchronized result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" />, <paramref name="suppressedChanges" />, or <paramref name="contentVolume" /> is <see langword="null" />.</exception>
    public static MailboxSynchronizationResult Synchronized(
        MailFolderResolution folder,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        int relocatedEmailCount,
        bool hasMoreEmails,
        SynchronizationCheckpoint checkpoint,
        MailboxReconciliationResult reconciliation,
        IReadOnlyList<SuppressedMailboxChange> suppressedChanges,
        MailboxContentVolume contentVolume)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(suppressedChanges);
        ArgumentNullException.ThrowIfNull(contentVolume);

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
            suppressedChanges,
            contentVolume);
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
            SuppressedChanges: [],
            MailboxContentVolume.None);
    }
}
