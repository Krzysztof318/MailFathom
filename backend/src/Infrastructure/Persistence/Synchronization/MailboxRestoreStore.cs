// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core implementation of what a restoring account still owes its source.</summary>
/// <remarks>
/// The reads use the scoped context because they join no transaction, and every write takes the caller's session: the
/// append record, the occurrence it settles, and the position the state walk reached are each committed beside the
/// work they describe.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxRestoreStore(MailFathomDbContext readContext, IEmailMetadataRepository metadata)
    : IMailboxRestoreStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxRestoreCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        var accountValue = account.Value;

        // Oldest first, over the same filtered index the drain reads the other side of: a message with no occurrence
        // is one the drain took off the source, which is exactly what the restore puts back. A message any append
        // record names is out of the answer whether that record is settled or standing, because in both cases the
        // source may already hold the copy.
        var rows = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == accountValue
                && email.UidValidity == null
                && !readContext.MailboxRestoreAppends.Any(append => append.StoredEmailId == email.Id))
            .OrderBy(email => email.ReceivedAt)
            .ThenBy(email => email.Id)
            .Take(maximumCandidates)
            .Select(email => new CandidateRow(
                email.Id,
                email.LocalMailFolderId == null
                    ? email.MailFolder.Alias
                    : readContext.LocalMailFolders
                        .Where(folder => folder.Id == email.LocalMailFolderId)
                        .Select(folder => folder.SourceFolderAlias)
                        .FirstOrDefault(),
                email.MailFolder.Alias,
                email.IsRemotelySeen,
                email.IsRemotelyFlagged,
                email.RemoteKeywords,
                email.ReceivedAt,
                email.SentAt,
                email.StoredAt))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(static row => row.ToCandidate())];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxRestoredStateCandidate>> ReadStateCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        var accountValue = account.Value;
        var position = await this.ReadStatePositionAsync(accountValue, cancellationToken);

        var rows = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == accountValue
                && email.UidValidity != null
                && (position == null || email.Id.CompareTo(position!.Value) > 0))
            .OrderBy(email => email.Id)
            .Take(maximumCandidates)
            .Select(email => new StateRow(
                email.Id,
                email.UidValidity!.Value,
                email.Uid!.Value,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.MailFolder.RemotePath,
                email.MailFolder.HierarchyDelimiter,
                email.LocalMailFolderId == null
                    ? email.MailFolder.Alias
                    : readContext.LocalMailFolders
                        .Where(folder => folder.Id == email.LocalMailFolderId)
                        .Select(folder => folder.SourceFolderAlias)
                        .FirstOrDefault(),
                email.IsRemotelySeen,
                email.IsRemotelyFlagged,
                email.RemoteKeywords))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToCandidate(account))];
    }

    /// <inheritdoc />
    public async Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        DateTimeOffset writtenAt,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var reached = email.Value;

        // The walk only ever moves forward, so a late write from a pass whose lease has since moved cannot pull the
        // position back over messages another replica has already written records for.
        await writeContext.MailboxAccounts
            .Where(row => row.Id == accountValue
                && (row.RestoreStatePosition == null || row.RestoreStatePosition!.Value.CompareTo(reached) < 0))
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.RestoreStatePosition, reached),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        writeContext.MailboxRestoreAppends.Add(new MailboxRestoreAppendEntity
        {
            Id = record.Id.Value,
            MailboxAccountId = account.Value,
            StoredEmailId = record.Email.Value,
            FolderAlias = record.SourceFolderAlias.Value,
            IssuedAt = record.IssuedAt,
        });
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAppendAsync(
        IPersistenceSession session,
        MailboxRestoreAppend record,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(occurrence);

        // The same carry synchronization performs when it finds a stored message at a new occurrence, rather than a
        // second answer to what an occurrence is: it binds the folder the copy is in, writes the identity inside it,
        // and clears the observations that described where the message used to be.
        var carried = await metadata.TryCarryToOccurrenceAsync(
            session,
            record.Email,
            occurrence,
            cancellationToken);

        if (!carried)
        {
            return false;
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await writeContext.MailboxRestoreAppends
            .Where(append => append.Id == record.Id.Value)
            .ExecuteDeleteAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxRestoreAppend>> ReadUnansweredAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        var accountValue = account.Value;

        var rows = await readContext.MailboxRestoreAppends
            .AsNoTracking()
            .Where(append => append.MailboxAccountId == accountValue && append.SettledAt == null)
            .OrderBy(append => append.IssuedAt)
            .ThenBy(append => append.Id)
            .Take(maximumRecords)
            .Select(append => new AppendRow(
                append.Id,
                append.StoredEmailId,
                append.FolderAlias,
                append.IssuedAt))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(static row => row.ToRecord())];
    }

    /// <inheritdoc />
    public async Task<bool> SettleAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppendId record,
        bool sourceHoldsTheCopy,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var recordValue = record.Value;

        var standing = writeContext.MailboxRestoreAppends
            .Where(append => append.Id == recordValue
                && append.MailboxAccountId == accountValue
                && append.SettledAt == null);

        var settled = sourceHoldsTheCopy
            ? await standing.ExecuteUpdateAsync(
                update => update.SetProperty(append => append.SettledAt, (DateTimeOffset?)settledAt),
                cancellationToken)
            : await standing.ExecuteDeleteAsync(cancellationToken);

        return settled == 1;
    }

    /// <inheritdoc />
    public async Task<int> CountUnmappedFoldersHoldingMailAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountValue = account.Value;

        return await readContext.LocalMailFolders
            .AsNoTracking()
            .CountAsync(
                folder => folder.MailboxAccountId == accountValue
                    && folder.ErasedAt == null
                    && folder.SourceFolderAlias == null
                    && readContext.StoredEmails.Any(email => email.LocalMailFolderId == folder.Id),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailboxRestoreStanding> ReadStandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountValue = account.Value;
        var position = await this.ReadStatePositionAsync(accountValue, cancellationToken);

        var awaitingAppend = await readContext.StoredEmails
            .AsNoTracking()
            .CountAsync(
                email => email.MailboxAccountId == accountValue
                    && email.UidValidity == null
                    && !readContext.MailboxRestoreAppends.Any(append => append.StoredEmailId == email.Id),
                cancellationToken);

        var awaitingStateWrite = await readContext.StoredEmails
            .AsNoTracking()
            .CountAsync(
                email => email.MailboxAccountId == accountValue
                    && email.UidValidity != null
                    && (position == null || email.Id.CompareTo(position!.Value) > 0),
                cancellationToken);

        var unanswered = await readContext.MailboxRestoreAppends
            .AsNoTracking()
            .CountAsync(
                append => append.MailboxAccountId == accountValue && append.SettledAt == null,
                cancellationToken);

        return new MailboxRestoreStanding(awaitingAppend, awaitingStateWrite, unanswered);
    }

    /// <summary>Reads how far the state walk has got, which is <see langword="null" /> before it has taken a step.</summary>
    private Task<Guid?> ReadStatePositionAsync(string account, CancellationToken cancellationToken) =>
        readContext.MailboxAccounts
            .AsNoTracking()
            .Where(row => row.Id == account)
            .Select(static row => row.RestoreStatePosition)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Builds the state the append or the records carry, from the columns a held account writes them to.</summary>
    private static RestoredEmailState StateOf(bool isSeen, bool isFlagged, string[] keywords) =>
        new(isSeen, isFlagged, RemoteEmailKeywords.Create(keywords));

    private sealed record CandidateRow(
        Guid Id,
        string? LocalSourceAlias,
        string OccurrenceAlias,
        bool IsSeen,
        bool IsFlagged,
        string[] Keywords,
        DateTimeOffset? ReceivedAt,
        DateTimeOffset? SentAt,
        DateTimeOffset StoredAt)
    {
        /// <summary>Builds the candidate, taking the internal date from the best answer the row has about its arrival.</summary>
        /// <remarks>
        /// The recorded arrival first, because that is the source's own <c>INTERNALDATE</c> and is what makes a
        /// restored folder sort in every client as it did before. A message that arrived with none carries its
        /// <c>Date</c> header, and one with neither carries the instant it was first stored — which is the latest
        /// point MailFathom can honestly say it existed.
        /// </remarks>
        internal MailboxRestoreCandidate ToCandidate() => new(
            StoredEmailId.Create(this.Id),
            MailFolderAlias.Create(this.LocalSourceAlias ?? this.OccurrenceAlias),
            StateOf(this.IsSeen, this.IsFlagged, this.Keywords),
            this.ReceivedAt ?? this.SentAt ?? this.StoredAt);
    }

    private sealed record StateRow(
        Guid Id,
        uint UidValidity,
        uint Uid,
        string Alias,
        int Generation,
        string RemotePath,
        string? HierarchyDelimiter,
        string? LocalSourceAlias,
        bool IsSeen,
        bool IsFlagged,
        string[] Keywords)
    {
        internal MailboxRestoredStateCandidate ToCandidate(MailAccountId account)
        {
            var folder = new MailFolderResolution(
                MailFolderAlias.Create(this.Alias),
                MailFolderResolutionGeneration.Create(this.Generation),
                MailFolderEntityResolver.ToRemotePath(this.RemotePath, this.HierarchyDelimiter));

            return new MailboxRestoredStateCandidate(
                StoredEmailId.Create(this.Id),
                EmailOccurrenceId.Create(
                    account,
                    folder.Id,
                    ImapUidValidity.Create(this.UidValidity),
                    ImapUid.Create(this.Uid)),
                folder,
                MailFolderAlias.Create(this.LocalSourceAlias ?? this.Alias),
                StateOf(this.IsSeen, this.IsFlagged, this.Keywords));
        }
    }

    private sealed record AppendRow(Guid Id, Guid Email, string FolderAlias, DateTimeOffset IssuedAt)
    {
        internal MailboxRestoreAppend ToRecord() => new(
            new MailboxRestoreAppendId(this.Id),
            StoredEmailId.Create(this.Email),
            MailFolderAlias.Create(this.FolderAlias),
            this.IssuedAt);
    }
}
