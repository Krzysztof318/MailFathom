// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Workspace;

/// <summary>
/// The seam that makes the three spaces one application: what one of them writes, the next one reads.
/// </summary>
/// <remarks>
/// Moving between spaces is navigation, which needs a running head and stays out of this suite — what is reachable
/// here is the thing that survives it, which is where the preservation actually lives. A space holding its own scope
/// would pass no test below.
/// </remarks>
public sealed class SharedWorkspaceTests
{
    /// <summary>
    /// The property is expression-bodied, so it is worth stating that it hands back one state rather than building a
    /// new one per read: a second state would leave two spaces reading different values of one thing.
    /// </summary>
    [Fact]
    public void Intent_ReadTwice_IsOneState()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());

        // Act
        var (first, second) = (workspace.Intent, workspace.Intent);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>The scope is one state per workspace for the reason the question above is.</summary>
    [Fact]
    public void Scope_ReadTwice_IsOneState()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());

        // Act
        var (first, second) = (workspace.Scope, workspace.Scope);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>A run starts against everything, which is what a first screen is composed over.</summary>
    [Fact]
    public async Task Scope_AWorkspaceNothingHasNarrowed_StartsAtEverything()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());

        // Act
        var scope = await workspace.Scope;

        // Assert
        Assert.Equal(WorkspaceScope.Everything, scope);
    }

    /// <summary>A run starts with nothing typed, rather than with whatever a previous one left.</summary>
    [Fact]
    public async Task Intent_AWorkspaceNobodyHasTypedInto_StartsEmpty()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());

        // Act
        var intent = await workspace.Intent;

        // Assert
        Assert.Equal(string.Empty, intent);
    }

    /// <summary>
    /// What a space narrowed to is what the workspace then holds, which is the whole of the claim that moving between
    /// spaces preserves the account, the folder, and the selection.
    /// </summary>
    [Fact]
    public async Task Scope_NarrowedOnce_IsWhatTheWorkspaceHolds()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        var narrowed = new WorkspaceScope
        {
            Account = "work",
            Folder = "Inbox",
            Selection = ImmutableArray.Create("117"),
        };

        // Act
        await workspace.Scope.UpdateAsync(_ => narrowed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(narrowed, await workspace.Scope);
    }

    /// <summary>
    /// A run opens where the last one was left, because the tree writes the scope it narrowed to into the store and
    /// this is the state every space then reads. Without it a restart would land on everything with the tree still
    /// drawing the folder as selected.
    /// </summary>
    [Fact]
    public async Task Scope_AWorkspaceOpenedAfterARunThatNarrowed_StartsWhereThatRunLeftIt()
    {
        // Arrange
        var narrowed = new WorkspaceScope { Account = "work", Folder = "INBOX" };
        var memory = new StubMailboxTreeMemory(new RememberedMailboxes(narrowed, ImmutableHashSet<string>.Empty));
        var workspace = new SharedWorkspace(memory);

        // Act
        var scope = await workspace.Scope;

        // Assert
        Assert.Equal(narrowed, scope);
    }

    /// <summary>A workspace without the store it opens on would be one that cannot say where a run starts.</summary>
    [Fact]
    public void Constructor_AMissingStore_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SharedWorkspace(null!));
    }

    /// <summary>A question travels with somebody between spaces rather than being retyped in each of them.</summary>
    [Fact]
    public async Task Intent_TypedOnce_IsWhatTheWorkspaceHolds()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());

        // Act
        await workspace.Intent.SetAsync("what did the auditor ask for", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("what did the auditor ask for", await workspace.Intent);
    }
}
