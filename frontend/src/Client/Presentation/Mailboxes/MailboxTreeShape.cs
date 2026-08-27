// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Presentation.Workspace;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>Turns what a deployment answered into the lines the mailbox tree draws.</summary>
/// <remarks>
/// <para>
/// A pure reduction of four inputs — the answer, what is expanded, what is in scope, and when it is being read — so the
/// shape of the tree is decided in one place a test reads directly rather than distributed across a control's own
/// container state. Nothing here asks the deployment anything.
/// </para>
/// <para>
/// Four decisions are worth stating because they are the ones a reader would otherwise take for guesses. A folder is
/// found to be the inbox, the sent folder, or the trash by the role the deployment gave it and never by matching its
/// name, because the names differ per provider and per language. Such a folder is drawn at the top of its mailbox
/// whatever level the mail server nests it at, and it takes its own subfolders with it, so nothing appears twice. A
/// role is offered across mailboxes only where more than one mailbox plays it, since with a single mailbox it would
/// name what the folder beneath that mailbox already names. And every row counts what is nested under it as well as
/// what is on it, so collapsing a subtree never hides unread mail.
/// </para>
/// </remarks>
internal static class MailboxTreeShape
{
    /// <summary>What a row standing for every mailbox at once is keyed by.</summary>
    internal const string EverythingKey = "*";

    /// <summary>What the parts of a key are joined with.</summary>
    /// <remarks>
    /// A unit separator rather than a character a mail server might put in a folder name. A name carrying one anyway
    /// costs that row the expansion it was remembered with and nothing else, because a key is only ever compared
    /// against a key composed the same way.
    /// </remarks>
    internal const char KeySeparator = '\u001F';

    /// <summary>Names the row one mailbox is remembered by.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <returns>The key.</returns>
    internal static string AccountKey(string accountId) => $"a{KeySeparator}{accountId}";

    /// <summary>Names the row one level of one mailbox's hierarchy is remembered by.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="path">The level's place on the mail server, outermost level first.</param>
    /// <returns>The key.</returns>
    internal static string LevelKey(string accountId, IReadOnlyList<string> path) =>
        $"{AccountKey(accountId)}{KeySeparator}{string.Join(KeySeparator, path)}";

    /// <summary>Draws the tree.</summary>
    /// <param name="answered">What the deployment reported about the owner's mailboxes and their folders.</param>
    /// <param name="expanded">The keys of the rows whose contents are being shown.</param>
    /// <param name="scope">What the workspace is narrowed to, which is what marks one row selected.</param>
    /// <param name="now">When the freshness bands are measured from.</param>
    /// <param name="words">Where the composed sentences come from.</param>
    /// <returns>The visible rows, outermost first, in the order they are drawn, and empty for an owner who owns no mailbox.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static IImmutableList<MailboxRow> Of(
        DeploymentMailFolders answered,
        IImmutableSet<string> expanded,
        WorkspaceScope scope,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(expanded);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(words);

        var accounts = answered.Owned;

        if (accounts.Count is 0)
        {
            return ImmutableArray<MailboxRow>.Empty;
        }

        var rows = ImmutableArray.CreateBuilder<MailboxRow>();

        rows.Add(EverythingRow(accounts, scope, words));
        rows.AddRange(UnifiedRoleRows(accounts, scope, words));

        foreach (var account in accounts)
        {
            AddAccount(rows, account, expanded, scope, now, words);
        }

        return rows.ToImmutable();
    }

    private static MailboxRow EverythingRow(
        IReadOnlyList<DeploymentAccountFolders> accounts,
        WorkspaceScope scope,
        IStringLocalizer words)
    {
        var counted = Total(accounts.SelectMany(account => account.Held));

        return Unscoped(
            EverythingKey,
            MailboxRowKind.Everything,
            words[MailboxWords.EverythingKey].Value,
            counted,
            WorkspaceScope.Everything,
            scope);
    }

    private static IEnumerable<MailboxRow> UnifiedRoleRows(
        IReadOnlyList<DeploymentAccountFolders> accounts,
        WorkspaceScope scope,
        IStringLocalizer words)
    {
        // With one mailbox a unified row would name exactly what the folder beneath that mailbox names, so the whole
        // block is absent rather than present and redundant.
        if (accounts.Count < 2)
        {
            return [];
        }

        return MailboxWords.RolesInReadingOrder
            .Select(role => (Role: role, Played: FoldersPlaying(accounts, role)))
            .Where(offered => offered.Played.Length > 1)
            .Select(offered => Unscoped(
                $"r{KeySeparator}{offered.Role}",
                MailboxRowKind.UnifiedRole,
                words[MailboxWords.UnifiedRoleKey, words[MailboxWords.RoleResourceKeyFor(offered.Role)].Value].Value,
                Total(offered.Played),
                new WorkspaceScope { Role = offered.Role.ToString() },
                scope));
    }

    private static DeploymentMailFolder[] FoldersPlaying(
        IReadOnlyList<DeploymentAccountFolders> accounts,
        MailFolderRole role) =>
        [
            .. accounts
                .Select(account => account.Held.FirstOrDefault(folder => folder.SpecialUse == role))
                .OfType<DeploymentMailFolder>()
        ];

    private static void AddAccount(
        ImmutableArray<MailboxRow>.Builder rows,
        DeploymentAccountFolders account,
        IImmutableSet<string> expanded,
        WorkspaceScope scope,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        var accountId = account.Account.Id;
        var held = account.Held;
        var key = AccountKey(accountId);
        var isExpanded = expanded.Contains(key) && held.Count > 0;
        var narrowed = new WorkspaceScope { Account = accountId };
        var standing = account.Account.Standing;
        var counted = Total(held);

        rows.Add(new MailboxRow(
            key,
            MailboxRowKind.Account,
            Depth: 0,
            account.Account.DisplayName,
            counted.Unread,
            counted.Stored,
            words[MailboxWords.StandingResourceKeyFor(standing)].Value,
            words[MailboxWords.FreshnessResourceKeyFor(MailboxWords.GapAt(account.Account.LastSynchronizedAt, now))].Value,
            standing is MailSynchronizationStanding.Unreachable,
            standing is MailSynchronizationStanding.Failing,
            account.Account.Behind,
            held.Count > 0,
            isExpanded,
            scope.NamesSamePlaceAs(narrowed),
            narrowed));

        if (!isExpanded)
        {
            return;
        }

        var placed = held.Select(folder => new PlacedFolder(LevelsOf(folder), folder)).ToArray();
        var roots = SpecialUseRoots(placed);

        foreach (var root in roots)
        {
            AddSubtree(rows, accountId, root, placed, depth: 1, expanded, scope, now, words);
        }

        var ordinary = placed
            .Where(folder => !roots.Any(root => IsAtOrUnder(folder.Levels, root.Levels)))
            .ToArray();

        AddLevel(rows, accountId, ordinary, [], depth: 1, expanded, scope, now, words);
    }

    /// <summary>
    /// Picks the folders drawn at the top of their mailbox: the ones the deployment gave a role, in reading order,
    /// dropping any that already sits beneath another of them.
    /// </summary>
    /// <remarks>
    /// A provider that both advertises a role and nests the folder inside another role's folder would otherwise have
    /// that folder drawn twice, once at the top and once under its parent. The outermost of the two is the one a person
    /// is looking for.
    /// </remarks>
    private static PlacedFolder[] SpecialUseRoots(IReadOnlyList<PlacedFolder> placed)
    {
        var roled = placed
            .Where(folder => MailboxWords.RolesInReadingOrder.Contains(folder.Folder.SpecialUse))
            .ToArray();

        return
        [
            .. roled
                .Where(folder => !roled.Any(other =>
                    !ReferenceEquals(other, folder) && IsUnder(folder.Levels, other.Levels)))
                .OrderBy(folder => MailboxWords.RolesInReadingOrder.IndexOf(folder.Folder.SpecialUse))
                .ThenBy(folder => folder.Folder.Alias, StringComparer.Ordinal)
        ];
    }

    private static void AddSubtree(
        ImmutableArray<MailboxRow>.Builder rows,
        string accountId,
        PlacedFolder root,
        IReadOnlyList<PlacedFolder> placed,
        int depth,
        IImmutableSet<string> expanded,
        WorkspaceScope scope,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        var key = LevelKey(accountId, root.Levels);
        var beneath = placed.Where(folder => IsUnder(folder.Levels, root.Levels)).ToArray();
        var isExpanded = beneath.Length > 0 && expanded.Contains(key);

        rows.Add(FolderRow(
            accountId,
            root.Folder,
            key,
            root.Levels[^1],
            depth,
            Total([root.Folder, .. beneath.Select(folder => folder.Folder)]),
            beneath.Length > 0,
            isExpanded,
            scope,
            now,
            words));

        if (isExpanded)
        {
            AddLevel(rows, accountId, beneath, root.Levels, depth + 1, expanded, scope, now, words);
        }
    }

    private static void AddLevel(
        ImmutableArray<MailboxRow>.Builder rows,
        string accountId,
        IReadOnlyList<PlacedFolder> placed,
        IReadOnlyList<string> prefix,
        int depth,
        IImmutableSet<string> expanded,
        WorkspaceScope scope,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        // Grouped ordinally, so two levels are the same level exactly when the mail server spelled them the same way,
        // and ordered for a reader, which is the culture the application is being read in rather than a byte order.
        var levels = placed
            .GroupBy(folder => folder.Levels[prefix.Count], StringComparer.Ordinal)
            .OrderBy(level => level.Key, StringComparer.CurrentCulture);

        foreach (var level in levels)
        {
            IReadOnlyList<string> path = [.. prefix, level.Key];

            var key = LevelKey(accountId, path);
            var bound = level.FirstOrDefault(folder => folder.Levels.Count == path.Count);
            var deeper = level.Where(folder => folder.Levels.Count > path.Count).ToArray();
            var isExpanded = deeper.Length > 0 && expanded.Contains(key);
            var counted = Total(level.Select(folder => folder.Folder));

            rows.Add(bound is null
                ? GroupRow(key, level.Key, depth, counted, deeper.Length > 0, isExpanded)
                : FolderRow(
                    accountId,
                    bound.Folder,
                    key,
                    level.Key,
                    depth,
                    counted,
                    deeper.Length > 0,
                    isExpanded,
                    scope,
                    now,
                    words));

            if (isExpanded)
            {
                AddLevel(rows, accountId, deeper, path, depth + 1, expanded, scope, now, words);
            }
        }
    }

    private static MailboxRow FolderRow(
        string accountId,
        DeploymentMailFolder folder,
        string key,
        string name,
        int depth,
        (int Unread, int Stored) counted,
        bool isExpandable,
        bool isExpanded,
        WorkspaceScope scope,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        var narrowed = new WorkspaceScope { Account = accountId, Folder = folder.Alias };
        var standing = folder.Standing;

        return new MailboxRow(
            key,
            MailboxRowKind.Folder,
            depth,
            name,
            counted.Unread,
            counted.Stored,
            words[MailboxWords.StandingResourceKeyFor(standing)].Value,
            words[MailboxWords.FreshnessResourceKeyFor(MailboxWords.GapAt(folder.LastSynchronizedAt, now))].Value,
            standing is MailSynchronizationStanding.Unreachable,
            standing is MailSynchronizationStanding.Failing,
            folder.Behind,
            isExpandable,
            isExpanded,
            scope.NamesSamePlaceAs(narrowed),
            narrowed);
    }

    private static MailboxRow GroupRow(
        string key,
        string name,
        int depth,
        (int Unread, int Stored) counted,
        bool isExpandable,
        bool isExpanded) =>
        new(
            key,
            MailboxRowKind.Group,
            depth,
            name,
            counted.Unread,
            counted.Stored,
            Standing: string.Empty,
            Freshness: string.Empty,
            IsUnreachable: false,
            IsFailing: false,
            IsBehind: false,
            isExpandable,
            isExpanded,
            IsSelected: false,
            Scope: null);

    /// <summary>Builds one of the rows that stands for several copies rather than for one, which carries no standing of its own.</summary>
    private static MailboxRow Unscoped(
        string key,
        MailboxRowKind kind,
        string name,
        (int Unread, int Stored) counted,
        WorkspaceScope narrowed,
        WorkspaceScope scope) =>
        new(
            key,
            kind,
            Depth: 0,
            name,
            counted.Unread,
            counted.Stored,
            Standing: string.Empty,
            Freshness: string.Empty,
            IsUnreachable: false,
            IsFailing: false,
            IsBehind: false,
            IsExpandable: false,
            IsExpanded: false,
            scope.NamesSamePlaceAs(narrowed),
            narrowed);

    /// <summary>Reads where a folder sits, falling back to its alias where nothing has bound it to a remote folder.</summary>
    /// <remarks>
    /// A folder the deployment has discovered but never synchronized carries no path, and leaving it out of the tree
    /// would make an empty mailbox and an unsynchronized one look the same. MailFathom's own alias is the only name
    /// there is for it until a run binds one.
    /// </remarks>
    private static IReadOnlyList<string> LevelsOf(DeploymentMailFolder folder) =>
        folder.HierarchyLevels.Count > 0 ? folder.HierarchyLevels : [folder.Alias];

    private static bool IsUnder(IReadOnlyList<string> levels, IReadOnlyList<string> ancestor) =>
        levels.Count > ancestor.Count && IsAtOrUnder(levels, ancestor);

    private static bool IsAtOrUnder(IReadOnlyList<string> levels, IReadOnlyList<string> ancestor) =>
        levels.Count >= ancestor.Count
        && ancestor.Select((level, index) => string.Equals(levels[index], level, StringComparison.Ordinal)).All(same => same);

    private static (int Unread, int Stored) Total(IEnumerable<DeploymentMailFolder> folders)
    {
        var counted = folders.ToArray();

        return (counted.Sum(folder => folder.UnreadEmailCount), counted.Sum(folder => folder.StoredEmailCount));
    }

    /// <summary>One folder beside the levels it is drawn under, so the levels are split once rather than at each pass.</summary>
    private sealed record PlacedFolder(IReadOnlyList<string> Levels, DeploymentMailFolder Folder);
}
