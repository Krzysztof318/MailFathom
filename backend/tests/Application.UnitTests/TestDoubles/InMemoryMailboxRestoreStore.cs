// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what restoring accounts still owe their sources, with the rules the pass's safety rests on.</summary>
/// <remarks>
/// <para>
/// The rules reproduced are the ones the real store enforces in SQL and the pass would be wrong without: a message an
/// append record stands for is no longer a candidate, a record is written only where none already stands for the
/// message, a record carrying a placement is finished by the pass rather than offered to an operator, and the state
/// walk advances over one forward-only position per account rather than a stamp per message — which is what makes a
/// candidate the pass skipped observable as one nothing comes back for.
/// </para>
/// <para>
/// Everything is held per account, because that is a guarantee of the port rather than a detail of the store behind
/// it: every member takes the account whose mailbox it is about, and a fake that ignored it would let a verdict
/// authored against one account reach another's record and report that as correct. The session is accepted and
/// unused, because there is no transaction here to join.
/// </para>
/// </remarks>
internal sealed class InMemoryMailboxRestoreStore : IMailboxRestoreStore
{
    private readonly List<(MailAccountId Account, MailboxRestoreCandidate Candidate)> awaitingAppend = [];
    private readonly List<(MailAccountId Account, MailboxRestoredStateCandidate Candidate)> stateWalk = [];
    private readonly List<(MailAccountId Account, MailboxRestoreAppend Record)> standingAppends = [];
    private readonly Dictionary<MailboxRestoreAppendId, (ImapUidValidity UidValidity, ImapUid Uid)> placements = [];
    private readonly HashSet<(MailAccountId Account, StoredEmailId Email)> settledAppends = [];
    private readonly Dictionary<MailAccountId, int> statePositions = [];

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

    /// <summary>Reports how far one account's state walk has got, which is what a skipped candidate must not move.</summary>
    /// <param name="account">The account whose walk is being read.</param>
    /// <returns>The number of candidates the walk has passed.</returns>
    internal int StatePositionOf(MailAccountId account) => this.statePositions.GetValueOrDefault(account);

    internal InMemoryMailboxRestoreStore AwaitingAppendOf(
        MailAccountId account,
        params MailboxRestoreCandidate[] candidates)
    {
        this.awaitingAppend.AddRange(candidates.Select(candidate => (account, candidate)));

        return this;
    }

    internal InMemoryMailboxRestoreStore AwaitingStateWriteOf(
        MailAccountId account,
        params MailboxRestoredStateCandidate[] candidates)
    {
        this.stateWalk.AddRange(candidates.Select(candidate => (account, candidate)));

        return this;
    }

    internal InMemoryMailboxRestoreStore WithAppendStandingFor(MailAccountId account, MailboxRestoreAppend record)
    {
        this.standingAppends.Add((account, record));

        return this;
    }

    /// <summary>Leaves behind what a pass that ended between a fully answered append and its occurrence would leave.</summary>
    internal InMemoryMailboxRestoreStore WithPlacementRecordedFor(
        MailAccountId account,
        MailboxRestoreAppend record,
        ImapUidValidity uidValidity,
        ImapUid uid)
    {
        this.standingAppends.Add((account, record));
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
            [.. this.CandidatesOf(account).Take(maximumCandidates)]);
    }

    public Task<IReadOnlyList<MailboxRestoredStateCandidate>> ReadStateCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        return Task.FromResult<IReadOnlyList<MailboxRestoredStateCandidate>>(
            [.. this.WalkOf(account).Skip(this.StatePositionOf(account)).Take(maximumCandidates)]);
    }

    public Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        this.StateWritten.Add(email);

        var reached = this.WalkOf(account).FindIndex(candidate => candidate.Email == email);

        if (reached >= this.StatePositionOf(account))
        {
            this.statePositions[account] = reached + 1;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken)
    {
        if (this.HasRecordFor(account, record.Email))
        {
            return Task.FromResult(false);
        }

        this.IssuedAppends.Add(record);
        this.standingAppends.Add((account, record));

        return Task.FromResult(true);
    }

    public Task<bool> RecordUnrestorableAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        if (this.HasRecordFor(account, record.Email))
        {
            return Task.FromResult(false);
        }

        this.Unrestorable.Add(record.Email);
        this.settledAppends.Add((account, record.Email));
        this.awaitingAppend.RemoveAll(entry => entry.Account == account && entry.Candidate.Email == record.Email);

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

    public Task ReleasePlacementAsync(
        IPersistenceSession session,
        MailboxRestoreAppendId record,
        CancellationToken cancellationToken)
    {
        this.placements.Remove(record);

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
            this.placements.Remove(record.Id);

            return Task.FromResult(false);
        }

        this.standingAppends.RemoveAll(entry => entry.Record.Id == record.Id);
        this.placements.Remove(record.Id);
        this.awaitingAppend.RemoveAll(entry => entry.Candidate.Email == record.Email);

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
            .. this.RecordsOf(account)
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
            [.. this.UnansweredOf(account).OrderBy(record => record.IssuedAt).Take(maximumRecords)]);
    }

    public Task<bool> SettleAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppendId record,
        bool sourceHoldsTheCopy,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        var index = this.standingAppends.FindIndex(entry =>
            entry.Account == account
            && entry.Record.Id == record
            && !this.placements.ContainsKey(entry.Record.Id));

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var settled = this.standingAppends[index].Record;
        this.standingAppends.RemoveAt(index);

        if (!sourceHoldsTheCopy)
        {
            return Task.FromResult(true);
        }

        this.settledAppends.Add((account, settled.Email));
        this.awaitingAppend.RemoveAll(entry => entry.Account == account && entry.Candidate.Email == settled.Email);

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
            this.CandidatesOf(account).Count,
            this.WalkOf(account).Count - this.StatePositionOf(account),
            this.UnansweredOf(account).Count,
            this.RecordsOf(account).Count(record => this.placements.ContainsKey(record.Id))));

    private List<MailboxRestoreCandidate> CandidatesOf(MailAccountId account) =>
    [
        .. this.awaitingAppend
            .Where(entry => entry.Account == account && !this.HasRecordFor(account, entry.Candidate.Email))
            .Select(entry => entry.Candidate),
    ];

    private List<MailboxRestoredStateCandidate> WalkOf(MailAccountId account) =>
        [.. this.stateWalk.Where(entry => entry.Account == account).Select(entry => entry.Candidate)];

    private List<MailboxRestoreAppend> RecordsOf(MailAccountId account) =>
        [.. this.standingAppends.Where(entry => entry.Account == account).Select(entry => entry.Record)];

    /// <summary>Reads the records an operator is offered, which is the ones no placement has come back for.</summary>
    private List<MailboxRestoreAppend> UnansweredOf(MailAccountId account) =>
        [.. this.RecordsOf(account).Where(record => !this.placements.ContainsKey(record.Id))];

    /// <summary>Reports whether any record stands for a message, settled or not, which is what the unique index says.</summary>
    private bool HasRecordFor(MailAccountId account, StoredEmailId email) =>
        this.settledAppends.Contains((account, email))
        || this.standingAppends.Any(entry => entry.Account == account && entry.Record.Email == email);
}
