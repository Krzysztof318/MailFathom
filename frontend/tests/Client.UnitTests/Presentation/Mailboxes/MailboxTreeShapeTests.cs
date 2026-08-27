// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Mailboxes;

/// <summary>The shape of the tree: what is drawn, in what order, and what each row narrows the workspace to.</summary>
public sealed class MailboxTreeShapeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RefreshedRecently = new(2026, 8, 25, 11, 50, 0, TimeSpan.Zero);

    /// <summary>An owner who owns no mailbox draws nothing, which is the state a view renders rather than an empty tree.</summary>
    [Fact]
    public void Of_AnOwnerWhoOwnsNoMailbox_DrawsNothing()
    {
        // Act
        var rows = Draw(Answered());

        // Assert
        Assert.Empty(rows);
    }

    /// <summary>Every mailbox at once is offered first, because it is what a run opens scoped to.</summary>
    [Fact]
    public void Of_OneMailbox_OpensOnEveryMailboxAndNarrowsToNothing()
    {
        // Act
        var rows = Draw(Answered(Mailbox("work", Folder("INBOX", role: "Inbox", unread: 3, stored: 40))));

        // Assert
        Assert.Equal(MailboxRowKind.Everything, rows[0].Kind);
        Assert.Equal("every mailbox", rows[0].Name);
        Assert.Equal(3, rows[0].UnreadCount);
        Assert.Equal(40, rows[0].StoredCount);
        Assert.Equal(WorkspaceScope.Everything, rows[0].Scope);
        Assert.True(rows[0].IsSelected);
    }

    /// <summary>
    /// One mailbox is offered as itself and never also as a role across mailboxes, because with a single mailbox the
    /// unified row would name exactly what the folder beneath the account already names.
    /// </summary>
    [Fact]
    public void Of_OneMailbox_OffersNoRoleAcrossMailboxes()
    {
        // Act
        var rows = Draw(Answered(Mailbox("work", Folder("INBOX", role: "Inbox"))));

        // Assert
        Assert.DoesNotContain(rows, row => row.Kind is MailboxRowKind.UnifiedRole);
        Assert.Equal(2, rows.Count);
        Assert.Equal(MailboxRowKind.Account, rows[1].Kind);
    }

    /// <summary>
    /// Several mailboxes make a role worth taking across all of them, which is the whole of what makes them one
    /// workspace rather than three applications side by side.
    /// </summary>
    [Fact]
    public void Of_SeveralMailboxes_OffersEachRoleMoreThanOneOfThemPlays()
    {
        // Arrange
        var answered = Answered(
            Mailbox("work", Folder("INBOX", role: "Inbox", unread: 2), Folder("SENT", role: "Sent")),
            Mailbox("home", Folder("INBOX", role: "Inbox", unread: 5), Folder("TRASH", role: "Trash")));

        // Act
        var rows = Draw(answered);
        var unified = rows.Where(row => row.Kind is MailboxRowKind.UnifiedRole).ToArray();

        // Assert
        Assert.Equal(["Inbox · every mailbox"], unified.Select(row => row.Name));
        Assert.Equal(7, unified[0].UnreadCount);
        Assert.Equal(new WorkspaceScope { Role = "Inbox" }, unified[0].Scope);
    }

    /// <summary>The unified rows are read in the order a person reads a mailbox rather than in the order the deployment published them.</summary>
    [Fact]
    public void Of_SeveralRolesAcrossMailboxes_ReadsThemInReadingOrder()
    {
        // Arrange
        var roles = new[] { "Trash", "Sent", "Inbox" };
        var answered = Answered(
            Mailbox("work", [.. roles.Select(role => Folder(role.ToUpperInvariant(), role: role))]),
            Mailbox("home", [.. roles.Select(role => Folder(role.ToUpperInvariant(), role: role))]));

        // Act
        var unified = Draw(answered).Where(row => row.Kind is MailboxRowKind.UnifiedRole).ToArray();

        // Assert
        Assert.Equal(
            ["Inbox · every mailbox", "Sent · every mailbox", "Trash · every mailbox"],
            unified.Select(row => row.Name));
    }

    /// <summary>
    /// Which folder is the sent one is the role the deployment gave it and never its name, which is what makes the
    /// tree right on a provider that names its folders in another language.
    /// </summary>
    [Fact]
    public void Of_AMailboxWhoseFoldersAreNamedInAnotherLanguage_PlacesThemByRoleRatherThanByName()
    {
        // Arrange
        var answered = Answered(Mailbox(
            "work",
            Folder("ARCHIVE", role: "Archive", path: ["Archiwum"]),
            Folder("SENT", role: "Sent", path: ["Wysłane"]),
            Folder("INBOX", role: "Inbox", path: ["Odebrane"])));

        // Act
        var folders = Draw(answered, Opened("work")).Where(row => row.Kind is MailboxRowKind.Folder).ToArray();

        // Assert
        Assert.Equal(["Odebrane", "Wysłane", "Archiwum"], folders.Select(row => row.Name));
        Assert.All(folders, row => Assert.Equal(1, row.Depth));
    }

    /// <summary>A mail server's hierarchy is drawn as one, level by level, and a level nothing is bound to is still a level.</summary>
    [Fact]
    public void Of_AnExpandedHierarchy_DrawsALevelNothingIsBoundToAsALevelThatNarrowsNothing()
    {
        // Arrange
        var answered = Answered(Mailbox(
            "work",
            Folder("PROJECTS-2024", path: ["Projects", "2024"], unread: 1, stored: 9)));

        // Act
        var rows = Draw(answered, Opened("work", ["Projects"]));
        var group = rows.Single(row => row.Kind is MailboxRowKind.Group);
        var folder = rows.Single(row => row.Kind is MailboxRowKind.Folder);

        // Assert
        Assert.Equal("Projects", group.Name);
        Assert.Equal(1, group.Depth);
        Assert.Null(group.Scope);
        Assert.False(group.IsSelectable);

        Assert.Equal("2024", folder.Name);
        Assert.Equal(2, folder.Depth);
        Assert.Equal(new WorkspaceScope { Account = "work", Folder = "PROJECTS-2024" }, folder.Scope);
    }

    /// <summary>
    /// A folder the deployment gave a role takes its own subfolders with it, so a provider that nests the inbox does
    /// not have that inbox drawn twice.
    /// </summary>
    [Fact]
    public void Of_ASpecialUseFolderWithSubfolders_TakesThemWithItRatherThanBeingDrawnTwice()
    {
        // Arrange
        var answered = Answered(Mailbox(
            "work",
            Folder("INBOX", role: "Inbox", path: ["INBOX"]),
            Folder("INBOX-INVOICES", path: ["INBOX", "Invoices"])));

        // Act
        var rows = Draw(answered, Opened("work", ["INBOX"]));

        // Assert
        Assert.Equal(
            [("every mailbox", 0), ("work mail", 0), ("INBOX", 1), ("Invoices", 2)],
            rows.Select(row => (row.Name, row.Depth)));
        Assert.DoesNotContain(rows, row => row.Kind is MailboxRowKind.Group);
    }

    /// <summary>A row counts what is nested under it as well as what is on it, so collapsing a subtree never hides unread mail.</summary>
    [Fact]
    public void Of_ACollapsedMailbox_CountsWhatIsUnderItRatherThanNothing()
    {
        // Arrange
        var answered = Answered(Mailbox(
            "work",
            Folder("INBOX", role: "Inbox", unread: 4, stored: 100),
            Folder("PROJECTS-2024", path: ["Projects", "2024"], unread: 2, stored: 9)));

        // Act
        var collapsed = Draw(answered).Single(row => row.Kind is MailboxRowKind.Account);

        // Assert
        Assert.Equal(6, collapsed.UnreadCount);
        Assert.Equal(109, collapsed.StoredCount);
        Assert.True(collapsed.IsExpandable);
        Assert.True(collapsed.CanOpen);
        Assert.False(collapsed.CanClose);
    }

    /// <summary>
    /// A folder the deployment has discovered but never synchronized is drawn under MailFathom's own name for it,
    /// because an empty mailbox and an unsynchronized one are not the same thing on screen.
    /// </summary>
    [Fact]
    public void Of_AFolderNothingHasBoundToARemoteFolderYet_IsDrawnUnderItsAlias()
    {
        // Arrange
        var answered = Answered(Mailbox(
            "work",
            Folder("NEWSLETTERS", path: [], standing: "NeverSynchronized")));

        // Act
        var folder = Draw(answered, Opened("work")).Single(row => row.Kind is MailboxRowKind.Folder);

        // Assert
        Assert.Equal("NEWSLETTERS", folder.Name);
        Assert.Equal("not synchronized yet", folder.Standing);
        Assert.Equal("no mail taken in yet", folder.Freshness);
    }

    /// <summary>
    /// A mail server that did not answer and a folder merely catching up are two situations rather than one, and
    /// neither of them is drawn as something still loading.
    /// </summary>
    [Fact]
    public void Of_AnUnreachableMailboxAndAFolderMerelyBehind_AreDrawnDistinctly()
    {
        // Arrange
        var answered = new DeploymentMailFolders(
            SynchronizationEnabled: true,
            [
                new DeploymentAccountFolders(
                    new DeploymentMailAccount("work", "work mail", "Unreachable", RefreshedRecently, Behind: false),
                    [Folder("INBOX", role: "Inbox", behind: true)]),
            ]);

        // Act
        var rows = Draw(answered, Opened("work"));
        var mailbox = rows.Single(row => row.Kind is MailboxRowKind.Account);
        var folder = rows.Single(row => row.Kind is MailboxRowKind.Folder);

        // Assert
        Assert.True(mailbox.IsUnreachable);
        Assert.False(mailbox.IsFailing);
        Assert.False(mailbox.IsBehind);
        Assert.Equal("mail server not answering", mailbox.Standing);

        Assert.False(folder.IsUnreachable);
        Assert.False(folder.IsFailing);
        Assert.True(folder.IsBehind);
        Assert.True(folder.ShowsCopyState);
    }

    /// <summary>A folder being refreshed like every other one says nothing about its copy, which is what keeps the pane readable.</summary>
    [Fact]
    public void Of_AFolderWithNothingToSayAboutItsCopy_SaysNothing()
    {
        // Act
        var folder = Draw(Answered(Mailbox("work", Folder("INBOX", role: "Inbox"))), Opened("work"))
            .Single(row => row.Kind is MailboxRowKind.Folder);

        // Assert
        Assert.False(folder.ShowsCopyState);
    }

    /// <summary>The row that is marked as current is the one naming the place in force, whatever narrowed the workspace to it.</summary>
    [Fact]
    public void Of_TheScopeInForce_MarksTheRowThatNamesThePlaceRatherThanTheOneThatWasPressed()
    {
        // Arrange
        var answered = Answered(Mailbox("work", Folder("INBOX", role: "Inbox")));
        var scope = new WorkspaceScope
        {
            Account = "work",
            Folder = "INBOX",
            Selection = ImmutableArray.Create("117"),
        };

        // Act
        var rows = Draw(answered, Opened("work"), scope);

        // Assert
        Assert.Equal(
            ["INBOX"],
            rows.Where(row => row.IsSelected).Select(row => row.Name));
    }

    /// <summary>A mailbox with no folder at all cannot be opened, so nothing offers to open it onto nothing.</summary>
    [Fact]
    public void Of_AMailboxWithNoFolder_CannotBeOpened()
    {
        // Act
        var mailbox = Draw(Answered(Mailbox("work")), Opened("work"))
            .Single(row => row.Kind is MailboxRowKind.Account);

        // Assert
        Assert.False(mailbox.IsExpandable);
        Assert.False(mailbox.CanOpen);
        Assert.False(mailbox.CanClose);
    }

    /// <summary>A tree drawn without one of its inputs would be a tree describing nowhere.</summary>
    [Fact]
    public void Of_AMissingInput_IsRefused()
    {
        // Arrange
        var answered = Answered();
        var expanded = ImmutableHashSet<string>.Empty;
        var words = Words();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            MailboxTreeShape.Of(null!, expanded, WorkspaceScope.Everything, Now, words));
        Assert.Throws<ArgumentNullException>(() =>
            MailboxTreeShape.Of(answered, null!, WorkspaceScope.Everything, Now, words));
        Assert.Throws<ArgumentNullException>(() =>
            MailboxTreeShape.Of(answered, expanded, null!, Now, words));
        Assert.Throws<ArgumentNullException>(() =>
            MailboxTreeShape.Of(answered, expanded, WorkspaceScope.Everything, Now, null!));
    }

    private static IImmutableList<MailboxRow> Draw(
        DeploymentMailFolders answered,
        IImmutableSet<string>? expanded = null,
        WorkspaceScope? scope = null) =>
        MailboxTreeShape.Of(
            answered,
            expanded ?? ImmutableHashSet<string>.Empty,
            scope ?? WorkspaceScope.Everything,
            Now,
            Words());

    private static ImmutableHashSet<string> Opened(string accountId, IReadOnlyList<string>? path = null) =>
        path is null
            ? ImmutableHashSet.Create(MailboxTreeShape.AccountKey(accountId))
            : ImmutableHashSet.Create(
                MailboxTreeShape.AccountKey(accountId),
                MailboxTreeShape.LevelKey(accountId, path));

    private static DeploymentMailFolders Answered(params DeploymentAccountFolders[] accounts) =>
        new(SynchronizationEnabled: true, accounts);

    private static DeploymentAccountFolders Mailbox(string id, params DeploymentMailFolder[] folders) =>
        new(
            new DeploymentMailAccount(id, $"{id} mail", "Synchronized", RefreshedRecently, Behind: false),
            folders);

    private static DeploymentMailFolder Folder(
        string alias,
        string? role = null,
        IReadOnlyList<string>? path = null,
        int unread = 0,
        int stored = 0,
        string standing = "Synchronized",
        bool behind = false) =>
        new(
            alias,
            role,
            path ?? [alias],
            stored,
            unread,
            standing,
            standing is "NeverSynchronized" ? null : RefreshedRecently,
            behind);

    private static StubStringLocalizer Words() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Mailboxes.Everything"] = "every mailbox",
        ["Mailboxes.Unified"] = "{0} · every mailbox",
        ["Mailboxes.Role.Inbox"] = "Inbox",
        ["Mailboxes.Role.Sent"] = "Sent",
        ["Mailboxes.Role.Trash"] = "Trash",
        ["Mailboxes.Role.Archive"] = "Archive",
        ["Mailboxes.Standing.Unrecognized"] = "state not recognized",
        ["Mailboxes.Standing.NeverSynchronized"] = "not synchronized yet",
        ["Mailboxes.Standing.Synchronized"] = "being refreshed",
        ["Mailboxes.Standing.Failing"] = "last refresh did not finish",
        ["Mailboxes.Standing.Unreachable"] = "mail server not answering",
        ["Mailboxes.Freshness.Never"] = "no mail taken in yet",
        ["Mailboxes.Freshness.WithinTheHour"] = "updated within the last hour",
        ["Mailboxes.Freshness.Today"] = "updated within the last day",
        ["Mailboxes.Freshness.WithinTheWeek"] = "updated within the last week",
        ["Mailboxes.Freshness.LongerAgo"] = "nothing taken in for over a week",
    });
}
