// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what a restoring account still owes its source, with the rules the pass's safety rests on.</summary>
/// <remarks>
/// The rules reproduced are the ones the real store enforces in SQL and the pass would be wrong without: a message an
/// append record stands for is no longer a candidate, a record is written only where none already stands for the
/// message, and the state walk advances over one forward-only position per account rather than a stamp per message —
/// which is what makes a candidate the pass skipped observable as one nothing comes back for. The session is accepted
/// and unused, because there is no transaction here to join.
/// </remarks>
internal sealed class InMemoryMailboxRestoreStore : IMailboxRestoreStore
{
    private readonly List<MailboxRestoreCandidate> awaitingAppend = [];
    private readonly List<MailboxRestoredStateCandidate> stateWalk = [];
    private readonly List<MailboxRestoreAppend> standingAppends = [];
    private readonly Dictionary<MailboxRestoreAppendId, (ImapUidValidity UidValidity, ImapUid Uid)> placements = [];
    private readonly HashSet<StoredEmailId> settledAppends = [];

    /// <summary>Gets the appends written down before their command went out, in the order they were issued.</summary>
    internal List<MailboxRestoreAppend> IssuedAppends { get; } = [];

    /// <summary>Gets the occurrence written onto the message for each append that came back answered.</summary>
    internal List<EmailOccurrenceId> ConfirmedOccurrences { get; } = [];

    /// <summary>Gets the messages stamped as having had their state written down.</summary>
    internal List<StoredEmailId> StateWritten { get; } = [];

    /// <summary>Gets the messages recorded as ones the restore can never put back.</summary>
    internal List<StoredEmailId> Unrestorable { get; } = [];

    /// <summary>Gets or sets whether something else already holds the occurrence an append is confirmed at.</summary>
    /// <remarks>
    /// What the real store answers when synchronization met the appended copy as an arrival and stored it beside the
    /// message it is a copy of, which is the one way a confirmation legitimately writes nothing.
    /// </remarks>
    internal bool OccurrenceIsAlreadyHeld { get; set; }

    /// <summary>Gets or sets how many of the account's local folders hold mail and map onto no source folder.</summary>
    internal int UnmappedFoldersHoldingMail { get; set; }

    /// <summary>Gets how far the state walk has got, which is what a skipped candidate must not move.</summary>
    internal int StatePosition { get; private set; }

    internal InMemoryMailboxRestoreStore AwaitingAppendOf(params MailboxRestoreCandidate[] candidates)
    {
        this.awaitingAppend.AddRange(candidates);

        return this;
    }

    internal InMemoryMailboxRestoreStore AwaitingStateWriteOf(params MailboxRestoredStateCandidate[] candidates)
    {
        this.stateWalk.AddRange(candidates);

        return this;
    }

    internal InMemoryMailboxRestoreStore WithAppendStandingFor(MailboxRestoreAppend record)
    {
        this.standingAppends.Add(record);

        return this;
    }

    /// <summary>Leaves behind what a pass that ended between a fully answered append and its occurrence would leave.</summary>
    internal InMemoryMailboxRestoreStore WithPlacementRecordedFor(
        MailboxRestoreAppend record,
        ImapUidValidity uidValidity,
        ImapUid uid)
    {
        this.standingAppends.Add(record);
        this.placements[record.Id] = (uidValidity, uid);

        return this;
    }

    public Task<IReadOnlyList<MailboxRestoreCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        return Task.FromResult<IReadOnlyList<MailboxRestoreCandidate>>(
        [
            .. this.awaitingAppend
                .Where(candidate => !this.HasRecordFor(candidate.Email))
                .Take(maximumCandidates),
        ]);
    }

    public Task<IReadOnlyList<MailboxRestoredStateCandidate>> ReadStateCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        return Task.FromResult<IReadOnlyList<MailboxRestoredStateCandidate>>(
            [.. this.stateWalk.Skip(this.StatePosition).Take(maximumCandidates)]);
    }

    public Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        this.StateWritten.Add(email);

        var reached = this.stateWalk.FindIndex(candidate => candidate.Email == email);

        if (reached >= this.StatePosition)
        {
            this.StatePosition = reached + 1;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken)
    {
        if (this.HasRecordFor(record.Email))
        {
            return Task.FromResult(false);
        }

        this.IssuedAppends.Add(record);
        this.standingAppends.Add(record);

        return Task.FromResult(true);
    }

    public Task<bool> RecordUnrestorableAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        if (this.HasRecordFor(record.Email))
        {
            return Task.FromResult(false);
        }

        this.Unrestorable.Add(record.Email);
        this.settledAppends.Add(record.Email);
        this.awaitingAppend.RemoveAll(candidate => candidate.Email == record.Email);

        return Task.FromResult(true);
    }

    public Task RecordPlacementAsync(
        IPersistenceSession session,
        MailboxRestoreAppendId record,
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken)
    {
        this.placements[record] = (uidValidity, uid);

        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAppendAsync(
        IPersistenceSession session,
        MailboxRestoreAppend record,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        this.ConfirmedOccurrences.Add(occurrence);

        if (this.OccurrenceIsAlreadyHeld)
        {
            return Task.FromResult(false);
        }

        this.standingAppends.RemoveAll(standing => standing.Id == record.Id);
        this.placements.Remove(record.Id);
        this.awaitingAppend.RemoveAll(candidate => candidate.Email == record.Email);

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<MailboxRestoreConfirmation>> ReadConfirmableAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        return Task.FromResult<IReadOnlyList<MailboxRestoreConfirmation>>(
        [
            .. this.standingAppends
                .Where(record => this.placements.ContainsKey(record.Id))
                .OrderBy(record => record.IssuedAt)
                .Take(maximumRecords)
                .Select(record => new MailboxRestoreConfirmation(
                    record,
                    this.placements[record.Id].UidValidity,
                    this.placements[record.Id].Uid)),
        ]);
    }

    public Task<IReadOnlyList<MailboxRestoreAppend>> ReadUnansweredAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        return Task.FromResult<IReadOnlyList<MailboxRestoreAppend>>(
            [.. this.standingAppends.OrderBy(record => record.IssuedAt).Take(maximumRecords)]);
    }

    public Task<bool> SettleAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppendId record,
        bool sourceHoldsTheCopy,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        var index = this.standingAppends.FindIndex(standing => standing.Id == record);

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var settled = this.standingAppends[index];
        this.standingAppends.RemoveAt(index);
        this.placements.Remove(record);

        if (!sourceHoldsTheCopy)
        {
            return Task.FromResult(true);
        }

        this.settledAppends.Add(settled.Email);
        this.awaitingAppend.RemoveAll(candidate => candidate.Email == settled.Email);

        return Task.FromResult(true);
    }

    public Task<int> CountUnmappedFoldersHoldingMailAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.UnmappedFoldersHoldingMail);

    public Task<MailboxRestoreStanding> ReadStandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MailboxRestoreStanding(
            this.awaitingAppend.Count(candidate => !this.HasRecordFor(candidate.Email)),
            this.stateWalk.Count - this.StatePosition,
            this.standingAppends.Count));

    /// <summary>Reports whether any record stands for a message, settled or not, which is what the unique index says.</summary>
    private bool HasRecordFor(StoredEmailId email) =>
        this.settledAppends.Contains(email) || this.standingAppends.Any(record => record.Email == email);
}
