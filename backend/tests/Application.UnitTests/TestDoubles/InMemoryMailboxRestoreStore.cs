// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what a restoring account still owes its source, with the two rules the pass's safety rests on.</summary>
/// <remarks>
/// The rules reproduced are the ones the real store enforces in SQL and the pass would be wrong without: a message an
/// append record stands for is no longer a candidate, and a message whose state has been written down is no longer one
/// either. The session is accepted and unused, because there is no transaction here to join.
/// </remarks>
internal sealed class InMemoryMailboxRestoreStore : IMailboxRestoreStore
{
    private readonly List<MailboxRestoreCandidate> awaitingAppend = [];
    private readonly List<MailboxRestoredStateCandidate> awaitingStateWrite = [];
    private readonly List<MailboxRestoreAppend> standingAppends = [];

    /// <summary>Gets the appends written down before their command went out, in the order they were issued.</summary>
    internal List<MailboxRestoreAppend> IssuedAppends { get; } = [];

    /// <summary>Gets the occurrence written onto the message for each append that came back answered.</summary>
    internal List<EmailOccurrenceId> ConfirmedOccurrences { get; } = [];

    /// <summary>Gets the messages stamped as having had their state written down.</summary>
    internal List<StoredEmailId> StateWritten { get; } = [];

    /// <summary>Gets or sets whether something else already holds the occurrence an append is confirmed at.</summary>
    /// <remarks>
    /// What the real store answers when synchronization met the appended copy as an arrival and stored it beside the
    /// message it is a copy of, which is the one way a confirmation legitimately writes nothing.
    /// </remarks>
    internal bool OccurrenceIsAlreadyHeld { get; set; }

    /// <summary>Gets or sets how many of the account's local folders hold mail and map onto no source folder.</summary>
    internal int UnmappedFoldersHoldingMail { get; set; }

    internal InMemoryMailboxRestoreStore AwaitingAppendOf(params MailboxRestoreCandidate[] candidates)
    {
        this.awaitingAppend.AddRange(candidates);

        return this;
    }

    internal InMemoryMailboxRestoreStore AwaitingStateWriteOf(params MailboxRestoredStateCandidate[] candidates)
    {
        this.awaitingStateWrite.AddRange(candidates);

        return this;
    }

    internal InMemoryMailboxRestoreStore WithAppendStandingFor(MailboxRestoreAppend record)
    {
        this.standingAppends.Add(record);

        return this;
    }

    public Task<IReadOnlyList<MailboxRestoreCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        var standing = this.standingAppends.Select(record => record.Email).ToHashSet();

        return Task.FromResult<IReadOnlyList<MailboxRestoreCandidate>>(
        [
            .. this.awaitingAppend
                .Where(candidate => !standing.Contains(candidate.Email))
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
            [.. this.awaitingStateWrite.Take(maximumCandidates)]);
    }

    public Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        DateTimeOffset writtenAt,
        CancellationToken cancellationToken)
    {
        this.StateWritten.Add(email);
        this.awaitingStateWrite.RemoveAll(candidate => candidate.Email == email);

        return Task.CompletedTask;
    }

    public Task WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken)
    {
        this.IssuedAppends.Add(record);
        this.standingAppends.Add(record);

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
        this.awaitingAppend.RemoveAll(candidate => candidate.Email == record.Email);

        return Task.FromResult(true);
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

        if (!sourceHoldsTheCopy)
        {
            return Task.FromResult(true);
        }

        this.awaitingAppend.RemoveAll(candidate => candidate.Email == settled.Email);

        return Task.FromResult(true);
    }

    public Task<int> CountUnmappedFoldersHoldingMailAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.UnmappedFoldersHoldingMail);

    public Task<MailboxRestoreStanding> ReadStandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var standing = this.standingAppends.Select(record => record.Email).ToHashSet();

        return Task.FromResult(new MailboxRestoreStanding(
            this.awaitingAppend.Count(candidate => !standing.Contains(candidate.Email)),
            this.awaitingStateWrite.Count,
            this.standingAppends.Count));
    }
}
