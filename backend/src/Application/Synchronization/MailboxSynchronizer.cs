// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
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
    private readonly IOwnerStoredContentLedger ownerContentLedger;
    private readonly IMailOwnership ownership;
    private readonly StoredContentCeiling storedContentCeiling;
    private readonly RawMimeMemoryBudget rawMimeMemoryBudget;
    private readonly IEmailMimeReader mimeReader;
    private readonly IMailboxMutationReconciliationStore mutationStore;
    private readonly IOutgoingMailFilingStore filingStore;
    private readonly MailboxReconciler reconciler;
    private readonly DerivedWorkGate derivedWorkGate;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly IMailSynchronizationPhaseTelemetry phaseTelemetry;
    private readonly SpamClassificationArrivals classificationArrivals;
    private readonly MailContactCollector contactCollection;
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
        IOwnerStoredContentLedger ownerContentLedger,
        IMailOwnership ownership,
        StoredContentCeiling storedContentCeiling,
        RawMimeMemoryBudget rawMimeMemoryBudget,
        IEmailMimeReader mimeReader,
        IMailboxMutationReconciliationStore mutationStore,
        IOutgoingMailFilingStore filingStore,
        MailboxReconciler reconciler,
        DerivedWorkGate derivedWorkGate,
        IDerivedWorkGateTelemetry gateTelemetry,
        IMailSynchronizationPhaseTelemetry phaseTelemetry,
        SpamClassificationArrivals classificationArrivals,
        MailContactCollector contactCollection,
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
        this.ownerContentLedger = ownerContentLedger;
        this.ownership = ownership;
        this.storedContentCeiling = storedContentCeiling;
        this.rawMimeMemoryBudget = rawMimeMemoryBudget;
        this.mimeReader = mimeReader;
        this.mutationStore = mutationStore;
        this.filingStore = filingStore;
        this.reconciler = reconciler;
        this.derivedWorkGate = derivedWorkGate;
        this.gateTelemetry = gateTelemetry;
        this.phaseTelemetry = phaseTelemetry;
        this.classificationArrivals = classificationArrivals;
        this.contactCollection = contactCollection;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Synchronizes one configured folder alias without mutating remote mailbox flags.</summary>
    /// <param name="account">The account to synchronize, named by its owner and its identifier.</param>
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
    /// <exception cref="RemoteFolderCreationRefusedException">
    /// Thrown when the mapping asked for its folder to be created and the mail server refused to hold one at that path.
    /// It fails this folder's run and no other, and it is deliberately not the unresolved alias below: a quota, a
    /// namespace that forbids the name, or a name the server will not accept asks the operator for something different
    /// from a path they mistyped.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no owner can be established for the account, which is what the stored-content bound is measured and
    /// charged against. An account never synchronized falls back to the deployment's owner record, so this reports a
    /// deployment holding no owner record or more than one — a state provisioning is supposed to make impossible, and
    /// therefore a defect rather than a condition a caller recovers from.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The alias is resolved against the server's advertised folders before anything is read, so a run always works
    /// under the binding that is durable at the moment it starts. An alias the server advertises no folder for ends
    /// this run and no other, unless its mapping asked for the folder to be created, in which case the run creates it
    /// and binds it exactly as it binds a folder it discovered.
    /// </para>
    /// <para>
    /// The account's synchronization window is read at the same moment as its transport security policy and bounds
    /// every batch the run requests. Excluded mail is left out by the server, so it costs no fetch, no MIME read, and
    /// no local write, and the folder checkpoint still advances across the excluded range so a run ends instead of
    /// rescanning it on every interval.
    /// </para>
    /// </remarks>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(
        MailAccountIdentity account,
        MailFolderMapping folderMapping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderMapping);

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(account.Id);

        var resolutionResult = await this.ResolveFolderAsync(
            account,
            folderMapping,
            transportSecurityPolicy,
            cancellationToken);

        if (resolutionResult.Resolution is not { } folder)
        {
            return MailboxSynchronizationResult.FolderNotResolved(resolutionResult.Outcome);
        }

        return await this.SynchronizeResolvedFolderAsync(
            account,
            folder,
            folderMapping.SpecialUse,
            transportSecurityPolicy,
            this.synchronizationWindowReader.GetWindow(account.Id),
            cancellationToken);
    }

    private async Task<MailboxSynchronizationResult> SynchronizeResolvedFolderAsync(
        MailAccountIdentity account,
        MailFolderResolution folder,
        MailFolderSpecialUse? folderRole,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailSynchronizationWindow synchronizationWindow,
        CancellationToken cancellationToken)
    {
        var persistedCheckpoint =
            await this.checkpointStore.GetCheckpointAsync(account, folder.Id, cancellationToken);

        var opened = await this.OpenSessionAsync(
            account,
            folder,
            transportSecurityPolicy,
            cancellationToken);

        await using var mailboxSession = opened.Session;

        var uidValidity = opened.UidValidity;
        var checkpoint = persistedCheckpoint?.UidValidity == uidValidity
            ? persistedCheckpoint
            : SynchronizationCheckpoint.None(uidValidity);

        // Whose mail this run is bringing in. A worker acts for nobody, so the owner arrives with the account the
        // supervisor resolved rather than being read off the account table again — and it cannot change while a run is
        // in flight, which is what let the previous read be one per run.
        var owner = account.Owner;

        // The mark is captured before the measurements, so bytes another run claims while these queries are in flight
        // are carried onto the readings rather than being overwritten by them. Both levels are measured here because
        // both bound this run, and neither figure answers for the other: the deployment's is what the disk fills with
        // and the owner's is what their payloads hold.
        var measurementMark = this.storedContentCeiling.MarkBefore(owner);
        this.storedContentCeiling.Observe(
            owner,
            await this.contentInventory.GetStoredContentBytesAsync(cancellationToken),
            await this.ownerContentLedger.ReadStoredContentBytesAsync(owner, cancellationToken),
            measurementMark);

        var budget = new SynchronizationContentBudget(this.options.MaxContentBytesPerRun);

        // Opened here for the reason the content budget above is, and with the same scope: what needs bounding is one
        // pass over one folder, and the account's own settings decide how much of it may reach the contact book.
        var collection = this.contactCollection.OpenRun(account, folderRole);

        var storedCount = 0;

        // What the run reports as arrived mail, decided here rather than by a query afterwards: the folder's role and
        // the server's own flag are both in hand at the moment an occurrence is stored, and neither survives to a later
        // reader — the stored row's flag belongs to the backward pass, and no consumer of the count should have to
        // reconstruct which of a run's messages were news.
        var arrivedCount = 0;
        var storesArrivingMail = folderRole == MailFolderSpecialUse.Inbox;
        var skippedOversizedCount = 0;
        var deferredForStorageCount = 0;
        var deferredForOwnerStorageCount = 0;
        var unreadableMimeCount = 0;
        var relocatedCount = 0;
        var hasMore = true;
        var stoppedForContentBudget = false;
        var inspectedBatchCount = 0;
        var suppressedChanges = new List<SuppressedMailboxChange>();

        using (var discovery = this.phaseTelemetry.BeginPhase(
            MailSynchronizationPhase.DiscoverEmails,
            cancellationToken))
        {
            while (hasMore && !stoppedForContentBudget && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
            {
                inspectedBatchCount++;

                var batch = await this.FetchBatchAsync(
                    mailboxSession,
                    checkpoint.LastSeenUid,
                    synchronizationWindow,
                    cancellationToken);
                var placements = await this.ReadPlacementsInBatchAsync(
                    account,
                    folder,
                    uidValidity,
                    batch,
                    cancellationToken);
                var filings = await this.ReadFilingsInBatchAsync(
                    account,
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

                    // A copy MailFathom filed of its own outgoing message arrives here as ordinary new mail, and the
                    // filing row is the only thing that says otherwise. It is stored like any other message and marked
                    // as this deployment's own, which is what keeps a rule that reacts to arriving mail from reacting
                    // to what the owner just sent.
                    var filing = FindFilingOf(filings, folder, metadata);

                    var occurrence = await this.StoreOccurrenceAsync(
                        mailboxSession,
                        metadata,
                        copy,
                        filing,
                        isFiledCopy: filing is not null,
                        owner,
                        budget,
                        collection,
                        cancellationToken);
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

                            // Nothing MailFathom itself put here is arrival, whichever of the two acts put it there:
                            // a copy filed of the owner's own outgoing message, which the person wrote, and a copy a
                            // rule made into a folder mapped as the inbox, which carries the source's flags and would
                            // otherwise announce as new mail the message it was copied from. Both are already
                            // recognized as this deployment's own act, and the run suppresses the appearance each of
                            // them raises for the same reason.
                            if (storesArrivingMail && !metadata.IsRemotelySeen && filing is null && copy is null)
                            {
                                arrivedCount++;
                            }

                            break;

                        // Counted apart by which ceiling deferred it, because the two ask an operator for different
                        // things: one for more room on the instance, the other for a larger share for one person.
                        case StoredEmailContentAvailability.AwaitingStorageHeadroom
                            when occurrence.ReachedStorageBound is StoredContentBound.Owner:
                            deferredForOwnerStorageCount++;

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
                        account,
                        folder,
                        persistedCheckpoint,
                        advancedCheckpoint,
                        cancellationToken);

                    checkpoint = advancedCheckpoint;
                    persistedCheckpoint = advancedCheckpoint;
                }

                hasMore = stoppedForContentBudget || batch.HasMore;
            }

            discovery.Completed();
        }

        // The backward pass runs over the same open session, so it costs no second connection and inspects only what the
        // forward pass has already committed. It is deliberately not gated on the forward pass having finished its
        // folder: a mailbox whose backfill spans many runs must still notice a deletion in the part of it that is
        // already stored.
        MailboxReconciliationResult reconciliation;

        using (var reconcilingFolder = this.phaseTelemetry.BeginPhase(
            MailSynchronizationPhase.ReconcileFolder,
            cancellationToken))
        {
            reconciliation = await this.reconciler.ReconcileAsync(
                mailboxSession,
                account,
                folder,
                uidValidity,
                checkpoint.ReconciledThroughModSeq,
                cancellationToken);

            checkpoint = await this.RecordReconciledModSeqAsync(
                account,
                folder,
                persistedCheckpoint,
                checkpoint,
                reconciliation.ReconciledThroughModSeq,
                cancellationToken);

            reconcilingFolder.Completed();
        }

        DeferredContentRefill refill;

        using (var refillingContent = this.phaseTelemetry.BeginPhase(
            MailSynchronizationPhase.RefillDeferredContent,
            cancellationToken))
        {
            refill = await this.RefillDeferredContentAsync(
                mailboxSession,
                account,
                folder,
                uidValidity,
                owner,
                budget,
                collection,
                cancellationToken);

            refillingContent.Completed();
        }

        return MailboxSynchronizationResult.Synchronized(
            folder,
            storedCount,
            arrivedCount,
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
                this.storedContentCeiling.OccupiedBytes,
                deferredForStorageCount,
                deferredForOwnerStorageCount,
                refill.RefilledEmailCount,
                stoppedForContentBudget || refill.StoppedForContentBudget));
    }

    /// <summary>Turns the configured alias into the folder the mail server advertises for it, as a stage of the run.</summary>
    /// <remarks>
    /// It is the first stage rather than preparation for the run, because it opens a session of its own and asks the
    /// server what it holds: an account whose folder listing became slow is attributable here and nowhere else.
    /// </remarks>
    private async Task<MailFolderResolutionResult> ResolveFolderAsync(
        MailAccountIdentity account,
        MailFolderMapping folderMapping,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        using var phase = this.phaseTelemetry.BeginPhase(MailSynchronizationPhase.ResolveFolder, cancellationToken);

        var resolutionResult = await this.folderResolver.ResolveAsync(
            account,
            folderMapping,
            transportSecurityPolicy,
            cancellationToken);

        phase.Completed();

        return resolutionResult;
    }

    /// <summary>Opens the read-only session the rest of the run works over, and reads what it is bound to.</summary>
    /// <remarks>
    /// The stage is the opening and not the session's lifetime: connecting, negotiating transport security,
    /// authenticating, selecting the folder, and asking which incarnation of it the server is serving is the part that
    /// waits on a mail server, and everything afterwards is reported by the stage that issued it. The validity is read
    /// here rather than by the caller so that a server slow to answer it is attributable to a stage at all.
    /// </remarks>
    private async Task<(IMailboxSession Session, ImapUidValidity UidValidity)> OpenSessionAsync(
        MailAccountIdentity account,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        using var phase = this.phaseTelemetry.BeginPhase(MailSynchronizationPhase.OpenSession, cancellationToken);

        var session = await this.mailboxSessionFactory.OpenReadOnlyAsync(
            account.Id,
            folder,
            transportSecurityPolicy,
            cancellationToken);

        try
        {
            var uidValidity = await session.GetUidValidityAsync(cancellationToken);

            phase.Completed();

            return (session, uidValidity);
        }
        catch
        {
            // The caller only takes ownership of a session this stage hands back, so one opened and then left
            // unreadable is released here rather than surviving the run that could not use it.
            await session.DisposeAsync();

            throw;
        }
    }

    /// <summary>Asks the mail server for one batch of the mail that follows the checkpoint, as a stage of the run.</summary>
    /// <remarks>
    /// Reported per batch, which is what separates a server slow to list a folder from local work slow to derive from
    /// what it listed — the two are otherwise one duration, and they are remedied in different places. The count is
    /// bounded by the run's batch limit, so a folder run publishes at most that many of these.
    /// </remarks>
    private async Task<RemoteEmailMetadataBatch> FetchBatchAsync(
        IMailboxSession mailboxSession,
        ImapUid? lastSeenUid,
        MailSynchronizationWindow synchronizationWindow,
        CancellationToken cancellationToken)
    {
        using var phase = this.phaseTelemetry.BeginPhase(MailSynchronizationPhase.FetchEmailBatch, cancellationToken);

        var batch = await mailboxSession.GetEmailBatchAfterAsync(
            lastSeenUid,
            this.options.MaxMetadataBatchSize,
            synchronizationWindow,
            cancellationToken);

        phase.Completed();

        return batch;
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
    /// <para>
    /// A row this pass completes may be a copy this deployment filed, and the inventory reports that beside the
    /// metadata so this pass does not have to assume a message which waited for storage headroom is somebody else's.
    /// What the answer decides is whether a spam verdict is asked for, and asking for one about the owner's own
    /// outgoing message is exactly what the join exists to prevent. The filing itself is not re-read here: the
    /// discovery that recorded this occurrence already met it and settled it, so the durable join on the stored email
    /// is what is left to read and what this pass carries forward.
    /// </para>
    /// </remarks>
    private async Task<DeferredContentRefill> RefillDeferredContentAsync(
        IMailboxSession mailboxSession,
        MailAccountIdentity account,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        MailOwnerId owner,
        SynchronizationContentBudget budget,
        ContactCollectionRun collection,
        CancellationToken cancellationToken)
    {
        var awaiting = await this.contentInventory.GetEmailsAwaitingContentAsync(
            account,
            folder.Id,
            uidValidity,
            this.options.MaxMetadataBatchSize,
            cancellationToken);

        var refilledCount = 0;
        var unreadableMimeCount = 0;
        var stoppedForContentBudget = false;

        foreach (var (metadata, isFiledCopy) in awaiting)
        {
            if (!budget.HasRunBudgetFor(this.AssumedContentCostOf(metadata)))
            {
                stoppedForContentBudget = true;

                break;
            }

            var occurrence = await this.StoreOccurrenceAsync(
                mailboxSession,
                metadata,
                placement: null,
                filing: null,
                isFiledCopy,
                owner,
                budget,
                collection,
                cancellationToken);

            // A ceiling filled up again while this pass ran. The pass stops asking rather than working down the queue
            // one refusal at a time: a claim is measured against the payload's own size, so a smaller occurrence behind
            // this one could still fit, and asking about each of them would spend a round trip per message to find the
            // few that do. Nothing is lost by stopping — the queue keeps what it held and the next pass picks it up
            // against whatever headroom the ceiling has by then.
            if (occurrence.Availability == StoredEmailContentAvailability.AwaitingStorageHeadroom)
            {
                break;
            }

            // Anything else is about this occurrence alone — it has left the folder, or its payload turned out to be
            // above the size limit — and says nothing about the ones behind it, which may still be fetchable now.
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
        MailAccountIdentity account,
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
            account,
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

    /// <summary>Reads the copies MailFathom filed into this folder that one of this batch's discoveries could be.</summary>
    /// <remarks>
    /// A batch that discovered nothing asks nothing, and the read is otherwise made once for the whole batch, so
    /// recognizing this deployment's own outgoing mail costs one query per batch on a folder nothing was ever filed
    /// into and never one per message. Both halves of the join travel in the same read, because a batch can carry
    /// discoveries of both kinds and a second query would double the cost of the case that answers nothing.
    /// </remarks>
    private async Task<IReadOnlyList<OutgoingMailFilingRecord>> ReadFilingsInBatchAsync(
        MailAccountIdentity account,
        MailFolderResolution folder,
        ImapUidValidity uidValidity,
        RemoteEmailMetadataBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.Emails.Count == 0)
        {
            return [];
        }

        return await this.filingStore.ReadFilingsAtAsync(
            account,
            folder.RemotePath,
            uidValidity,
            [.. batch.Emails.Select(static email => email.OccurrenceId.Uid)],
            [.. batch.Emails.Select(static email => email.InternetMessageId).OfType<string>().Distinct(StringComparer.Ordinal)],
            cancellationToken);
    }

    /// <summary>Finds the copy this discovery is, if it is one MailFathom filed.</summary>
    /// <remarks>
    /// The placement is preferred over the message identity, because the placement is the server's own statement about
    /// where it put the copy while the identity is a comparison MailFathom makes. The fallback exists for the servers
    /// that advertise no <c>UIDPLUS</c> and therefore never made that statement; every condition of the read is
    /// restated by the row itself, so a query widened later cannot quietly widen what counts as this system's own.
    /// </remarks>
    private static OutgoingMailFilingRecord? FindFilingOf(
        IReadOnlyList<OutgoingMailFilingRecord> filings,
        MailFolderResolution folder,
        RemoteEmailMetadata metadata) =>
        filings.FirstOrDefault(candidate => candidate.AccountsForPlacementAt(
            folder.RemotePath,
            metadata.OccurrenceId.UidValidity,
            metadata.OccurrenceId.Uid))
        ?? filings.FirstOrDefault(candidate => candidate.AccountsForMessageAt(
            folder.RemotePath,
            metadata.InternetMessageId));

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
                    record.Owner,
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
        MailAccountIdentity account,
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
            account,
            folder,
            persistedCheckpoint,
            reconciledCheckpoint,
            cancellationToken);

        return reconciledCheckpoint;
    }

    /// <summary>Stores one discovered occurrence, settling the copy that placed it in the same transaction where there was one.</summary>
    /// <remarks>
    /// <para>
    /// The placement observation belongs in the write that stores the email rather than beside it. A copy whose arrival
    /// was recorded but whose row was rolled back would answer for a change no local state reflects, and the next run
    /// would store the message as an arrival nobody accounted for.
    /// </para>
    /// <para>
    /// What this transaction deliberately does not contain is the cut. Redaction has already happened — it is part of
    /// the extraction above, so the text committed here is the text every enabled scanner has seen — but classification
    /// and the owner's rules have not, and both may still decide that this message is not derived from or that it
    /// belongs in a folder mapped differently. <see cref="Emails.Chunking.MailChunkingPass" /> is where the passages are
    /// cut, after those two stages, and the same pass is what offers the message for embedding.
    /// </para>
    /// <para>
    /// What follows the commit is the one hand-off this method makes: the message is asked to be classified, as a job
    /// the durable queue leases and retries per message. It is asked for only where the content was stored, because a
    /// message without content is reported unclassifiable rather than fetched, and only after the commit, because the
    /// queue enqueues against state that cannot roll back underneath it.
    /// </para>
    /// </remarks>
    private async Task<OccurrenceSynchronizationOutcome> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        MailboxMutationRecord? placement,
        OutgoingMailFilingRecord? filing,
        bool isFiledCopy,
        MailOwnerId owner,
        SynchronizationContentBudget budget,
        ContactCollectionRun collection,
        CancellationToken cancellationToken)
    {
        if (!this.WouldFetchContentOf(metadata))
        {
            return await this.RecordOccurrenceWithoutContentAsync(
                owner,
                metadata,
                placement,
                filing,
                StoredEmailContentAvailability.ExceededSizeLimit,
                StoredContentBound.None,
                cancellationToken);
        }

        // Room is claimed before the fetch rather than checked before the write, because a payload retrieved into a
        // full store would have cost the network read and the buffer for nothing, and because a check that every
        // concurrent run made against the same reading would let each of them believe it had the room the others were
        // taking. The occurrence is still recorded, so the gap is queryable and a later run with room fetches exactly
        // what this one left.
        var storageAttempt = this.storedContentCeiling.TryClaim(owner, this.AssumedContentCostOf(metadata));
        using var storageClaim = storageAttempt.Claim;

        if (storageClaim is null)
        {
            return await this.RecordOccurrenceWithoutContentAsync(
                owner,
                metadata,
                placement,
                filing,
                StoredEmailContentAvailability.AwaitingStorageHeadroom,
                storageAttempt.ReachedBound,
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
                owner,
                metadata,
                placement,
                filing,
                StoredEmailContentAvailability.ExceededSizeLimit,
                StoredContentBound.None,
                cancellationToken);
        }

        budget.RecordFetched(content.RawMime.Length);

        // Enrichment reads the payload this run already fetched, so it costs no second IMAP round trip and cannot reach
        // the remote \Seen flag. A message nobody can parse is counted and stepped over: the occurrence is stored with
        // only what the server's envelope reported, and the folder checkpoint still advances past it.
        var extraction = await this.mimeReader.ReadMetadataAsync(content, owner, cancellationToken);

        // Recorded where the message arrives, although nothing is derived from it here. The gate's answer about a
        // message nobody has scored yet is the only place the two withholding answers are ever reached — a later stage
        // sees a message the gate admits or does not see it at all — so a run that recorded nothing until the cut would
        // report a mailbox held behind classification exactly as it reports a mailbox with no mail in it.
        this.gateTelemetry.RecordAdmission(this.derivedWorkGate.Admit(new DerivedWorkCandidate(
            metadata.OccurrenceId.AccountId,
            metadata.OccurrenceId.FolderResolutionId.Alias,
            this.timeProvider.GetUtcNow(),
            StoredEmailContentAvailability.Available,
            Verdict: null)));

        var storedEmailId = default(StoredEmailId);

        // Before the unit of work rather than inside it. Under the object backend this reaches the endpoint, and the
        // metadata upsert below opens a transaction the moment it runs — it writes the search document with a set-based
        // update, which is atomic with the rest only inside one.
        var placedContent = await this.contentStore.PlaceContentAsync(
            EmailContentKind.IncomingMessage,
            content.RawMime,
            cancellationToken);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    owner,
                    metadata,
                    extraction.Metadata,
                    StoredEmailContentAvailability.Available,
                    attemptCancellationToken);
                await this.contentStore.SaveContentAsync(
                    persistenceSession,
                    storedEmailId,
                    content.OccurrenceId,
                    placedContent,
                    attemptCancellationToken);

                await this.ObservePlacementAsync(persistenceSession, placement, attemptCancellationToken);
                await this.ObserveFilingAsync(persistenceSession, filing, storedEmailId, attemptCancellationToken);
            },
            cancellationToken);

        budget.RecordStored(content.RawMime.Length);
        storageClaim.Settle(content.RawMime.Length);

        // Asked for after the commit rather than inside it, because the queue takes no persistence session by design:
        // work enqueued against a transaction that then rolled back would name a message no local state holds. It is one
        // insert per stored message, it is refused rather than queued once the deployment's backlog bound is reached,
        // and it is skipped outright where classification is off or does not cover this folder.
        //
        // A copy MailFathom filed of this deployment's own outgoing message is skipped too, which is the one place
        // scoring is decided by where a message came from rather than by what it holds: nothing this system composed
        // and sent needs a verdict about whether somebody sent it unsolicited, and a spam verdict on it would withhold
        // everything derived from a message the owner wrote and could file their own send into their junk folder. The
        // answer comes from the caller rather than from the filing beside it, because a run completing an occurrence a
        // previous one deferred meets no filing to settle and still stores the same copy.
        if (!isFiledCopy)
        {
            await this.classificationArrivals.ScheduleAsync(
                storedEmailId,
                metadata.OccurrenceId,
                owner,
                cancellationToken);
        }

        // The second hand-off, and the one that stays inside this pass rather than reaching a queue: the headers it
        // reads are the ones the extraction above already produced, so a contact costs a bounded number of indexed
        // reads and no round trip to the mail server. It follows the commit for the reason the enqueue does, and it is
        // skipped outright on an account whose owner never switched collection on.
        if (extraction.Metadata is { } extracted)
        {
            await this.contactCollection.CollectFromAsync(extracted, collection, cancellationToken);
        }

        return new OccurrenceSynchronizationOutcome(
            storedEmailId,
            StoredEmailContentAvailability.Available,
            extraction.Outcome != EmailMimeExtractionOutcome.Extracted);
    }

    /// <summary>Records one occurrence from its envelope alone, with the reason its payload is not stored beside it.</summary>
    private async Task<OccurrenceSynchronizationOutcome> RecordOccurrenceWithoutContentAsync(
        MailOwnerId owner,
        RemoteEmailMetadata metadata,
        MailboxMutationRecord? placement,
        OutgoingMailFilingRecord? filing,
        StoredEmailContentAvailability availability,
        StoredContentBound reachedStorageBound,
        CancellationToken cancellationToken)
    {
        var storedEmailId = default(StoredEmailId);

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    owner,
                    metadata,
                    extractedMetadata: null,
                    availability,
                    attemptCancellationToken);

                await this.ObservePlacementAsync(persistenceSession, placement, attemptCancellationToken);
                await this.ObserveFilingAsync(persistenceSession, filing, storedEmailId, attemptCancellationToken);
            },
            cancellationToken);

        // An occurrence whose content was never retrieved has no MIME to read, so it is neither enriched nor counted as
        // unreadable.
        return new OccurrenceSynchronizationOutcome(
            storedEmailId,
            availability,
            MimeCouldNotBeRead: false,
            reachedStorageBound);
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

    /// <summary>Writes down that a copy MailFathom filed has been met, and joins the stored email to the send it is of.</summary>
    /// <remarks>
    /// Both writes belong in the transaction that stores the email. The join is what keeps everything reacting to newly
    /// synchronized mail from reacting to the owner's own outgoing message, and recording it apart from the row it is
    /// about would let a rolled-back store leave a filing claiming to have been met by an email nothing holds.
    /// </remarks>
    private async Task ObserveFilingAsync(
        IPersistenceSession persistenceSession,
        OutgoingMailFilingRecord? filing,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        if (filing is null)
        {
            return;
        }

        await this.metadataRepository.RecordFiledFromOutgoingAsync(
            persistenceSession,
            storedEmailId,
            filing.OutgoingEmailId,
            cancellationToken);

        await this.filingStore.RecordFilingObservedAsync(
            persistenceSession,
            filing.OutgoingEmailId,
            filing.Filing,
            this.timeProvider.GetUtcNow(),
            cancellationToken);
    }

    // A checkpoint advance is attempted once rather than retried: the intended progress was derived from the state read
    // at the start of the run, so a competing advance invalidates the decision itself instead of only the write.
    private async Task CommitCheckpointAsync(
        MailAccountIdentity account,
        MailFolderResolution folder,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession =
            await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        await this.checkpointStore.SaveCheckpointAsync(
            persistenceSession,
            account,
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
    /// <param name="ReachedStorageBound">
    /// Which stored-content ceiling left the payload unstored, and <see cref="StoredContentBound.None" /> otherwise. It
    /// is beside the availability rather than folded into it, because the queue a deferred occurrence joins is the same
    /// one whichever ceiling deferred it and only an operator's action differs.
    /// </param>
    private readonly record struct OccurrenceSynchronizationOutcome(
        StoredEmailId? StoredEmailId,
        StoredEmailContentAvailability? Availability,
        bool MimeCouldNotBeRead,
        StoredContentBound ReachedStorageBound = StoredContentBound.None)
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
/// <param name="ArrivedEmailCount">
/// How many of the stored occurrences were mail arriving for the person: stored in the inbox, unread on the server when
/// the run stored them, and placed there by neither of MailFathom's own two acts — filing a copy of the owner's
/// outgoing message, and a rule copying a message into a folder mapped as the inbox. It is a subset of
/// <paramref name="StoredEmailCount" /> and is what a run reports as arrived mail, so no consumer has to rebuild the
/// rule from a count that means something wider.
/// </param>
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
    int ArrivedEmailCount,
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
    /// <param name="arrivedEmailCount">How many of those were unread inbox mail that MailFathom did not place there itself.</param>
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
        int arrivedEmailCount,
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
            arrivedEmailCount,
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
            0,
            HasMoreEmails: false,
            Checkpoint: null,
            MailboxReconciliationResult.NothingToReconcile,
            SuppressedChanges: [],
            MailboxContentVolume.None);
    }
}
