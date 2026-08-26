// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Reads where each folder of a scope sits on its mail server, and how much of it this deployment holds.</summary>
/// <remarks>
/// <para>
/// Two queries rather than one, because the two halves are bounded by different things and a join would multiply them.
/// The bindings are one row per alias and the counts are an aggregate over the mail those aliases hold, so PostgreSQL
/// performs both reductions and what crosses the boundary is one row per folder either way.
/// </para>
/// <para>
/// The bindings query keeps the newest generation of each alias, because an alias rebound after a server recreated the
/// folder names the folder it names now. The counts deliberately do not: every generation's mail is still listed under
/// the one alias, so an alias that was rebound counts what both bindings stored.
/// </para>
/// <para>
/// The counts admit exactly the rows a mailbox read would return, through the same predicate those reads compose. A
/// count assembled from its own idea of what is readable would be a figure no listing of the folder could reproduce —
/// tombstoned mail counted into a folder that will not show it, or another owner's rows counted into this owner's
/// total.
/// </para>
/// <para>
/// Both queries are composed by static methods rather than inline, so what they ask PostgreSQL for can be read without
/// a database. Neither shape is one a reader can check by eye — a correlated maximum and a filtered aggregate both
/// return plausible rows when they translate to something else — and this class runs against nothing a unit test has.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailFolderReader(MailFathomDbContext dbContext) : IStoredMailFolderReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredMailFolder>> ReadAsync(
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var bindings = await NewestBindingsIn(dbContext, scope).ToArrayAsync(cancellationToken);
        var counts = await FolderCountsIn(dbContext, scope).ToArrayAsync(cancellationToken);

        var countsByFolder = counts.ToDictionary(
            static folder => (folder.MailboxAccountId, folder.Alias),
            static folder => folder);

        // Ordered here rather than in SQL because the order is ordinal by contract, and a database's collation is not
        // something MailFathom configures.
        return
        [
            .. bindings
                .Select(binding => Describe(binding, countsByFolder))
                .OrderBy(static folder => folder.Folder.AccountId.Value, StringComparer.Ordinal)
                .ThenBy(static folder => folder.Folder.Alias.Value, StringComparer.Ordinal),
        ];
    }

    /// <summary>Composes the query that reads the remote folder each alias in scope currently names, one row per alias.</summary>
    /// <param name="dbContext">The scoped context, which the read joins no transaction of.</param>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <returns>The composed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The generation is compared against the alias's own highest rather than grouped in process, because the number of
    /// bindings behind an alias grows without a ceiling as a mail server recreates the folder — and a request drawing a
    /// folder tree would otherwise pay for that whole history to reach the same one row per alias. The subquery is
    /// deliberately unscoped: it asks what the alias's newest binding is, and narrowing it would let a withheld
    /// generation decide that a scoped one is current. The owner is not that kind of narrowing and is carried anyway:
    /// it names the same row's own owner rather than the caller's, so a binding it excludes is a different folder
    /// belonging to somebody else rather than one of this alias's generations the caller may not see.
    /// </remarks>
    internal static IQueryable<StoredMailFolderBinding> NewestBindingsIn(
        MailFathomDbContext dbContext,
        MailboxScope scope) =>
        MailFoldersInScope.Within(dbContext.MailFolders.AsNoTracking(), scope)
            .Where(folder => folder.ResolutionGeneration == dbContext.MailFolders
                .Where(binding => binding.OwnerId == folder.OwnerId
                    && binding.MailboxAccountId == folder.MailboxAccountId
                    && binding.Alias == folder.Alias)
                .Max(binding => binding.ResolutionGeneration))
            .Select(folder => new StoredMailFolderBinding(
                folder.MailboxAccountId,
                folder.Alias,
                folder.RemotePath,
                folder.HierarchyDelimiter));

    /// <summary>Composes the query that counts the readable mail of each alias in scope, and how much of it is unread.</summary>
    /// <param name="dbContext">The scoped context, which the read joins no transaction of.</param>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <returns>The composed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The unread figure is a second aggregate over the same group rather than a second query, so both counts describe
    /// one set of rows read once. Grouping by the alias rather than by the binding is what makes an alias that was
    /// rebound count what every generation of it stored.
    /// </remarks>
    internal static IQueryable<StoredMailFolderCount> FolderCountsIn(
        MailFathomDbContext dbContext,
        MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope)
            .GroupBy(email => new { email.MailboxAccountId, email.MailFolder.Alias })
            .Select(folder => new StoredMailFolderCount(
                folder.Key.MailboxAccountId,
                folder.Key.Alias,
                folder.Count(),
                folder.Count(email => !email.IsRemotelySeen)));

    /// <summary>Rebuilds one folder from its binding and whatever the counts said about it.</summary>
    /// <remarks>An alias whose mail is all tombstoned, or which holds none, appears in no count row and reads as nought of both.</remarks>
    private static StoredMailFolder Describe(
        StoredMailFolderBinding binding,
        IReadOnlyDictionary<(string AccountId, string Alias), StoredMailFolderCount> countsByFolder)
    {
        var counted = countsByFolder.GetValueOrDefault((binding.MailboxAccountId, binding.Alias));

        return new StoredMailFolder(
            new MailFolderIdentity(
                MailAccountId.Create(binding.MailboxAccountId),
                MailFolderAlias.Create(binding.Alias)),
            MailFolderEntityResolver.ToRemotePath(binding.RemotePath, binding.HierarchyDelimiter),
            counted?.StoredEmailCount ?? 0,
            counted?.UnreadEmailCount ?? 0);
    }
}

/// <summary>The columns one alias's current binding is rebuilt from.</summary>
/// <param name="MailboxAccountId">The account the folder belongs to.</param>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="RemotePath">The path the mail server advertises the folder at.</param>
/// <param name="HierarchyDelimiter">The delimiter the server reported, as the text the column holds, and empty where it reported none.</param>
internal sealed record StoredMailFolderBinding(
    string MailboxAccountId,
    string Alias,
    string RemotePath,
    string? HierarchyDelimiter);

/// <summary>What one alias's mail amounts to, as PostgreSQL counted it.</summary>
/// <param name="MailboxAccountId">The account the folder belongs to.</param>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="StoredEmailCount">How many of its emails this deployment holds and would serve.</param>
/// <param name="UnreadEmailCount">How many of those the mail server last reported without <c>\Seen</c>.</param>
internal sealed record StoredMailFolderCount(
    string MailboxAccountId,
    string Alias,
    int StoredEmailCount,
    int UnreadEmailCount);
