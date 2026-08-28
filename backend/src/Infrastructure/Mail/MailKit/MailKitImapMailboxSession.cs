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
    MailServerConnectionBudget connectionBudget,
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

        var connection = MailKitImapConnection.ForReading(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            connectionBudget,
            MailServerConnectionPurpose.Work,
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
            (_, openFolder, attemptToken) => this.SearchAndFetchBatchAsync(
                openFolder,
                lastSeenUid,
                maxEmailCount,
                synchronizationWindow,
                attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RemoteFolderWindowObservation> ObserveWindowWithoutSettingSeenAsync(
        IReadOnlyList<ImapUid> uids,
        ulong? reconciledThroughModSeq,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return Task.FromResult(RemoteFolderWindowObservation.FromDescribedOccurrences([], null));
        }

        return connection.ExecuteFolderReadAsync(
            (client, openFolder, attemptToken) => this.ObserveWindowAsync(
                client,
                openFolder,
                uids,
                reconciledThroughModSeq,
                attemptToken),
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
            (_, openFolder, attemptToken) => this.FetchRawMimeWithPeekAsync(openFolder, occurrenceId, maxRawMimeBytes, attemptToken),
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

    /// <summary>Chooses how much of the window the server has to describe, from what this connection advertises.</summary>
    /// <remarks>
    /// <para>
    /// The three paths reach the same end state and differ only in what the server is asked to send. Without
    /// <c>CONDSTORE</c> there is nothing to narrow by and the whole window is described. With it, the fetch is narrowed
    /// to what changed — but a narrowed fetch alone cannot tell an unchanged message from a deleted one, because both
    /// are silence, so something else has to establish which of the rest still exist: the <c>VANISHED</c> report where
    /// <c>QRESYNC</c> is on, and a UID search that returns identifiers and no message data where it is not.
    /// </para>
    /// <para>
    /// The capabilities are read from the connection this attempt is running on rather than from the session's opening,
    /// so a recovered connection to a server advertising something else is followed rather than remembered.
    /// </para>
    /// </remarks>
    private async Task<RemoteFolderWindowObservation> ObserveWindowAsync(
        IImapClient client,
        IMailFolder openFolder,
        IReadOnlyList<ImapUid> uids,
        ulong? reconciledThroughModSeq,
        CancellationToken cancellationToken)
    {
        var requestedUids = uids.Select(uid => new UniqueId(uid.Value)).ToArray();
        var folderModSeq = ReportedModSeqOf(openFolder);

        if (reconciledThroughModSeq is not { } changedSince || !client.Capabilities.HasFlag(ImapCapabilities.CondStore))
        {
            var described = await this.FetchFlagsAsync(
                openFolder,
                requestedUids,
                new FetchRequest(ReconciliationSummaryItems),
                cancellationToken);

            return RemoteFolderWindowObservation.FromDescribedOccurrences(described, folderModSeq);
        }

        return client.Capabilities.HasFlag(ImapCapabilities.QuickResync)
            ? await this.ObserveThroughVanishedReportAsync(openFolder, requestedUids, changedSince, folderModSeq, cancellationToken)
            : await this.ObserveThroughSurvivingUidSearchAsync(openFolder, requestedUids, changedSince, folderModSeq, cancellationToken);
    }

    /// <summary>Fetches what changed and lets the server's vanished report account for the rest.</summary>
    /// <remarks>
    /// With quick resynchronization enabled, a modification-sequence-limited fetch also reports the messages expunged
    /// since that sequence, so one command answers both halves of the question: the summaries name what changed, the
    /// vanished report names what is gone, and every other requested UID is a message the folder still holds unchanged.
    /// The subscription is removed in a <c>finally</c> because the folder outlives this read, and a handler left behind
    /// would record a later window's vanished messages into a set nobody is reading.
    /// </remarks>
    private async Task<RemoteFolderWindowObservation> ObserveThroughVanishedReportAsync(
        IMailFolder openFolder,
        UniqueId[] requestedUids,
        ulong changedSince,
        ulong? folderModSeq,
        CancellationToken cancellationToken)
    {
        var windowUids = requestedUids.Select(static uid => uid.Id).ToHashSet();
        var vanishedUids = new HashSet<uint>();

        void RecordVanishedMessages(object? sender, MessagesVanishedEventArgs eventArgs)
        {
            // A report is the server's own input and is kept only where it answers the question this read asked. A UID
            // outside the window says nothing about an occurrence this pass selected, and recording every one of them
            // would let a server decide how much this read allocates.
            foreach (var vanishedUid in eventArgs.UniqueIds.Where(vanished => windowUids.Contains(vanished.Id)))
            {
                vanishedUids.Add(vanishedUid.Id);
            }
        }

        openFolder.MessagesVanished += RecordVanishedMessages;

        IReadOnlyList<RemoteEmailFlagObservation> described;
        try
        {
            described = await this.FetchFlagsAsync(
                openFolder,
                requestedUids,
                new FetchRequest(ReconciliationSummaryItems) { ChangedSince = changedSince },
                cancellationToken);
        }
        finally
        {
            openFolder.MessagesVanished -= RecordVanishedMessages;
        }

        var describedUids = described.Select(static observation => observation.Uid.Value).ToHashSet();

        return new RemoteFolderWindowObservation(
            described,
            [
                .. requestedUids
                    .Where(uid => !describedUids.Contains(uid.Id) && !vanishedUids.Contains(uid.Id))
                    .Select(static uid => ImapUid.Create(uid.Id)),
            ],
            folderModSeq);
    }

    /// <summary>Asks which of the window's UIDs the folder still holds, then fetches flags only for the ones that changed.</summary>
    /// <remarks>
    /// A <c>UID SEARCH</c> over the window returns identifiers and nothing else, so establishing existence costs no
    /// message data and cannot set a flag. It is issued before the fetch on purpose: a message expunged between the two
    /// commands is then reported as still present and reconciled on a later window, which is the harmless way for that
    /// race to end. The other order would let a message the server proved exists fall out of both answers and be
    /// treated as deleted.
    /// </remarks>
    private async Task<RemoteFolderWindowObservation> ObserveThroughSurvivingUidSearchAsync(
        IMailFolder openFolder,
        UniqueId[] requestedUids,
        ulong changedSince,
        ulong? folderModSeq,
        CancellationToken cancellationToken)
    {
        var survivingUids = await openFolder.SearchAsync(SearchQuery.Uids(requestedUids), cancellationToken);
        var stillPresentUids = survivingUids.Select(static uid => uid.Id).ToHashSet();

        var described = await this.FetchFlagsAsync(
            openFolder,
            requestedUids,
            new FetchRequest(ReconciliationSummaryItems) { ChangedSince = changedSince },
            cancellationToken);
        var describedUids = described.Select(static observation => observation.Uid.Value).ToHashSet();

        return new RemoteFolderWindowObservation(
            described,
            [
                .. requestedUids
                    .Where(uid => stillPresentUids.Contains(uid.Id) && !describedUids.Contains(uid.Id))
                    .Select(static uid => ImapUid.Create(uid.Id)),
            ],
            folderModSeq);
    }

    /// <summary>Asks the folder to describe the supplied UIDs and reports one observation per UID it answered for.</summary>
    /// <remarks>
    /// <para>
    /// IMAP requires a server to ignore a UID a <c>UID FETCH</c> names that the folder no longer holds, rather than to
    /// fail the command, so an unnarrowed answer describes exactly the messages that still exist. That silence is the
    /// detection mechanism for a message deleted on the server, which is why nothing here fills a missing UID in with a
    /// default.
    /// </para>
    /// <para>
    /// A summary for a UID the command did not name is dropped. RFC 7162 lets a server volunteer information about
    /// messages another client has touched, and this read answers a question about one window: an occurrence outside it
    /// has not been selected for reconciliation and carries no local identity here to write against.
    /// </para>
    /// <para>
    /// A summary that answers for a requested UID without the flags the command asked for is refused rather than
    /// dropped, and that is the whole reason this method can throw. Dropping it would turn a message the server just
    /// proved exists into the same silence a deleted message produces, and an account configured to erase local copies
    /// would then destroy mail on the strength of an answer the server never gave.
    /// </para>
    /// </remarks>
    /// <exception cref="MailboxAnswerIncompleteException">Thrown when the server answered for an email without its flags.</exception>
    private async Task<IReadOnlyList<RemoteEmailFlagObservation>> FetchFlagsAsync(
        IMailFolder openFolder,
        UniqueId[] requestedUids,
        IFetchRequest fetchRequest,
        CancellationToken cancellationToken)
    {
        var summaries = await openFolder.FetchAsync(requestedUids, fetchRequest, cancellationToken);
        var observedAt = timeProvider.GetUtcNow();
        var windowUids = requestedUids.Select(static uid => uid.Id).ToHashSet();
        var answered = summaries.Where(summary => windowUids.Contains(summary.UniqueId.Id)).ToArray();

        if (answered.Any(static summary => summary.Flags is null))
        {
            throw new MailboxAnswerIncompleteException(accountId, folder.Alias, "FLAGS");
        }

        return
        [
            .. answered.Select(summary => new RemoteEmailFlagObservation(
                ImapUid.Create(summary.UniqueId.Id),
                SnapshotOf(summary, observedAt))),
        ];
    }

    /// <summary>Reads the folder's modification sequence, and reports none where the value cannot be one.</summary>
    /// <remarks>
    /// MailKit reports zero for a folder whose server sent no <c>HIGHESTMODSEQ</c>, which is the absence rather than a
    /// sequence. RFC 7162 bounds the value to 63 bits, so anything above that is a server contradicting the
    /// specification and is treated as no sequence at all rather than stored as an ordering key nothing else can
    /// compare against.
    /// </remarks>
    private static ulong? ReportedModSeqOf(IMailFolder openFolder) =>
        openFolder.HighestModSeq is > 0UL and <= (ulong)long.MaxValue ? openFolder.HighestModSeq : null;

    /// <summary>Reads the stored snapshot out of the one <c>FLAGS</c> answer the server gave for a message.</summary>
    /// <remarks>
    /// The five system flags and the keywords arrive in that same answer — MailKit splits the response into
    /// <see cref="IMessageSummary.Flags" /> and <see cref="IMessageSummary.Keywords" /> and both are filled by the one
    /// <see cref="MessageSummaryItems.Flags" /> item the reconciliation fetch already asks for — so reading the keywords
    /// costs no extra round trip and no wider request. What they are worth normalizing into is
    /// <see cref="RemoteEmailKeywords" />'s to say; MailKit reports them as the server wrote them.
    /// </remarks>
    private static RemoteEmailFlagSnapshot SnapshotOf(IMessageSummary summary, DateTimeOffset observedAt)
    {
        var flags = summary.Flags!.Value;

        return new RemoteEmailFlagSnapshot(
            observedAt,
            flags.HasFlag(MessageFlags.Seen),
            flags.HasFlag(MessageFlags.Answered),
            flags.HasFlag(MessageFlags.Flagged),
            flags.HasFlag(MessageFlags.Draft),
            flags.HasFlag(MessageFlags.Deleted),
            RemoteEmailKeywords.Create(summary.Keywords));
    }

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
        Stream stream;

        try
        {
            stream = await openFolder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        }
        catch (MessageNotFoundException)
        {
            // The folder answered that it holds no such UID, which is an answer rather than a failure: the message left
            // between the moment this run learned of it and the moment its body was asked for. Repeating the fetch would
            // receive the same answer, and failing the folder's run for it would stop every message behind it.
            return RemoteEmailContentFetchResult.NoLongerHeld();
        }

        await using (stream)
        {
            using var memory = new MemoryStream();

            if (!await TryCopyWithinLimitAsync(stream, memory, maxRawMimeBytes, cancellationToken))
            {
                return RemoteEmailContentFetchResult.ExceededSizeLimit();
            }

            return RemoteEmailContentFetchResult.Retrieved(new RemoteEmailContent(occurrenceId, memory.ToArray()));
        }
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
