// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>The list over one deployment: what it reads, what paging it does, and what it writes back.</summary>
public sealed class DeploymentMessageListTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>How long a test waits on a request it is holding open before it gives up rather than hanging the run.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The list opens on the leading page of the place in force, which is one request for a screenful.</summary>
    [Fact]
    public async Task Rows_ADeploymentAnswering_DrawsThePageItServed()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 3, next: "after-3", previous: null)));

        // Act
        var rows = await over.List.Rows;

        // Assert
        Assert.NotNull(rows);
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2), MailMessages.Key(3)], rows.Select(row => row.Key));

        var asked = Assert.Single(over.Harness.Deployment.Requests).RequestUri;
        Assert.Equal("/api/client/emails", asked.AbsolutePath);
        Assert.Contains($"pageSize={DeploymentMessageList.PageSize}", asked.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor=", asked.Query, StringComparison.Ordinal);
    }

    /// <summary>The place somebody narrowed to is what the deployment is asked about, rather than every mailbox.</summary>
    [Fact]
    public async Task Rows_AScopeNarrowedToAFolder_AsksTheDeploymentAboutThatFolder()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 1, next: null, previous: null)));
        await over.List.Rows;

        // Act
        await over.Workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Account = "work", Folder = "INBOX" },
            TestContext.Current.CancellationToken);

        await over.Until(() =>
            ValueTask.FromResult(over.Asked.Any(query => query.Contains("folder=INBOX", StringComparison.Ordinal))));

        // Assert
        Assert.Contains(
            over.Asked,
            query => query.Contains("account=work", StringComparison.Ordinal)
                && query.Contains("folder=INBOX", StringComparison.Ordinal));
    }

    /// <summary>
    /// The list reads the place rather than the scope, which is load-bearing rather than an optimization: it writes
    /// what is selected back into the same scope, so keying it on the whole of one would read the folder again on every
    /// click.
    /// </summary>
    [Fact]
    public async Task Rows_AScopeWhoseSelectionChanged_DoesNotReadTheFolderAgain()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 3, next: null, previous: null)));
        var rows = await over.List.Rows;

        // Act
        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(rows![0]),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Workspace.Scope)?.Selection.Count is 1);

        // Assert
        Assert.Single(over.Harness.Deployment.Requests);
    }

    /// <summary>
    /// What is selected in the list is the application's scope rather than the control's, which is what lets the rest
    /// of the client act on it.
    /// </summary>
    [Fact]
    public async Task Chosen_RowsSomebodySelected_ReachTheWorkspaceAsWhatIsInScope()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 3, next: null, previous: null)));
        var rows = await over.List.Rows;

        // Act
        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(rows![0], rows[2]),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Workspace.Scope)?.Selection.Count is 2);

        // Assert
        var scope = await over.Workspace.Scope;
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(3)], scope!.Selection);
    }

    /// <summary>Scrolling on takes the next page onto what is loaded, asked for with the cursor the last one answered with.</summary>
    [Fact]
    public async Task ShowMoreAsync_MoreMailAfterWhatIsLoaded_TakesThePageOntoTheList()
    {
        // Arrange
        using var over = new ListOver(request => Answer(
            Cursor(request) is null
                ? Page(1, 2, next: "after-2", previous: null)
                : Page(3, 2, next: null, previous: "before-3")));

        await over.List.Rows;

        // Act
        await over.List.ShowMoreAsync(TestContext.Current.CancellationToken);

        // Assert
        var rows = await over.List.Rows;
        Assert.Equal(
            [MailMessages.Key(1), MailMessages.Key(2), MailMessages.Key(3), MailMessages.Key(4)],
            rows!.Select(row => row.Key));
        Assert.Contains("cursor=after-2", over.Asked[^1], StringComparison.Ordinal);
        Assert.Contains("direction=forward", over.Asked[^1], StringComparison.Ordinal);
    }

    /// <summary>Scrolling back asks the other way, which is how a window that dropped a page gets it again.</summary>
    [Fact]
    public async Task ShowEarlierAsync_MoreMailBeforeWhatIsLoaded_AsksForItTheOtherWay()
    {
        // Arrange
        using var over = new ListOver(request => Answer(
            Cursor(request) is null
                ? Page(3, 2, next: null, previous: "before-3")
                : Page(1, 2, next: "after-2", previous: null)));

        await over.List.Rows;

        // Act
        await over.List.ShowEarlierAsync(TestContext.Current.CancellationToken);

        // Assert
        var rows = await over.List.Rows;
        Assert.Equal(
            [MailMessages.Key(1), MailMessages.Key(2), MailMessages.Key(3), MailMessages.Key(4)],
            rows!.Select(row => row.Key));
        Assert.Contains("cursor=before-3", over.Asked[^1], StringComparison.Ordinal);
        Assert.Contains("direction=backward", over.Asked[^1], StringComparison.Ordinal);
    }

    /// <summary>At the end of the list there is no cursor to ask with, so the deployment is not asked at all.</summary>
    [Fact]
    public async Task ShowMoreAsync_AtTheEndOfTheList_AsksTheDeploymentForNothing()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 2, next: null, previous: null)));
        await over.List.Rows;

        // Act
        await over.List.ShowMoreAsync(TestContext.Current.CancellationToken);
        await over.List.ShowEarlierAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(over.Harness.Deployment.Requests);
        Assert.False(await over.List.HasMoreAfter);
        Assert.False(await over.List.HasMoreBefore);
    }

    /// <summary>
    /// A page that did not arrive is reported beside the list rather than as the list's own failure: what is already
    /// drawn is still true, and putting the whole list into an error state would take a folder's worth of mail off the
    /// screen over one request.
    /// </summary>
    [Fact]
    public async Task ShowMoreAsync_APageThatDidNotArrive_IsReportedBesideTheListRatherThanAsIt()
    {
        // Arrange
        using var over = new ListOver(request => Cursor(request) is null
            ? Answer(Page(1, 2, next: "after-2", previous: null))
            : StubTransport.JsonResponse("{}", HttpStatusCode.InternalServerError));

        await over.List.Rows;

        // Act
        await over.List.ShowMoreAsync(TestContext.Current.CancellationToken);

        // Assert
        var rows = await over.List.Rows;
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows!.Select(row => row.Key));
        Assert.True(await over.List.PagingFailed);
    }

    /// <summary>
    /// A page that failed for a folder somebody has already left says nothing about the one they are in: the abandoned
    /// read is the loser of an ordinary race, and reporting it beside the new folder would put a warning over mail that
    /// arrived.
    /// </summary>
    [Fact]
    public async Task ShowMoreAsync_APageThatFailedForAPlaceSomebodyLeft_IsNotReportedBesideTheNewOne()
    {
        // Arrange
        var cancellation = TestContext.Current.CancellationToken;

        using var reached = new ManualResetEventSlim(false);
        using var released = new ManualResetEventSlim(false);

        using var over = new ListOver(request =>
        {
            if (Cursor(request) is not null)
            {
                reached.Set();
                released.Wait(Patience, cancellation);

                return StubTransport.JsonResponse("{}", HttpStatusCode.InternalServerError);
            }

            return request.RequestUri!.Query.Contains("folder=INBOX", StringComparison.Ordinal)
                ? Answer(Page(5, 2, next: null, previous: null))
                : Answer(Page(1, 2, next: "after-2", previous: null));
        });

        await over.List.Rows;

        // The read is started on a thread of its own because the scripted deployment holds the request open on the one
        // that made it, which is how a test states that a page is still in flight.
        var paging = Task.Run(
            async () => await over.List.ShowMoreAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.True(reached.Wait(Patience, cancellation));

        // Act
        await over.Workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Account = "work", Folder = "INBOX" },
            TestContext.Current.CancellationToken);

        await over.Until(async () => await over.List.Rows is [{ } first, _] && first.Key == MailMessages.Key(5));

        released.Set();
        await paging;

        // Assert
        Assert.False(await over.List.PagingFailed);
    }

    /// <summary>Coming back to a folder is coming back: the leading page it was left on is what is asked for again.</summary>
    [Fact]
    public async Task Rows_APlaceSomebodyHasBeenInBefore_ReopensWhereItWasLeft()
    {
        // Arrange
        using var over = new ListOver(
            _ => Answer(Page(9, 2, next: "after-10", previous: "before-9")),
            new RememberedMessageList(
                MessagePlace.Everything.RememberedAs,
                "after-8",
                MailTimelinePageDirection.Forward,
                MessageListArrangement.Default));

        // Act
        var rows = await over.List.Rows;

        // Assert
        Assert.Equal([MailMessages.Key(9), MailMessages.Key(10)], rows!.Select(row => row.Key));
        Assert.Contains("cursor=after-8", Assert.Single(over.Asked), StringComparison.Ordinal);
    }

    /// <summary>
    /// A cursor the deployment no longer honours is a position to give up rather than an error to show: nobody typed
    /// it, and the list somebody asked for still exists.
    /// </summary>
    [Fact]
    public async Task Rows_ACursorTheDeploymentRefuses_OpensAtTheLeadingEndInstead()
    {
        // Arrange
        using var over = new ListOver(
            request => Cursor(request) is null
                ? Answer(Page(1, 2, next: "after-2", previous: null))
                : StubTransport.JsonResponse("{}", HttpStatusCode.BadRequest),
            new RememberedMessageList(
                MessagePlace.Everything.RememberedAs,
                "issued-under-another-arrangement",
                MailTimelinePageDirection.Forward,
                MessageListArrangement.Default));

        // Act
        var rows = await over.List.Rows;

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows!.Select(row => row.Key));
        Assert.Equal(2, over.Asked.Count);
        Assert.DoesNotContain("cursor=", over.Asked[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// A remembered position naming no cursor is the leading end of the list, which is read forward whatever direction
    /// was written beside it: there is no row to read away from, and the deployment refuses such a page rather than
    /// answering it.
    /// </summary>
    [Fact]
    public async Task Rows_ARememberedPositionNamingNoCursor_IsReadForwardWhateverWasWrittenBesideIt()
    {
        // Arrange
        using var over = new ListOver(
            request => Answer(Page(1, 2, next: null, previous: null)),
            new RememberedMessageList(
                MessagePlace.Everything.RememberedAs,
                Cursor: null,
                MailTimelinePageDirection.Backward,
                MessageListArrangement.Default));

        // Act
        var rows = await over.List.Rows;

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows!.Select(row => row.Key));
        Assert.Contains("direction=forward", Assert.Single(over.Asked), StringComparison.Ordinal);
    }

    /// <summary>
    /// A place is reopened under the arrangement it was left in, because a cursor is only honoured against the filters
    /// and the order it was issued under.
    /// </summary>
    [Fact]
    public async Task Rows_APlaceLeftUnderAnArrangement_ReopensUnderTheSameOne()
    {
        // Arrange
        using var over = new ListOver(
            _ => Answer(Page(1, 2, next: null, previous: null)),
            new RememberedMessageList(
                MessagePlace.Everything.RememberedAs,
                Cursor: null,
                MailTimelinePageDirection.Forward,
                new MessageListArrangement { Order = MailTimelineOrder.OldestFirst, UnreadOnly = true }));

        // Act
        await over.List.Rows;

        // Assert
        var asked = Assert.Single(over.Asked);
        Assert.Contains("order=oldestFirst", asked, StringComparison.Ordinal);
        Assert.Contains("unread=true", asked, StringComparison.Ordinal);
        Assert.Equal(
            new MessageListArrangement { Order = MailTimelineOrder.OldestFirst, UnreadOnly = true },
            await over.List.Arrangement);
    }

    /// <summary>
    /// Arranging the list differently reads it again, because a new arrangement invalidates every cursor held under
    /// the old one.
    /// </summary>
    [Fact]
    public async Task ArrangeAsync_AListArrangedDifferently_ReadsItAgainUnderTheNewArrangement()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 2, next: null, previous: null)));
        await over.List.Rows;

        // Act
        await over.List.ArrangeAsync(
            MessageListArrangement.Default with { FlaggedOnly = true },
            TestContext.Current.CancellationToken);

        await over.Until(() => ValueTask.FromResult(over.Harness.Deployment.Requests.Count > 1));

        // Assert
        Assert.Contains("flagged=true", over.Asked[^1], StringComparison.Ordinal);
        Assert.Equal(MessageListArrangement.Default with { FlaggedOnly = true }, await over.List.Arrangement);
    }

    /// <summary>What was left is written down as the request that reopens the leading page, and not as a row.</summary>
    [Fact]
    public async Task Rows_AListThatHasBeenPaged_WritesDownTheRequestThatReopensIt()
    {
        // Arrange
        using var over = new ListOver(request => Answer(
            Cursor(request) is null
                ? Page(1, 2, next: "after-2", previous: null)
                : Page(3, 2, next: null, previous: "before-3")));

        await over.List.Rows;

        // Act
        await over.List.ShowMoreAsync(TestContext.Current.CancellationToken);
        // Assert
        var written = over.Memory.Written[^1];
        Assert.Equal(MessagePlace.Everything.RememberedAs, written.PlaceKey);
        Assert.Null(written.Cursor);
        Assert.Equal(MailTimelinePageDirection.Forward, written.Direction);
        Assert.Equal(MessageListArrangement.Default, written.Arrangement);
    }

    /// <summary>Asking again is one act: the session reads the deployment again, and the list follows it.</summary>
    [Fact]
    public async Task AskAgainAsync_AReadSomebodyAskedForAgain_ReachesTheSessionAndTheDeployment()
    {
        // Arrange
        using var over = new ListOver(_ => Answer(Page(1, 2, next: null, previous: null)));
        await over.List.Rows;

        // Act
        await over.List.AskAgainAsync(TestContext.Current.CancellationToken);
        await over.Until(() => ValueTask.FromResult(over.Harness.Deployment.Requests.Count > 1));

        // Assert
        Assert.Equal(1, over.Session.Refreshes);
        Assert.True(over.Harness.Deployment.Requests.Count > 1);
    }

    /// <summary>A list built over nothing would be one that could not say what it reads or where it draws it from.</summary>
    [Fact]
    public void Construction_AMissingCollaborator_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => Answer(Page(1, 1, next: null, previous: null)));
        using var session = Session();
        var memory = new StubMessageListMemory();
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        var clock = new StubClock(Now);
        var words = Words();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(null!, session, workspace, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(harness.Client, null!, workspace, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(harness.Client, session, null!, memory, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(harness.Client, session, workspace, null!, clock, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(harness.Client, session, workspace, memory, null!, words));
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentMessageList(harness.Client, session, workspace, memory, clock, null!));
    }

    private static StubClientSession Session() =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", ["mailfathom.mail.read"])));

    private static StubStringLocalizer Words() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [MessageWords.NoSenderKey] = "Unknown sender",
        [MessageWords.NoSubjectKey] = "No subject",
        [MessageWords.NoDateKey] = "No date",
        [MessageWords.MoreRecipientsKey] = "{0} and {1} more",
        [MessageWords.AnnouncementKey] = "{0}, {1}, {2}",
        [MessageWords.UnreadKey] = "unread",
        [MessageWords.FlaggedKey] = "flagged",
        [MessageWords.AnsweredKey] = "answered",
        [MessageWords.AttachmentsKey] = "attachment",
    });

    private static HttpResponseMessage Answer(string page) => StubTransport.JsonResponse(page);

    /// <summary>Reads the cursor a request carried, which is what tells one page of a script apart from the next.</summary>
    private static string? Cursor(HttpRequestMessage request)
    {
        var query = request.RequestUri?.Query ?? string.Empty;

        return query
            .TrimStart('?')
            .Split('&')
            .FirstOrDefault(stated => stated.StartsWith("cursor=", StringComparison.Ordinal))?["cursor=".Length..];
    }

    /// <summary>Writes a page of mail as the deployment serves one.</summary>
    private static string Page(int first, int count, string? next, string? previous)
    {
        var emails = Enumerable
            .Range(first, count)
            .Select(number =>
                $$"""
                  {
                    "id": "{{MailMessages.Key(number)}}",
                    "account": "work",
                    "folder": "INBOX",
                    "subject": "Message {{number.ToString(CultureInfo.InvariantCulture)}}",
                    "receivedAt": "2026-08-25T11:50:00+00:00",
                    "senderAddress": "someone@example.test",
                    "senderDisplayName": "Someone",
                    "toAddresses": [ "owner@example.test" ],
                    "unread": false,
                    "flagged": false,
                    "answered": false,
                    "hasAttachments": false,
                    "attachmentCount": 0,
                    "sizeOctets": 1024,
                    "preview": "The opening of it"
                  }
                  """);

        return $$"""
                 {
                   "emails": [ {{string.Join(",", emails)}} ],
                   "nextCursor": {{Written(next)}},
                   "previousCursor": {{Written(previous)}},
                   "pageSize": 50
                 }
                 """;
    }

    private static string Written(string? cursor) => cursor is null ? "null" : $"\"{cursor}\"";

    /// <summary>One list and everything it is composed over, owned together so a test states its arrangement once.</summary>
    private sealed class ListOver : IDisposable
    {
        internal ListOver(
            Func<HttpRequestMessage, HttpResponseMessage> deployment,
            params RememberedMessageList[] remembered)
        {
            this.Harness = new DeploymentHarness(deployment);
            this.Session = DeploymentMessageListTests.Session();
            this.Memory = new StubMessageListMemory(remembered);
            this.Workspace = new SharedWorkspace(new StubMailboxTreeMemory());

            this.List = new DeploymentMessageList(
                this.Harness.Client,
                this.Session,
                this.Workspace,
                this.Memory,
                new StubClock(Now),
                Words());
        }

        internal DeploymentHarness Harness { get; }

        internal StubClientSession Session { get; }

        internal StubMessageListMemory Memory { get; }

        internal SharedWorkspace Workspace { get; }

        internal DeploymentMessageList List { get; }

        /// <summary>Gets the query string of every request the deployment was asked, in order.</summary>
        internal IReadOnlyList<string> Asked =>
            [.. this.Harness.Deployment.Requests.Select(request => request.RequestUri.Query)];

        /// <summary>Waits until a consequence of a feed re-evaluating has happened, or fails the test.</summary>
        /// <param name="settled">What is being waited for.</param>
        /// <returns>The wait.</returns>
        /// <remarks>
        /// <para>
        /// Only the four acts that reach the list through a feed rather than through a call are waited on: writing the
        /// scope, writing what is selected, and the two that ask the list to be read again. Each of those is finished
        /// by MVUX re-evaluating a feed on a thread of its own, and no await a test holds is the one that finishes it.
        /// Everything the list does inside an awaited method — every page, and everything written down beside one —
        /// is asserted straight after that await instead.
        /// </para>
        /// <para>
        /// Polling rather than a recorded feed, because the package that records one does not compile against the Uno
        /// version this stack pins; <c>frontend/tests/AGENTS.md</c> carries that and names the model's own surface as
        /// the seam until it does. The bound is generous and only reached by a failing test, so a run costs the wait
        /// itself rather than the bound.
        /// </para>
        /// </remarks>
        internal async Task Until(Func<ValueTask<bool>> settled)
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                if (await settled())
                {
                    return;
                }

                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Fail("The list did not settle on what the test was waiting for.");
        }

        public void Dispose()
        {
            this.Session.Dispose();
            this.Harness.Dispose();
        }
    }
}
