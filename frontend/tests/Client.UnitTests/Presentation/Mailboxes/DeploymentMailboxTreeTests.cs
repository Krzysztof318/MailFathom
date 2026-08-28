// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Mailboxes;

/// <summary>The tree over one deployment: what it reads, what selecting a row does, and what outlives the run.</summary>
public sealed class DeploymentMailboxTreeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private const string TwoMailboxes =
        """
        {
          "synchronizationEnabled": true,
          "accounts": [
            {
              "account": { "id": "work", "displayName": "Work mail", "synchronizationState": "Synchronized",
                           "lastSynchronizedAt": "2026-08-25T11:50:00+00:00", "behind": false },
              "folders": [
                { "alias": "INBOX", "role": "Inbox", "path": [ "INBOX" ], "storedEmailCount": 40,
                  "unreadEmailCount": 3, "synchronizationState": "Synchronized",
                  "lastSynchronizedAt": "2026-08-25T11:50:00+00:00", "behind": false },
                { "alias": "PROJECTS-2024", "role": null, "path": [ "Projects", "2024" ], "storedEmailCount": 9,
                  "unreadEmailCount": 1, "synchronizationState": "Synchronized",
                  "lastSynchronizedAt": "2026-08-25T11:41:00+00:00", "behind": true }
              ]
            },
            {
              "account": { "id": "home", "displayName": "Home mail", "synchronizationState": "Unreachable",
                           "lastSynchronizedAt": "2026-08-18T09:00:00+00:00", "behind": true },
              "folders": [
                { "alias": "INBOX", "role": "Inbox", "path": [ "Odebrane" ], "storedEmailCount": 12,
                  "unreadEmailCount": 5, "synchronizationState": "Unreachable",
                  "lastSynchronizedAt": "2026-08-18T09:00:00+00:00", "behind": true }
              ]
            }
          ]
        }
        """;

    /// <summary>The tree opens on every mailbox and each mailbox, with the folders behind a row somebody opens.</summary>
    [Fact]
    public async Task Rows_ADeploymentAnswering_DrawsEveryMailboxAndEachOfThem()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);

        // Act
        var rows = await over.Tree.Rows;

        // Assert
        Assert.NotNull(rows);
        Assert.Equal(
            [MailboxRowKind.Everything, MailboxRowKind.UnifiedRole, MailboxRowKind.Account, MailboxRowKind.Account],
            rows.Select(row => row.Kind));
        Assert.Equal(9, rows[0].UnreadCount);
    }

    /// <summary>One read for the whole tree: the rows and the notice beside them are projections of one answer.</summary>
    [Fact]
    public async Task Rows_ReadBesideTheNoticeAboutThem_AsksTheDeploymentOnce()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);

        // Act
        await over.Tree.Rows;
        await over.Tree.SynchronizationPaused;
        await over.Tree.Rows;

        // Assert
        Assert.Single(over.Harness.Deployment.Requests);
        Assert.Equal("/api/client/folders", over.Harness.Deployment.Requests[0].RequestUri.AbsolutePath);
    }

    /// <summary>Opening a row shows what is nested under it, and the tree is redrawn without the deployment being asked again.</summary>
    [Fact]
    public async Task ToggleAsync_AMailboxSomebodyOpened_ShowsItsFoldersWithoutAskingAgain()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Tree.Rows;

        // Act
        await over.Tree.ToggleAsync(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);
        var rows = await over.Tree.Rows;

        // Assert
        Assert.NotNull(rows);
        Assert.Contains(rows, row => row.Kind is MailboxRowKind.Folder && row.Name is "INBOX");
        Assert.Single(over.Harness.Deployment.Requests);
    }

    /// <summary>Opening a row twice closes it again, because one control does both.</summary>
    [Fact]
    public async Task ToggleAsync_ARowOpenedTwice_IsClosedAgain()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        var key = MailboxTreeShape.AccountKey("work");

        // Act
        await over.Tree.ToggleAsync(key, TestContext.Current.CancellationToken);
        await over.Tree.ToggleAsync(key, TestContext.Current.CancellationToken);
        var rows = await over.Tree.Rows;

        // Assert
        Assert.NotNull(rows);
        Assert.DoesNotContain(rows, row => row.Kind is MailboxRowKind.Folder);
        Assert.Empty(over.Memory.Remembered.Expanded);
    }

    /// <summary>Selecting a row narrows the workspace, which is what makes the tree the client's scope selector.</summary>
    [Fact]
    public async Task SelectAsync_AFolderSomebodyChose_NarrowsTheWorkspaceToIt()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Tree.ToggleAsync(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);
        var rows = await over.Tree.Rows;
        var folder = rows!.First(row => row.Kind is MailboxRowKind.Folder);

        // Act
        await over.Tree.SelectAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new WorkspaceScope { Account = "work", Folder = "INBOX" },
            await over.Workspace.Scope);
    }

    /// <summary>
    /// A role taken across mailboxes is a scope like any other, which is what turns three acts into one for somebody
    /// with three mailboxes.
    /// </summary>
    [Fact]
    public async Task SelectAsync_ARoleAcrossMailboxes_NarrowsTheWorkspaceToTheRole()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        var rows = await over.Tree.Rows;
        var unified = rows!.Single(row => row.Kind is MailboxRowKind.UnifiedRole);

        // Act
        await over.Tree.SelectAsync(unified, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new WorkspaceScope { Role = "Inbox" }, await over.Workspace.Scope);
    }

    /// <summary>
    /// What was selected inside the old scope is dropped rather than carried, because it named mail in a folder
    /// somebody has just left.
    /// </summary>
    [Fact]
    public async Task SelectAsync_AFolderChosenWhileSomethingWasSelected_LeavesTheOldSelectionBehind()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Account = "home", Selection = ImmutableArray.Create("117") },
            TestContext.Current.CancellationToken);

        var rows = await over.Tree.Rows;
        var mailbox = rows!.First(row => row.Kind is MailboxRowKind.Account);

        // Act
        await over.Tree.SelectAsync(mailbox, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty((await over.Workspace.Scope)!.Selection);
    }

    /// <summary>A level of a mail server's hierarchy that is not a folder narrows nothing, because no route names one.</summary>
    [Fact]
    public async Task SelectAsync_ALevelNothingIsBoundTo_NarrowsNothing()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Tree.ToggleAsync(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);
        var rows = await over.Tree.Rows;
        var group = rows!.Single(row => row.Kind is MailboxRowKind.Group);

        // Act
        await over.Tree.SelectAsync(group, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(WorkspaceScope.Everything, await over.Workspace.Scope);
    }

    /// <summary>What was open and where somebody was is kept, which is what makes starting the client again opening it.</summary>
    [Fact]
    public async Task SelectAsync_AFolderSomebodyChose_IsKeptForTheNextRun()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Tree.ToggleAsync(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);
        var rows = await over.Tree.Rows;
        var folder = rows!.First(row => row.Kind is MailboxRowKind.Folder);

        // Act
        await over.Tree.SelectAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new WorkspaceScope { Account = "work", Folder = "INBOX" },
            over.Memory.Remembered.Scope);
        Assert.Equal([MailboxTreeShape.AccountKey("work")], over.Memory.Remembered.Expanded);
    }

    /// <summary>What is selected inside a scope is not kept, because it names mail that may not be in the copy next time.</summary>
    [Fact]
    public async Task SelectAsync_AScopeWithSomethingSelectedInside_KeepsThePlaceRatherThanTheSelection()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        var rows = await over.Tree.Rows;
        var mailbox = rows!.First(row => row.Kind is MailboxRowKind.Account);
        await over.Tree.SelectAsync(mailbox, TestContext.Current.CancellationToken);

        await over.Workspace.Scope.UpdateAsync(
            scope => scope! with { Selection = ImmutableArray.Create("117") },
            TestContext.Current.CancellationToken);

        // Act
        await over.Tree.ToggleAsync(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(over.Memory.Remembered.Scope.Selection);
        Assert.Equal("work", over.Memory.Remembered.Scope.Account);
    }

    /// <summary>A run opens where the last one was left, both in what is open and in what is in scope.</summary>
    [Fact]
    public async Task Rows_ARunAfterOneThatLeftTheTreeSomewhere_OpensThere()
    {
        // Arrange
        var remembered = new RememberedMailboxes(
            new WorkspaceScope { Account = "work", Folder = "INBOX" },
            ImmutableHashSet.Create(MailboxTreeShape.AccountKey("work")));

        using var over = await TreeOver.CreateAsync(TwoMailboxes, remembered);

        // Act
        var rows = await over.Tree.Rows;

        // Assert
        Assert.NotNull(rows);
        Assert.Equal(["INBOX"], rows.Where(row => row.IsSelected).Select(row => row.Name));
        Assert.Contains(rows, row => row.Kind is MailboxRowKind.Folder);
    }

    /// <summary>A deployment that stopped refreshing says so beside the tree rather than on each of its rows.</summary>
    [Fact]
    public async Task SynchronizationPaused_ADeploymentThatStoppedRefreshing_SaysSoBesideTheTree()
    {
        // Arrange
        using var paused = await TreeOver.CreateAsync("""{"synchronizationEnabled":false,"accounts":[]}""");
        using var refreshing = await TreeOver.CreateAsync(TwoMailboxes);

        // Act, Assert
        Assert.True(await paused.Tree.SynchronizationPaused);
        Assert.False(await refreshing.Tree.SynchronizationPaused);
    }

    /// <summary>An owner who owns no mailbox draws nothing, which is a state rather than a failure.</summary>
    [Fact]
    public async Task Rows_AnOwnerWhoOwnsNoMailbox_IsEmptyRatherThanAFailure()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync("""{"synchronizationEnabled":true,"accounts":[]}""");

        // Act
        var rows = await over.Tree.Rows;

        // Assert
        Assert.True(rows is null || rows.Count is 0);
    }

    /// <summary>A read that did not arrive reaches the pane as the feed's error axis rather than as no mailboxes.</summary>
    [Fact]
    public async Task Rows_ADeploymentThatDidNotAnswer_ReachesTheScreenAsAFailure()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(_ => throw new HttpRequestException("nothing is answering"));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await over.Tree.Rows);

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
    }

    /// <summary>
    /// Asking again is the session's act rather than the tree's, which is what keeps the two from disagreeing about
    /// whether the deployment is there — and what makes the tree follow a connection that came back.
    /// </summary>
    [Fact]
    public async Task AskAgainAsync_PressedOnAFailedRead_AsksTheSessionAgainAndReadsTheFoldersWithIt()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);
        await over.Tree.Rows;

        // Act
        await over.Tree.AskAgainAsync(TestContext.Current.CancellationToken);
        await over.Tree.Rows;

        // Assert
        Assert.Equal(1, over.Session.Refreshes);
        Assert.Equal(2, over.Harness.Deployment.Requests.Count);
    }

    /// <summary>A row with no key is not a row, so nothing can be opened by naming nothing.</summary>
    [Fact]
    public async Task ToggleAsync_ARowWithNoKey_IsRefused()
    {
        // Arrange
        using var over = await TreeOver.CreateAsync(TwoMailboxes);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await over.Tree.ToggleAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    /// <summary>A tree that could be built without one of its collaborators would be one describing nowhere.</summary>
    [Fact]
    public async Task Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(TwoMailboxes));
        using var session = Session();
        var memory = new StubMailboxTreeMemory();
        var workspace = new SharedWorkspace(memory);
        var clock = new StubClock(Now);
        var words = Words();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(null!, session, workspace, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(harness.Client, null!, workspace, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(harness.Client, session, null!, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(harness.Client, session, workspace, null!, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(harness.Client, session, workspace, memory, null!, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMailboxTree(harness.Client, session, workspace, memory, clock, null!));
    }

    private static StubClientSession Session() =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", ["mailfathom.mail.read"])));

    private static StubStringLocalizer Words() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Mailboxes.Everything"] = "every mailbox",
        ["Mailboxes.Unified"] = "{0} · every mailbox",
        ["Mailboxes.Role.Inbox"] = "Inbox",
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

    /// <summary>One tree and everything it is composed over, owned together so a test states its arrangement once.</summary>
    private sealed class TreeOver : IDisposable
    {
        private TreeOver(DeploymentHarness harness, RememberedMailboxes? remembered)
        {
            this.Harness = harness;
            this.Session = DeploymentMailboxTreeTests.Session();
            this.Memory = new StubMailboxTreeMemory(remembered);
            this.Workspace = new SharedWorkspace(this.Memory);

            this.Tree = new DeploymentMailboxTree(
                this.Harness.Client,
                this.Session,
                this.Workspace,
                this.Memory,
                new StubClock(Now),
                Words());
        }

        internal static ValueTask<TreeOver> CreateAsync(string answered, RememberedMailboxes? remembered = null) =>
            CreateAsync(_ => StubTransport.JsonResponse(answered), remembered);

        internal static async ValueTask<TreeOver> CreateAsync(
            Func<HttpRequestMessage, HttpResponseMessage> deployment,
            RememberedMailboxes? remembered = null) =>
            new(await DeploymentHarness.CreateAsync(deployment), remembered);

        internal DeploymentHarness Harness { get; }

        internal StubClientSession Session { get; }

        internal StubMailboxTreeMemory Memory { get; }

        internal SharedWorkspace Workspace { get; }

        internal DeploymentMailboxTree Tree { get; }

        public void Dispose()
        {
            this.Session.Dispose();
            this.Harness.Dispose();
        }
    }
}
