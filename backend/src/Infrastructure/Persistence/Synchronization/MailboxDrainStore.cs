// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core implementation of what a held account's source still holds.</summary>
/// <remarks>
/// The reads use the scoped context because they join no transaction, and every write takes the caller's session: the
/// occurrence a message is cleared of and the removal record a drained erasure is relieved of are the drain's own
/// durable record, so both commit with whatever else the pass decided in the same unit of work.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxDrainStore(MailFathomDbContext readContext) : IMailboxDrainStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drainableFolders);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);

        if (drainableFolders.Count == 0)
        {
            return [];
        }

        var accountValue = account.Value;
        var aliasValues = drainableFolders.Select(static alias => alias.Value).ToArray();

        // Oldest first, over the filtered index the drain has of its own, so the read costs what the source still
        // holds rather than what the mailbox does. The folders are named inside the query rather than filtered out of
        // its answer, so the bound is spent on rows a command may actually name.
        var rows = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == accountValue
                && email.UidValidity != null
                && aliasValues.Contains(email.MailFolder.Alias))
            .OrderBy(email => email.ReceivedAt)
            .ThenBy(email => email.Id)
            .Take(maximumCandidates)
            .Select(email => new DrainRow(
                email.Id,
                email.UidValidity!.Value,
                email.Uid!.Value,
                email.ContentAvailability,
                email.ContentVerifiedAt,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.MailFolder.RemotePath,
                email.MailFolder.HierarchyDelimiter))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToCandidate(account))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAgainAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        if (emails.Count == 0)
        {
            return [];
        }

        var accountValue = account.Value;
        var identifiers = emails.Select(email => email.Value).ToArray();

        var rows = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == accountValue
                && identifiers.Contains(email.Id)
                && email.UidValidity != null)
            .Select(email => new DrainRow(
                email.Id,
                email.UidValidity!.Value,
                email.Uid!.Value,
                email.ContentAvailability,
                email.ContentVerifiedAt,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.MailFolder.RemotePath,
                email.MailFolder.HierarchyDelimiter))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToCandidate(account))];
    }

    /// <inheritdoc />
    public async Task RecordContentVerifiedAsync(
        IPersistenceSession session,
        StoredEmailId email,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await writeContext.StoredEmails
            .Where(stored => stored.Id == email.Value)
            .ExecuteUpdateAsync(
                update => update.SetProperty(stored => stored.ContentVerifiedAt, verifiedAt),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ClearOccurrencesAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> drained,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drained);

        if (drained.Count == 0)
        {
            return 0;
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var identifiers = drained.Select(email => email.Value).ToArray();

        // The account is compared as well as the identity, because an identifier is only this account's to clear: the
        // clearing is what makes MailFathom the only holder of the message, and a write that reached another account's
        // row would say so falsely.
        return await writeContext.StoredEmails
            .Where(stored => stored.MailboxAccountId == accountValue && identifiers.Contains(stored.Id))
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(stored => stored.UidValidity, (uint?)null)
                    .SetProperty(stored => stored.Uid, (uint?)null),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxSourceRemoval>> ReadSourceRemovalsAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        int maximumRemovals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drainableFolders);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRemovals, 1);

        if (drainableFolders.Count == 0)
        {
            return [];
        }

        var accountValue = account.Value;
        var aliasValues = drainableFolders.Select(static alias => alias.Value).ToArray();

        var rows = await readContext.MailboxSourceRemovals
            .AsNoTracking()
            .Where(removal => removal.MailboxAccountId == accountValue
                && aliasValues.Contains(removal.MailFolder!.Alias))
            .OrderBy(removal => removal.RecordedAt)
            .Take(maximumRemovals)
            .Select(removal => new RemovalRow(
                removal.Id,
                removal.UidValidity,
                removal.Uid,
                removal.MailFolder!.Alias,
                removal.MailFolder.ResolutionGeneration,
                removal.MailFolder.RemotePath,
                removal.MailFolder.HierarchyDelimiter))
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToRemoval(account))];
    }

    /// <inheritdoc />
    public async Task DeleteSourceRemovalsAsync(
        IPersistenceSession session,
        IReadOnlyCollection<MailboxSourceRemovalId> removals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(removals);

        if (removals.Count == 0)
        {
            return;
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var identifiers = removals.Select(removal => removal.Value).ToArray();

        await writeContext.MailboxSourceRemovals
            .Where(removal => identifiers.Contains(removal.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailboxDrainStanding> ReadStandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountValue = account.Value;

        var standing = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == accountValue && email.UidValidity != null)
            .GroupBy(email => email.ContentAvailability)
            .Select(group => new AvailabilityCount(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);

        var awaitingRemoval = await readContext.MailboxSourceRemovals
            .AsNoTracking()
            .CountAsync(removal => removal.MailboxAccountId == accountValue, cancellationToken);

        return new MailboxDrainStanding(
            standing.Sum(entry => entry.Count),
            CountOf(standing, StoredEmailContentAvailability.ExceededSizeLimit),
            CountOf(standing, StoredEmailContentAvailability.AwaitingStorageHeadroom),
            awaitingRemoval);
    }

    private static int CountOf(
        IReadOnlyList<AvailabilityCount> counted,
        StoredEmailContentAvailability availability) =>
        counted.FirstOrDefault(entry => entry.Availability == availability)?.Count ?? 0;

    private static MailFolderResolution ResolutionOf(string alias, int generation, string remotePath, string? delimiter) =>
        new(
            MailFolderAlias.Create(alias),
            MailFolderResolutionGeneration.Create(generation),
            MailFolderEntityResolver.ToRemotePath(remotePath, delimiter));

    private sealed record AvailabilityCount(StoredEmailContentAvailability Availability, int Count);

    private sealed record DrainRow(
        Guid Id,
        uint UidValidity,
        uint Uid,
        StoredEmailContentAvailability ContentAvailability,
        DateTimeOffset? ContentVerifiedAt,
        string Alias,
        int Generation,
        string RemotePath,
        string? HierarchyDelimiter)
    {
        internal MailboxDrainCandidate ToCandidate(MailAccountId account)
        {
            var folder = ResolutionOf(this.Alias, this.Generation, this.RemotePath, this.HierarchyDelimiter);

            return new MailboxDrainCandidate(
                StoredEmailId.Create(this.Id),
                EmailOccurrenceId.Create(
                    account,
                    folder.Id,
                    ImapUidValidity.Create(this.UidValidity),
                    ImapUid.Create(this.Uid)),
                folder,
                this.ContentAvailability,
                this.ContentVerifiedAt);
        }
    }

    private sealed record RemovalRow(
        Guid Id,
        uint UidValidity,
        uint Uid,
        string Alias,
        int Generation,
        string RemotePath,
        string? HierarchyDelimiter)
    {
        internal MailboxSourceRemoval ToRemoval(MailAccountId account)
        {
            var folder = ResolutionOf(this.Alias, this.Generation, this.RemotePath, this.HierarchyDelimiter);

            return new MailboxSourceRemoval(
                new MailboxSourceRemovalId(this.Id),
                EmailOccurrenceId.Create(
                    account,
                    folder.Id,
                    ImapUidValidity.Create(this.UidValidity),
                    ImapUid.Create(this.Uid)),
                folder);
        }
    }
}
