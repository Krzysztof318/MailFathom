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
using MailFathom.Infrastructure.Persistence.Emails;
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

        // Oldest first. A message with no occurrence is one the drain took off the source, which is exactly what the
        // restore puts back, and a message any append record names is out of the answer whether that record is settled
        // or standing, because in both cases the source may already hold the copy. Mail the mailbox regards as deleted
        // is out of it too: the drain takes a tombstoned row off the source like any other, and putting one back would
        // hand somebody a message they deleted before the account was ever held. No index serves the null test —
        // the drain's filtered index covers the occurrence being present rather than absent — so this is the account's
        // own rows scanned once per pass, which is what the run's budget already bounds.
        var rows = await readContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
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
                email.IsRemotelyAnswered,
                email.IsRemotelyFlagged,
                email.IsRemotelyDraft,
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
                email.IsRemotelyAnswered,
                email.IsRemotelyFlagged,
                email.IsRemotelyDraft,
                email.RemoteKeywords))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToCandidate(account))];
    }

    /// <inheritdoc />
    public async Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
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
    public Task<bool> WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken) =>
        this.StageRecordAsync(session, account, record, settledAt: null, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RecordUnrestorableAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken) =>
        this.StageRecordAsync(session, account, record, settledAt, cancellationToken);

    /// <inheritdoc />
    public async Task RecordPlacementAsync(
        IPersistenceSession session,
        MailboxRestoreAppendId record,
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var recordValue = record.Value;

        await writeContext.MailboxRestoreAppends
            .Where(append => append.Id == recordValue)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(append => append.AppendedUidValidity, (uint?)uidValidity.Value)
                    .SetProperty(append => append.AppendedUid, (uint?)uid.Value),
                cancellationToken);
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

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        if (!carried)
        {
            // The occurrence is held by something else, so this record will never be carried by a pass and clearing
            // the placement is what hands it to an operator: a record carrying one is read as work the next pass
            // finishes, and one that nothing finishes and nobody is offered would hold the account in its phase for
            // ever. What it stops being is not what it is — the copy is on the source and an operator will find it.
            await writeContext.MailboxRestoreAppends
                .Where(append => append.Id == record.Id.Value)
                .ExecuteUpdateAsync(
                    row => row
                        .SetProperty(append => append.AppendedUidValidity, (uint?)null)
                        .SetProperty(append => append.AppendedUid, (uint?)null),
                    cancellationToken);

            return false;
        }

        await writeContext.MailboxRestoreAppends
            .Where(append => append.Id == record.Id.Value)
            .ExecuteDeleteAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxRestoreConfirmation>> ReadConfirmableAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        var accountValue = account.Value;

        var rows = await readContext.MailboxRestoreAppends
            .AsNoTracking()
            .Where(append => append.MailboxAccountId == accountValue
                && append.SettledAt == null
                && append.AppendedUidValidity != null)
            .OrderBy(append => append.IssuedAt)
            .ThenBy(append => append.Id)
            .Take(maximumRecords)
            .Select(append => new ConfirmableRow(
                append.Id,
                append.StoredEmailId,
                append.FolderAlias,
                append.IssuedAt,
                append.AppendedUidValidity!.Value,
                append.AppendedUid!.Value))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(static row => row.ToConfirmation())];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxRestoreAppend>> ReadUnansweredAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        var accountValue = account.Value;

        // A record carrying a placement is not one an operator can help with: its outcome is completely known and the
        // next pass carries it. Offering one would invite a verdict that either deletes the row and lets a second
        // APPEND go out, or stamps it settled so the placement is never carried at all.
        var rows = await readContext.MailboxRestoreAppends
            .AsNoTracking()
            .Where(append => append.MailboxAccountId == accountValue
                && append.SettledAt == null
                && append.AppendedUidValidity == null)
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
                && append.SettledAt == null
                && append.AppendedUidValidity == null);

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
                // Erased or not: what pauses the restore is mail with no source folder to go back into, and an
                // erasure that left messages bound to the folder has left exactly that. Reading only the live folders
                // would let the restore append those messages nowhere and end the phase over them.
                folder => folder.MailboxAccountId == accountValue
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
            .Where(StoredEmailTombstone.IsNotTombstoned)
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
                append => append.MailboxAccountId == accountValue
                    && append.SettledAt == null
                    && append.AppendedUidValidity == null,
                cancellationToken);

        var awaitingConfirmation = await readContext.MailboxRestoreAppends
            .AsNoTracking()
            .CountAsync(
                append => append.MailboxAccountId == accountValue
                    && append.SettledAt == null
                    && append.AppendedUidValidity != null,
                cancellationToken);

        return new MailboxRestoreStanding(
            awaitingAppend,
            awaitingStateWrite,
            unanswered,
            awaitingConfirmation);
    }

    /// <summary>Stages one record for a message nothing has a record for yet.</summary>
    /// <remarks>
    /// The read is what makes this safe to commit under the optimistic retry policy: a replay whose first attempt in
    /// fact landed, and a pass on another replica that reached the message first, both find the row and stage nothing.
    /// It reads through the write context rather than the scoped one, so the answer is the transaction's own.
    /// </remarks>
    private async Task<bool> StageRecordAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        DateTimeOffset? settledAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var email = record.Email.Value;

        if (await writeContext.MailboxRestoreAppends.AnyAsync(
                append => append.StoredEmailId == email,
                cancellationToken))
        {
            return false;
        }

        writeContext.MailboxRestoreAppends.Add(new MailboxRestoreAppendEntity
        {
            Id = record.Id.Value,
            MailboxAccountId = account.Value,
            StoredEmailId = email,
            FolderAlias = record.SourceFolderAlias.Value,
            IssuedAt = record.IssuedAt,
            SettledAt = settledAt,
        });

        return true;
    }

    /// <summary>Reads how far the state walk has got, which is <see langword="null" /> before it has taken a step.</summary>
    private Task<Guid?> ReadStatePositionAsync(string account, CancellationToken cancellationToken) =>
        readContext.MailboxAccounts
            .AsNoTracking()
            .Where(row => row.Id == account)
            .Select(static row => row.RestoreStatePosition)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Builds the state the append or the records carry, from the columns a held account writes them to.</summary>
    private static RestoredEmailState StateOf(
        bool isSeen,
        bool isAnswered,
        bool isFlagged,
        bool isDraft,
        string[] keywords) =>
        new(isSeen, isAnswered, isFlagged, isDraft, RemoteEmailKeywords.Create(keywords));

    private sealed record CandidateRow(
        Guid Id,
        string? LocalSourceAlias,
        string OccurrenceAlias,
        bool IsSeen,
        bool IsAnswered,
        bool IsFlagged,
        bool IsDraft,
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
            StateOf(this.IsSeen, this.IsAnswered, this.IsFlagged, this.IsDraft, this.Keywords),
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
        bool IsAnswered,
        bool IsFlagged,
        bool IsDraft,
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
                StateOf(this.IsSeen, this.IsAnswered, this.IsFlagged, this.IsDraft, this.Keywords));
        }
    }

    private sealed record ConfirmableRow(
        Guid Id,
        Guid Email,
        string FolderAlias,
        DateTimeOffset IssuedAt,
        uint UidValidity,
        uint Uid)
    {
        internal MailboxRestoreConfirmation ToConfirmation() => new(
            new MailboxRestoreAppend(
                new MailboxRestoreAppendId(this.Id),
                StoredEmailId.Create(this.Email),
                MailFolderAlias.Create(this.FolderAlias),
                this.IssuedAt),
            ImapUidValidity.Create(this.UidValidity),
            ImapUid.Create(this.Uid));
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
