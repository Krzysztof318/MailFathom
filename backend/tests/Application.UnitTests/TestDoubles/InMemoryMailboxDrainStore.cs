// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what a held account's source still has, with the pre-batch re-read the real store answers from.</summary>
internal sealed class InMemoryMailboxDrainStore : IMailboxDrainStore
{
    private readonly List<MailboxDrainCandidate> candidates = [];
    private readonly List<MailboxSourceRemoval> removals = [];

    /// <summary>Gets the messages this pass has cleared the occurrence of, in the order they were cleared.</summary>
    internal List<StoredEmailId> Cleared { get; } = [];

    /// <summary>Gets the messages whose payload was recorded as read back and matched.</summary>
    internal List<StoredEmailId> Verified { get; } = [];

    /// <summary>Gets the source removal records this pass deleted.</summary>
    internal List<MailboxSourceRemovalId> DeletedRemovals { get; } = [];

    /// <summary>Gets or sets what the pre-batch re-read answers, or <see langword="null" /> to answer from what is held.</summary>
    /// <remarks>
    /// This is how a test writes the case the re-read exists for: a row erased, moved, or drained by another replica
    /// between the selection and the command.
    /// </remarks>
    internal IReadOnlyList<MailboxDrainCandidate>? ReReadAnswer { get; set; }

    internal InMemoryMailboxDrainStore Holding(params MailboxDrainCandidate[] held)
    {
        this.candidates.AddRange(held);

        return this;
    }

    internal InMemoryMailboxDrainStore AwaitingRemovalOf(params MailboxSourceRemoval[] awaiting)
    {
        this.removals.AddRange(awaiting);

        return this;
    }

    public Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drainableFolders);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        return Task.FromResult<IReadOnlyList<MailboxDrainCandidate>>(
        [
            .. this.candidates
                .Where(candidate => drainableFolders.Contains(candidate.Folder.Alias))
                .Take(maximumCandidates),
        ]);
    }

    public Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAgainAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        var asked = emails.ToHashSet();
        var answered = this.ReReadAnswer ?? this.candidates;

        return Task.FromResult<IReadOnlyList<MailboxDrainCandidate>>(
            [.. answered.Where(candidate => asked.Contains(candidate.Email))]);
    }

    public Task RecordContentVerifiedAsync(
        IPersistenceSession session,
        StoredEmailId email,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        this.Verified.Add(email);

        var index = this.candidates.FindIndex(candidate => candidate.Email == email);

        if (index >= 0)
        {
            this.candidates[index] = this.candidates[index] with { ContentVerifiedAt = verifiedAt };
        }

        return Task.CompletedTask;
    }

    public Task<int> ClearOccurrencesAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> drained,
        CancellationToken cancellationToken)
    {
        this.Cleared.AddRange(drained);
        this.candidates.RemoveAll(candidate => drained.Contains(candidate.Email));

        return Task.FromResult(drained.Count);
    }

    public Task<IReadOnlyList<MailboxSourceRemoval>> ReadSourceRemovalsAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        int maximumRemovals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drainableFolders);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRemovals, 1);

        return Task.FromResult<IReadOnlyList<MailboxSourceRemoval>>(
        [
            .. this.removals
                .Where(removal => drainableFolders.Contains(removal.Folder.Alias))
                .Take(maximumRemovals),
        ]);
    }

    public Task DeleteSourceRemovalsAsync(
        IPersistenceSession session,
        IReadOnlyCollection<MailboxSourceRemovalId> removed,
        CancellationToken cancellationToken)
    {
        this.DeletedRemovals.AddRange(removed);
        this.removals.RemoveAll(removal => removed.Contains(removal.Id));

        return Task.CompletedTask;
    }

    public Task<MailboxDrainStanding> ReadStandingAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(new MailboxDrainStanding(
            this.candidates.Count,
            this.candidates.Count(candidate =>
                candidate.ContentAvailability is StoredEmailContentAvailability.ExceededSizeLimit),
            this.candidates.Count(candidate =>
                candidate.ContentAvailability is StoredEmailContentAvailability.AwaitingStorageHeadroom),
            this.removals.Count));
}
