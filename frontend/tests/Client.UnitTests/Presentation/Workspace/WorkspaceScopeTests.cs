// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.UnitTests.Presentation.Workspace;

/// <summary>
/// Holds the scope to being a value rather than an object, which is what the state carrying it between spaces reads
/// it as when deciding whether anything changed.
/// </summary>
public sealed class WorkspaceScopeTests
{
    /// <summary>A run starts against everything, because nothing has narrowed it yet.</summary>
    [Fact]
    public void Everything_TheScopeARunStartsIn_NamesNoAccountFolderOrSelection()
    {
        // Act
        var scope = WorkspaceScope.Everything;

        // Assert
        Assert.Null(scope.Account);
        Assert.Null(scope.Folder);
        Assert.Null(scope.Role);
        Assert.Empty(scope.Selection);
        Assert.Equal(string.Empty, scope.BodySelection);
        Assert.False(scope.NarrowsAnything);
    }

    /// <summary>
    /// An account in scope is a narrowing, whether or not anything within it is selected — and so is a folder named
    /// without one, which nothing here refuses and which therefore must not read as everything. A role is one too: it
    /// names no account and still leaves most of the mail out.
    /// </summary>
    [Fact]
    public void NarrowsAnything_AnAccountAFolderARoleOrASelection_IsANarrowing()
    {
        // Act
        var account = WorkspaceScope.Everything with { Account = "work" };
        var folder = WorkspaceScope.Everything with { Folder = "Inbox" };
        var role = WorkspaceScope.Everything with { Role = "Sent" };
        var selection = WorkspaceScope.Everything with { Selection = ImmutableArray.Create("1") };
        var bodySelection = WorkspaceScope.Everything with { BodySelection = "the selected passage" };

        // Assert
        Assert.True(account.NarrowsAnything);
        Assert.True(folder.NarrowsAnything);
        Assert.True(role.NarrowsAnything);
        Assert.True(selection.NarrowsAnything);
        Assert.True(bodySelection.NarrowsAnything);
    }

    /// <summary>
    /// Two scopes naming the same thing are the same scope. A record compares an immutable list by reference, so
    /// without this the state would report a change every time a space rebuilt an identical selection.
    /// </summary>
    [Fact]
    public void Equals_TwoScopesNamingTheSameThing_AreEqualThoughTheirSelectionsAreDistinctObjects()
    {
        // Arrange
        var one = new WorkspaceScope { Account = "work", Folder = "Inbox", Selection = ImmutableArray.Create("a", "b") };
        var other = new WorkspaceScope { Account = "work", Folder = "Inbox", Selection = ImmutableArray.Create("a", "b") };

        // Assert
        Assert.NotSame(one.Selection, other.Selection);
        Assert.Equal(one, other);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
    }

    /// <summary>Order is part of a selection, so a differently ordered one is a different scope.</summary>
    [Fact]
    public void Equals_SelectionsDifferingInContentOrOrder_AreNotEqual()
    {
        // Arrange
        var one = WorkspaceScope.Everything with { Selection = ImmutableArray.Create("a", "b") };
        var reordered = WorkspaceScope.Everything with { Selection = ImmutableArray.Create("b", "a") };
        var shorter = WorkspaceScope.Everything with { Selection = ImmutableArray.Create("a") };

        // Assert
        Assert.NotEqual(one, reordered);
        Assert.NotEqual(one, shorter);
    }

    [Fact]
    public void Equals_TwoDifferentBodySelections_AreDifferentScopes()
    {
        // Arrange
        var one = WorkspaceScope.Everything with { BodySelection = "first passage" };
        var other = WorkspaceScope.Everything with { BodySelection = "second passage" };

        // Assert
        Assert.NotEqual(one, other);
    }

    /// <summary>A folder is read within its account, so the same folder name under another account is another scope.</summary>
    [Fact]
    public void Equals_TheSameFolderUnderAnotherAccount_IsAnotherScope()
    {
        // Arrange
        var work = new WorkspaceScope { Account = "work", Folder = "Inbox" };
        var personal = new WorkspaceScope { Account = "personal", Folder = "Inbox" };

        // Assert
        Assert.NotEqual(work, personal);
    }

    /// <summary>
    /// A role taken across mailboxes is a place of its own rather than a folder under no account, so the two are not
    /// one scope — reading them as one would mark every mailbox's own sent folder as the selected row.
    /// </summary>
    [Fact]
    public void Equals_ARoleAcrossMailboxesAndAFolderOfThatName_AreNotOneScope()
    {
        // Arrange
        var role = WorkspaceScope.Everything with { Role = "Sent" };
        var folder = WorkspaceScope.Everything with { Folder = "Sent" };

        // Assert
        Assert.NotEqual(role, folder);
    }

    /// <summary>
    /// The tree marks a row on the place a scope names rather than on the whole of it, because what is selected inside
    /// a folder changes as somebody reads and the folder they are reading in does not.
    /// </summary>
    [Fact]
    public void NamesSamePlaceAs_TwoScopesInOneFolderHoldingDifferentSelections_NameOnePlace()
    {
        // Arrange
        var reading = new WorkspaceScope { Account = "work", Folder = "Inbox", Selection = ImmutableArray.Create("1") };
        var chosen = new WorkspaceScope { Account = "work", Folder = "Inbox" };
        var elsewhere = new WorkspaceScope { Account = "work", Folder = "Archive" };

        // Assert
        Assert.True(reading.NamesSamePlaceAs(chosen));
        Assert.NotEqual(reading, chosen);
        Assert.False(reading.NamesSamePlaceAs(elsewhere));
        Assert.False(reading.NamesSamePlaceAs(null));
    }
}
