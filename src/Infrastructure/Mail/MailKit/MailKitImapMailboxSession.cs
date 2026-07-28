// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MailKit;
using MailKit.Search;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Resilience;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Resilience;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>MailKit-backed factory for authenticated read-only IMAP folder sessions.</summary>
internal sealed class MailKitImapMailboxSessionFactory(
    Func<IMailKitImapClient> clientFactory,
    IImapAccountSettingsProvider settingsProvider,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier) : IMailboxSessionFactory
{
    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the connection passes to the returned session; an establishment failure disposes it here instead.")]
    public async Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var connection = new MailKitImapConnection(
            clientFactory,
            settingsProvider,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            folder,
            transportSecurityPolicy);

        try
        {
            await connection.EnsureOpenFolderAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return new MailKitImapMailboxSession(accountId, folder, connection);
    }
}

/// <summary>Reads one folder over a connection that re-establishes itself when the server drops it.</summary>
/// <remarks>
/// Both reads below are repeatable by construction: a UID search and a <c>BODY.PEEK</c> fetch return the same answer
/// however often they are issued and change nothing on the server. That is what makes them safe to run under a retry
/// at all, and it is why no operation that could set a flag may ever be added to this type.
/// </remarks>
internal sealed class MailKitImapMailboxSession(
    MailAccountId accountId,
    MailFolderResolution folder,
    MailKitImapConnection connection) : IMailboxSession
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    /// <inheritdoc />
    public async Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken)
    {
        var openFolder = await connection.EnsureOpenFolderAsync(cancellationToken);

        return ImapUidValidity.Create(openFolder.UidValidity);
    }

    /// <inheritdoc />
    public Task<RemoteEmailMetadataBatch> GetEmailBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        if (lastSeenUid is { } checkpointUid && checkpointUid.Value >= UniqueId.MaxValue.Id)
        {
            return Task.FromResult(new RemoteEmailMetadataBatch([], checkpointUid, HasMore: false));
        }

        return connection.ExecuteFolderReadAsync(
            (openFolder, attemptToken) => this.SearchAndFetchBatchAsync(openFolder, lastSeenUid, maxEmailCount, attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RemoteEmailContent> FetchEmailContentWithoutSettingSeenAsync(
        EmailOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRawMimeBytes);

        return connection.ExecuteFolderReadAsync(
            (openFolder, attemptToken) => this.FetchRawMimeWithPeekAsync(openFolder, occurrenceId, maxRawMimeBytes, attemptToken),
            cancellationToken);
    }

    private async Task<RemoteEmailMetadataBatch> SearchAndFetchBatchAsync(
        IMailFolder openFolder,
        ImapUid? lastSeenUid,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        var minValue = lastSeenUid is { } uid ? uid.Value + 1U : 1U;
        var highestAssignedUid = GetHighestAssignedUid(openFolder.UidNext);
        if (highestAssignedUid is null || minValue > highestAssignedUid.Value)
        {
            return new RemoteEmailMetadataBatch([], lastSeenUid, HasMore: false);
        }

        // UID SEARCH returns identifiers only, so scanning the whole remaining assigned range stays cheap and lets the batch
        // be bounded by email count rather than by UID-space width. Bounding by UID-space width would advance a sparse
        // folder by at most maxEmailCount UIDs per batch and make an initial backfill take an impractical number of runs.
        var searchRange = new UniqueIdRange(new UniqueId(minValue), new UniqueId(highestAssignedUid.Value));
        var matchingUids = await openFolder.SearchAsync(SearchQuery.Uids(searchRange), cancellationToken);
        var assignedUids = matchingUids
            .Where(candidate => candidate.Id >= minValue && candidate.Id <= highestAssignedUid.Value)
            .OrderBy(candidate => candidate.Id)
            .ToArray();

        var batchedUids = assignedUids.Take(maxEmailCount).ToArray();
        var hasMore = assignedUids.Length > batchedUids.Length;

        // Everything the search covered has been inspected, so an exhausted range checkpoints to the highest assigned UID
        // even when it matched nothing. A truncated batch may only checkpoint through the last UID actually fetched.
        var inspectedThroughUid = hasMore ? batchedUids[^1].Id : highestAssignedUid.Value;
        var summaries = batchedUids.Length == 0
            ? []
            : await openFolder.FetchAsync(batchedUids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);

        var uidValidity = ImapUidValidity.Create(openFolder.UidValidity);
        var messages = summaries.Select(summary => new RemoteEmailMetadata(
            EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, ImapUid.Create(summary.UniqueId.Id)),
            summary.Envelope?.MessageId,
            summary.Envelope?.Subject,
            summary.Envelope?.Date?.ToUniversalTime(),
            summary.Size ?? 0)).ToArray();

        return new RemoteEmailMetadataBatch(messages, ImapUid.Create(inspectedThroughUid), hasMore);
    }

    private async Task<RemoteEmailContent> FetchRawMimeWithPeekAsync(
        IMailFolder openFolder,
        EmailOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        if (occurrenceId.AccountId != accountId ||
            occurrenceId.FolderResolutionId != folder.Id ||
            occurrenceId.UidValidity.Value != openFolder.UidValidity)
        {
            throw new ArgumentException("The message occurrence does not belong to the open mailbox session.", nameof(occurrenceId));
        }

        // The folder is selected read-only and MailKit's GetStreamAsync(uid) issues "UID FETCH <uid> (BODY.PEEK[])", so neither
        // the selection mode nor the fetch item is capable of setting the remote \Seen flag. Changing this call to any
        // non-PEEK retrieval or to a StoreAsync-based flag update would break the read-only synchronization invariant,
        // including on the attempt that follows a recovered connection.
        await using var stream = await openFolder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        using var memory = new MemoryStream();

        await CopyToMemoryWithLimitAsync(occurrenceId, stream, memory, maxRawMimeBytes, cancellationToken);

        return new RemoteEmailContent(occurrenceId, memory.ToArray());
    }

    private static uint? GetHighestAssignedUid(UniqueId? uidNext)
    {
        if (uidNext is null || uidNext.Value.Id <= 1U)
        {
            return null;
        }

        return uidNext.Value.Id - 1U;
    }

    private static async Task CopyToMemoryWithLimitAsync(
        EmailOccurrenceId occurrenceId,
        Stream source,
        MemoryStream destination,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var buffer = rentedBuffer.AsMemory();
            long totalBytes = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalBytes += read;
                if (totalBytes > maxRawMimeBytes)
                {
                    throw new EmailContentTooLargeException(occurrenceId, totalBytes, maxRawMimeBytes);
                }

                await destination.WriteAsync(buffer[..read], cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
