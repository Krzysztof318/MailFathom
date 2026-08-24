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
        Assert.Empty(scope.Selection);
        Assert.False(scope.NarrowsAnything);
    }

    /// <summary>An account in scope is a narrowing, whether or not anything within it is selected.</summary>
    [Fact]
    public void NarrowsAnything_AnAccountOrASelection_IsANarrowing()
    {
        // Act
        var account = WorkspaceScope.Everything with { Account = "work" };
        var selection = WorkspaceScope.Everything with { Selection = ImmutableArray.Create("1") };

        // Assert
        Assert.True(account.NarrowsAnything);
        Assert.True(selection.NarrowsAnything);
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
}
