// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>MailKit-backed factory for authenticated read-only IMAP folder sessions.</summary>
internal sealed class MailKitImapMailboxSessionFactory(
    Func<IImapClient> clientFactory,
    IImapAccountSettingsProvider settingsProvider,
    IMailAccessTokenSource accessTokenSource,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier,
    TimeProvider timeProvider) : IMailboxSessionFactory
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
            accessTokenSource,
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

        return new MailKitImapMailboxSession(accountId, folder, connection, timeProvider);
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
    MailKitImapConnection connection,
    TimeProvider timeProvider) : IMailboxSession
{
    /// <summary>The only items the reconciliation fetch may ever ask for.</summary>
    /// <remarks>
    /// Both are answered out of the folder's own state and neither retrieves a message, so the command IMAP receives is
    /// <c>UID FETCH &lt;set&gt; (FLAGS UID)</c> and no part of it can set <c>\Seen</c>. Adding any body, header, or
    /// <c>RFC822</c> item to this set would silently mark every reconciled message as read — the exact failure the
    /// read-only invariant exists to prevent — so it is a named constant that one test asserts against rather than an
    /// argument spelled out at a call site.
    /// </remarks>
    internal const MessageSummaryItems ReconciliationSummaryItems =
        MessageSummaryItems.Flags | MessageSummaryItems.UniqueId;

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
        MailSynchronizationWindow synchronizationWindow,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        if (lastSeenUid is { } checkpointUid && checkpointUid.Value >= UniqueId.MaxValue.Id)
        {
            return Task.FromResult(new RemoteEmailMetadataBatch([], checkpointUid, HasMore: false));
        }

        return connection.ExecuteFolderReadAsync(
            (openFolder, attemptToken) => this.SearchAndFetchBatchAsync(
                openFolder,
                lastSeenUid,
                maxEmailCount,
                synchronizationWindow,
                attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteEmailFlagObservation>> GetRemoteFlagsWithoutSettingSeenAsync(
        IReadOnlyList<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RemoteEmailFlagObservation>>([]);
        }

        return connection.ExecuteFolderReadAsync(
            (openFolder, attemptToken) => this.FetchFlagsAsync(openFolder, uids, attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RemoteEmailContentFetchResult> FetchEmailContentWithoutSettingSeenAsync(
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
        MailSynchronizationWindow synchronizationWindow,
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
        var matchingUids = await openFolder.SearchAsync(
            BuildRangeSearchQuery(searchRange, synchronizationWindow),
            cancellationToken);
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

    /// <summary>Asks the folder for the flags of the supplied UIDs and reports one observation per UID it answered for.</summary>
    /// <remarks>
    /// <para>
    /// IMAP requires a server to ignore a UID a <c>UID FETCH</c> names that the folder no longer holds, rather than to
    /// fail the command, so the answer describes exactly the messages that still exist. That silence is the detection
    /// mechanism for a message deleted on the server, which is why nothing here fills a missing UID in with a default.
    /// </para>
    /// <para>
    /// A summary that answers for a UID without the flags the command requested is refused rather than dropped, and that
    /// is the whole reason this method can throw. Dropping it would turn a message the server just proved exists into
    /// the same silence a deleted message produces, and an account configured to erase local copies would then destroy
    /// mail on the strength of an answer the server never gave.
    /// </para>
    /// </remarks>
    /// <exception cref="MailboxAnswerIncompleteException">Thrown when the server answered for an email without its flags.</exception>
    private async Task<IReadOnlyList<RemoteEmailFlagObservation>> FetchFlagsAsync(
        IMailFolder openFolder,
        IReadOnlyList<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        var requestedUids = uids.Select(uid => new UniqueId(uid.Value)).ToArray();
        var summaries = await openFolder.FetchAsync(requestedUids, ReconciliationSummaryItems, cancellationToken);
        var observedAt = timeProvider.GetUtcNow();

        if (summaries.Any(static summary => summary.Flags is null))
        {
            throw new MailboxAnswerIncompleteException(accountId, folder.Alias, "FLAGS");
        }

        return
        [
            .. summaries.Select(summary => new RemoteEmailFlagObservation(
                ImapUid.Create(summary.UniqueId.Id),
                SnapshotOf(summary.Flags!.Value, observedAt))),
        ];
    }

    /// <summary>Reads the five flags the stored snapshot keeps out of what the server reported.</summary>
    /// <remarks>
    /// Only the system flags a mailbox reader asks about are kept. Keywords a server or another client defines are
    /// deliberately not stored: they are mail-derived data nothing queries, and copying them would widen what a
    /// deletion or an export has to account for.
    /// </remarks>
    private static RemoteEmailFlagSnapshot SnapshotOf(MessageFlags flags, DateTimeOffset observedAt) => new(
        observedAt,
        flags.HasFlag(MessageFlags.Seen),
        flags.HasFlag(MessageFlags.Answered),
        flags.HasFlag(MessageFlags.Flagged),
        flags.HasFlag(MessageFlags.Draft),
        flags.HasFlag(MessageFlags.Deleted));

    private async Task<RemoteEmailContentFetchResult> FetchRawMimeWithPeekAsync(
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

        if (!await TryCopyWithinLimitAsync(stream, memory, maxRawMimeBytes, cancellationToken))
        {
            return RemoteEmailContentFetchResult.ExceededSizeLimit();
        }

        return RemoteEmailContentFetchResult.Retrieved(new RemoteEmailContent(occurrenceId, memory.ToArray()));
    }

    /// <summary>Builds the one UID SEARCH that carries both the remaining UID range and the account's earliest-arrival bound.</summary>
    /// <remarks>
    /// The date condition travels with the range in the same command, so the server never reports an excluded UID and
    /// this run never fetches one. MailKit renders <c>DeliveredAfter</c> as the IMAP <c>SINCE</c> key, which compares
    /// the server-assigned <c>INTERNALDATE</c> disregarding time and time zone and includes the named day itself; the
    /// envelope <c>Date</c> header MailFathom stores as the sent timestamp is deliberately not what is compared, for the
    /// reason <see cref="MailSynchronizationWindow" /> records.
    /// </remarks>
    private static SearchQuery BuildRangeSearchQuery(
        UniqueIdRange searchRange,
        MailSynchronizationWindow synchronizationWindow)
    {
        SearchQuery rangeQuery = SearchQuery.Uids(searchRange);

        return synchronizationWindow.EarliestEmailReceivedDate is { } earliestReceivedDate
            ? rangeQuery.And(SearchQuery.DeliveredAfter(earliestReceivedDate.ToDateTime(TimeOnly.MinValue)))
            : rangeQuery;
    }

    private static uint? GetHighestAssignedUid(UniqueId? uidNext)
    {
        if (uidNext is null || uidNext.Value.Id <= 1U)
        {
            return null;
        }

        return uidNext.Value.Id - 1U;
    }

    /// <summary>Copies the payload, stopping as soon as it grows past the limit rather than buffering the rest of it.</summary>
    /// <returns><see langword="true" /> when the whole payload fit within <paramref name="maxRawMimeBytes" />; otherwise <see langword="false" />.</returns>
    private static async Task<bool> TryCopyWithinLimitAsync(
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
                    return false;
                }

                await destination.WriteAsync(buffer[..read], cancellationToken);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
