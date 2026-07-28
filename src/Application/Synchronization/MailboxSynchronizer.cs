// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Folders;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Synchronization;

/// <summary>Coordinates read-only mailbox folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly MailFolderResolver folderResolver;
    private readonly IMailboxSessionFactory mailboxSessionFactory;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IPersistenceSessionFactory persistenceSessionFactory;
    private readonly IEmailMetadataRepository metadataRepository;
    private readonly IEmailContentStore contentStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(
        MailFolderResolver folderResolver,
        IMailboxSessionFactory mailboxSessionFactory,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        ISynchronizationCheckpointStore checkpointStore,
        IPersistenceSessionFactory persistenceSessionFactory,
        IEmailMetadataRepository metadataRepository,
        IEmailContentStore contentStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        this.folderResolver = folderResolver;
        this.mailboxSessionFactory = mailboxSessionFactory;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.checkpointStore = checkpointStore;
        this.persistenceSessionFactory = persistenceSessionFactory;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
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
    /// The alias is resolved against the server's advertised folders before anything is read, so a run always works
    /// under the binding that is durable at the moment it starts. An alias the server advertises no folder for ends
    /// this run and no other.
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
            return MailboxSynchronizationResult.FolderAliasUnresolved();
        }

        return await this.SynchronizeResolvedFolderAsync(
            accountId,
            folder,
            transportSecurityPolicy,
            cancellationToken);
    }

    private async Task<MailboxSynchronizationResult> SynchronizeResolvedFolderAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
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
        var hasMore = true;
        var inspectedBatchCount = 0;

        while (hasMore && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
        {
            inspectedBatchCount++;

            var batch = await mailboxSession.GetEmailBatchAfterAsync(checkpoint.LastSeenUid, this.options.MaxMetadataBatchSize, cancellationToken);
            foreach (var metadata in batch.Emails.OrderBy(email => email.OccurrenceId.Uid.Value))
            {
                var availability = await this.StoreOccurrenceAsync(mailboxSession, metadata, cancellationToken);
                if (availability == StoredEmailContentAvailability.Available)
                {
                    storedCount++;
                }
                else
                {
                    skippedOversizedCount++;
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

        return MailboxSynchronizationResult.Synchronized(
            storedCount,
            skippedOversizedCount,
            hasMore,
            checkpoint);
    }

    private async Task<StoredEmailContentAvailability> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
        }

        RemoteEmailContent content;
        try
        {
            content = await mailboxSession.FetchEmailContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
        }
        catch (EmailContentTooLargeException)
        {
            // The advertised size understated the payload, so the occurrence is recorded without content instead of
            // being silently skipped past by the checkpoint.
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
        }

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                var storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    StoredEmailContentAvailability.Available,
                    attemptCancellationToken);
                await this.contentStore.SaveContentAsync(
                    persistenceSession,
                    storedEmailId,
                    content,
                    attemptCancellationToken);
            },
            cancellationToken);

        return StoredEmailContentAvailability.Available;
    }

    private async Task<StoredEmailContentAvailability> RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    attemptCancellationToken);
            },
            cancellationToken);

        return StoredEmailContentAvailability.ExceededSizeLimit;
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
}

/// <summary>States whether a synchronization run reached its folder at all.</summary>
public enum MailboxSynchronizationOutcome
{
    /// <summary>The run synchronized the folder the alias is bound to.</summary>
    Synchronized = 0,

    /// <summary>The server advertised no folder matching the alias, so this folder was not synchronized.</summary>
    FolderAliasUnresolved = 1,
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
/// <param name="Outcome">Whether the run reached a folder.</param>
/// <param name="StoredEmailCount">How many occurrences were stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed emails when the run's batch budget ran out.</param>
/// <param name="Checkpoint">The progress the run ended on, which is present exactly when <paramref name="Outcome" /> is <see cref="MailboxSynchronizationOutcome.Synchronized" />.</param>
public sealed record MailboxSynchronizationResult(
    MailboxSynchronizationOutcome Outcome,
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint? Checkpoint)
{
    /// <summary>Reports a run that reached its folder.</summary>
    /// <param name="storedEmailCount">How many occurrences were stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many occurrences were stored as metadata only.</param>
    /// <param name="hasMoreEmails">Whether unprocessed emails remain.</param>
    /// <param name="checkpoint">The progress the run ended on.</param>
    /// <returns>A synchronized result.</returns>
    public static MailboxSynchronizationResult Synchronized(
        int storedEmailCount,
        int skippedOversizedEmailCount,
        bool hasMoreEmails,
        SynchronizationCheckpoint checkpoint) =>
        new(MailboxSynchronizationOutcome.Synchronized, storedEmailCount, skippedOversizedEmailCount, hasMoreEmails, checkpoint);

    /// <summary>Reports a configured alias the server advertised no folder for.</summary>
    /// <returns>An unresolved result, which describes no progress because none was attempted.</returns>
    public static MailboxSynchronizationResult FolderAliasUnresolved() =>
        new(MailboxSynchronizationOutcome.FolderAliasUnresolved, 0, 0, HasMoreEmails: false, Checkpoint: null);
}
