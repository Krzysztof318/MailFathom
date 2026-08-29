// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Search;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail;

/// <summary>The Mail space, which reads whether it may be offered at all and leaves the mailboxes to the tree.</summary>
public sealed class MailModelTests
{
    /// <summary>
    /// The space reads whether it may be offered from the session rather than from a request the deployment refused,
    /// which is what keeps a credential that may not read mail off a screen that would have failed on its own terms.
    /// </summary>
    [Fact]
    public async Task WithholdsMail_AGrantNotCarryingReading_SaysSoRatherThanLeavingTheOfferToBeInverted()
    {
        // Arrange
        using var withheld = SessionOffering("mailfathom.mail.ask");
        await using var withheldModel = new MailModel(
            withheld,
            new StubMessageList(),
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        using var offered = SessionOffering("mailfathom.mail.read");
        await using var offeredModel = new MailModel(
            offered,
            new StubMessageList(),
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act, Assert
        Assert.True(await withheldModel.WithholdsMail);
        Assert.False(await offeredModel.WithholdsMail);
    }

    /// <summary>
    /// The list is the run's own read through this space rather than one built here, so leaving the space and coming
    /// back finds it where it was instead of reading its first page again.
    /// </summary>
    [Fact]
    public async Task Messages_TheRunsOwnList_IsReadThroughRatherThanCopied()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList(Row(1), Row(2));
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        var rows = await model.Messages;

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows!.Select(row => row.Key));
        Assert.Same(list.Chosen, model.Chosen);
        Assert.Same(list.PagingFailed, model.PagingFailed);
    }

    /// <summary>
    /// Selecting in the list is the application's selection rather than the control's, so a question asked next is
    /// asked about the rows somebody picked.
    /// </summary>
    [Fact]
    public async Task ChooseAsync_RowsSomebodySelected_ReachTheListAsWhatIsChosen()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList(Row(1), Row(2));
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());
        var rows = await model.Messages;

        // Act
        await model.ChooseAsync(ImmutableList.Create(rows![0]), TestContext.Current.CancellationToken);

        // Assert
        var chosen = await model.Chosen;
        Assert.Equal([MailMessages.Key(1)], chosen!.Select(row => row.Key));
    }

    /// <summary>
    /// The conversation is the run's own read through this space as well, so a citation that opened one and a row that
    /// opened one are the same screen rather than two.
    /// </summary>
    [Fact]
    public async Task Thread_TheRunsOwnConversation_IsReadThroughRatherThanCopied()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var thread = new StubMailThread();
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            thread,
            new StubWorkspace(),
            new StubMailSearch());

        // Act, Assert
        Assert.Same(thread.Reading, model.Thread);
        Assert.Same(thread.Messages, model.ThreadMessages);
        Assert.Same(thread.HasMoreMessages, model.HasMoreThreadMessages);
        Assert.Same(thread.PagingFailed, model.ThreadPagingFailed);
    }

    /// <summary>The phone's message route reads the selected conversation row from the same run-wide thread as the wide composition.</summary>
    [Fact]
    public async Task OpenedThreadMessage_ANavigationRow_ReadsTheLiveRowFromTheRunsOwnConversation()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var first = ThreadRow(1);
        var navigation = ThreadRow(2);
        var opened = navigation with { Contribution = "Updated after navigation" };
        var thread = new StubMailThread(first, opened);
        await using var model = new MailModel(
            navigation,
            session,
            new StubMessageList(),
            thread,
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        var message = await model.OpenedThreadMessage;

        // Assert
        Assert.Same(opened, message);
    }

    /// <summary>The phone route can offer the whole-message read before the conversation row is expanded inline.</summary>
    [Fact]
    public void OffersStandaloneWholeMessage_ACollapsedConversationRow_OffersTheRead()
    {
        // Arrange
        var message = ThreadRow(1);

        // Act, Assert
        Assert.True(message.OffersStandaloneWholeMessage);
    }

    /// <summary>The message route can distinguish a live row from the sentinel shown while none is open.</summary>
    [Fact]
    public void IsClosed_ARowWithoutAnIdentity_ReportsTheRouteAsClosed()
    {
        // Arrange
        var opened = ThreadRow(1);
        var closed = opened with { Key = string.Empty };

        // Act, Assert
        Assert.False(opened.IsClosed);
        Assert.True(closed.IsClosed);
    }

    /// <summary>
    /// Every gesture the conversation column offers reaches the conversation the run holds, and reaches the member that
    /// gesture means: expanding a message and reading the whole of it are two different requests of the deployment, and
    /// one wired to the other would compile.
    /// </summary>
    [Fact]
    public async Task ToggleThreadMessage_EveryGestureTheColumnOffers_ReachesTheConversationTheRunHolds()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var thread = new StubMailThread();
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            thread,
            new StubWorkspace(),
            new StubMailSearch());

        var attachment = new MailAttachmentRequest(MailMessages.Identity(4), Position: 2);

        // Act
        await model.ToggleThreadMessage(MailMessages.Key(1), TestContext.Current.CancellationToken);
        await model.ShowWholeThreadMessage(MailMessages.Key(2), TestContext.Current.CancellationToken);
        await model.ShowThreadRemoteContent(MailMessages.Key(3), TestContext.Current.CancellationToken);
        await model.SaveAttachment(attachment, TestContext.Current.CancellationToken);
        model.CancelAttachment(attachment);
        await model.ShowMoreThreadMessages(TestContext.Current.CancellationToken);
        await model.RetryThread(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailMessages.Key(1)], thread.Toggled);
        Assert.Equal([MailMessages.Key(2)], thread.Whole);
        Assert.Equal([MailMessages.Key(3)], thread.Remote);
        Assert.Equal([attachment], thread.Saved);
        Assert.Equal([attachment], thread.Cancelled);
        Assert.Equal(1, thread.Pages);
        Assert.Equal(1, thread.Asks);
    }

    /// <summary>Both ends of the loaded window are reported, because each is a control of its own on the screen.</summary>
    [Fact]
    public async Task HasMoreAfter_AWindowWithMailEitherSideOfIt_ReportsEachEndSeparately()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList(Row(1)) { More = true, Earlier = false };
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act, Assert
        Assert.True(await model.HasMoreAfter);
        Assert.False(await model.HasMoreBefore);
    }

    /// <summary>Each of the arrangement's parts is stated on its own, because each is a control somebody presses.</summary>
    [Fact]
    public async Task ReadsOldestFirst_AListArrangedEveryWay_StatesEachPartOnItsOwn()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList();
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        await list.ArrangeAsync(
            new MessageListArrangement
            {
                Order = MailTimelineOrder.OldestFirst,
                UnreadOnly = true,
                FlaggedOnly = true,
                WithAttachmentsOnly = true,
                IncludeJunk = true,
            },
            TestContext.Current.CancellationToken);

        // Act, Assert
        Assert.True(await model.ReadsOldestFirst);
        Assert.True(await model.KeepsUnreadOnly);
        Assert.True(await model.KeepsFlaggedOnly);
        Assert.True(await model.KeepsWithAttachmentsOnly);
        Assert.True(await model.KeepsJunk);
        Assert.True(await model.KeepsLessThanEverything);
        Assert.False(await model.KeepsEverything);
    }

    /// <summary>
    /// A place holding no mail and a place whose mail this list is keeping out are not the same thing to be told, so
    /// both are stated as their own affirmative rather than one being read backwards in the view.
    /// </summary>
    [Fact]
    public async Task KeepsEverything_AListNobodyNarrowed_IsStatedBesideItsOppositeRatherThanDerivedFromIt()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act, Assert
        Assert.True(await model.KeepsEverything);
        Assert.False(await model.KeepsLessThanEverything);
    }

    /// <summary>Every control the space carries is the list's own act, which is what keeps the space free of one.</summary>
    [Fact]
    public async Task ShowMore_TheControlsTheSpaceCarries_ReachTheList()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList();
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        await model.ShowMore(TestContext.Current.CancellationToken);
        await model.ShowEarlier(TestContext.Current.CancellationToken);
        await model.RetryMessages(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, list.Forwards);
        Assert.Equal(1, list.Backwards);
        Assert.Equal(1, list.Asks);
    }

    /// <summary>Reading from the other end is one control, so pressing it twice reads the list the way it started.</summary>
    [Fact]
    public async Task ReverseOrder_AListReadFromTheOtherEnd_ReadsItBackTheOriginalWayWhenPressedAgain()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList();
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        await model.ReverseOrder(TestContext.Current.CancellationToken);
        await model.ReverseOrder(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [MailTimelineOrder.OldestFirst, MailTimelineOrder.NewestFirst],
            list.Arranged.Select(arrangement => arrangement.Order));
    }

    /// <summary>
    /// A filter is composed from what is in force rather than from what a control shows, which is what keeps one toggle
    /// from undoing the change beside it.
    /// </summary>
    [Fact]
    public async Task ToggleUnreadOnly_AFilterPutInForceBesideAnother_LeavesTheOtherWhereItWas()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList();
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        await model.ToggleUnreadOnly(TestContext.Current.CancellationToken);
        await model.ToggleFlaggedOnly(TestContext.Current.CancellationToken);
        await model.ToggleWithAttachmentsOnly(TestContext.Current.CancellationToken);
        await model.ToggleJunk(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new MessageListArrangement
            {
                UnreadOnly = true,
                FlaggedOnly = true,
                WithAttachmentsOnly = true,
                IncludeJunk = true,
            },
            list.Arranged[^1]);
    }

    /// <summary>A filter put in force is taken out of force by the same control, because one control does both.</summary>
    [Fact]
    public async Task ToggleUnreadOnly_AFilterPressedTwice_IsTakenOutOfForceAgain()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList();
        await using var model = new MailModel(
            session,
            list,
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act
        await model.ToggleUnreadOnly(TestContext.Current.CancellationToken);
        await model.ToggleUnreadOnly(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true, false], list.Arranged.Select(arrangement => arrangement.UnreadOnly));
    }

    [Fact]
    public async Task UseBodySelection_AFragmentSomebodyChose_PutsItIntoTheSharedScopeWithoutLosingTheMessage()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var workspace = new StubWorkspace(
            WorkspaceScope.Everything with { Selection = [MailMessages.Key(1)] });
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            new StubMailThread(),
            workspace,
            new StubMailSearch());

        // Act
        await model.UseBodySelection("the selected passage", TestContext.Current.CancellationToken);

        // Assert
        var scope = await workspace.Scope;
        Assert.Equal([MailMessages.Key(1)], scope!.Selection);
        Assert.Equal("the selected passage", scope.BodySelection);
    }

    /// <summary>The search is the run's own list, and every search gesture reaches that same object.</summary>
    [Fact]
    public async Task Search_TheRunsOwnRankedList_IsReadAndActedThroughRatherThanCopied()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var search = new StubMailSearch(Row(1));
        var recent = new RecentMailSearch("quarter", new MailSearchQuery { Query = "quarter" });
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            new StubMailThread(),
            new StubWorkspace(),
            search);

        // Act
        await model.OpenSearch(TestContext.Current.CancellationToken);
        await model.CloseSearch(TestContext.Current.CancellationToken);
        await model.UseCurrentSearchScope(TestContext.Current.CancellationToken);
        await model.SearchMail(TestContext.Current.CancellationToken);
        await model.ShowMoreSearchResults(TestContext.Current.CancellationToken);
        await model.WidenSearch(TestContext.Current.CancellationToken);
        await model.OpenSearchResult(Row(1), TestContext.Current.CancellationToken);
        await model.RepeatSearch(recent, TestContext.Current.CancellationToken);
        await model.ClearSearchAccount(TestContext.Current.CancellationToken);
        await model.ClearSearchFolder(TestContext.Current.CancellationToken);
        await model.ClearSearchSender(TestContext.Current.CancellationToken);
        await model.ClearSearchRecipient(TestContext.Current.CancellationToken);
        await model.ClearSearchReceivedOnOrAfter(TestContext.Current.CancellationToken);
        await model.ClearSearchReceivedBefore(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailMessages.Key(1)], (await model.SearchResults)!.Select(row => row.Key));
        Assert.Equal(1, search.Opens);
        Assert.Equal(1, search.Closes);
        Assert.Equal(1, search.ScopeUses);
        Assert.Equal(1, search.Searches);
        Assert.Equal(1, search.Pages);
        Assert.Equal(1, search.Widens);
        Assert.Equal([MailMessages.Key(1)], search.Opened.Select(row => row.Key));
        Assert.Equal([recent], search.Repeated);
        Assert.Equal(
            [
                MailSearchFilter.Account,
                MailSearchFilter.Folder,
                MailSearchFilter.Sender,
                MailSearchFilter.Recipient,
                MailSearchFilter.ReceivedOnOrAfter,
                MailSearchFilter.ReceivedBefore,
            ],
            search.Cleared);
    }

    /// <summary>A space that could be built without either of these would be one that can say neither what it shows nor whether it may.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new MailModel(
                null!,
                new StubMessageList(),
                new StubMailThread(),
                new StubWorkspace(),
                new StubMailSearch()));

        Assert.Throws<ArgumentNullException>(
            () => new MailModel(session, null!, new StubMailThread(), new StubWorkspace(), new StubMailSearch()));
        Assert.Throws<ArgumentNullException>(
            () => new MailModel(session, new StubMessageList(), null!, new StubWorkspace(), new StubMailSearch()));
        Assert.Throws<ArgumentNullException>(
            () => new MailModel(session, new StubMessageList(), new StubMailThread(), null!, new StubMailSearch()));
        Assert.Throws<ArgumentNullException>(
            () => new MailModel(session, new StubMessageList(), new StubMailThread(), new StubWorkspace(), null!));
    }

    /// <summary>A selection nobody named is a caller's mistake rather than a list with nothing chosen.</summary>
    [Fact]
    public async Task ChooseAsync_AMissingSelection_IsRefused()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = new MailModel(
            session,
            new StubMessageList(),
            new StubMailThread(),
            new StubWorkspace(),
            new StubMailSearch());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => model.ChooseAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }

    private static MessageRow Row(int number) => new(
        MailMessages.Key(number),
        MailThreads.Identity,
        "Someone",
        "Quarterly review",
        Preview: string.Empty,
        "09:41",
        "Someone, Quarterly review, 09:41",
        IsUnread: false,
        IsFlagged: false,
        IsAnswered: false,
        HasAttachments: false,
        AttachmentCount: 0);

    private static ThreadMessageRow ThreadRow(int number) => new(
        MailMessages.Key(number),
        "Someone",
        "Quarterly review",
        "Owner",
        "What this one added",
        "09:41",
        "Someone, Quarterly review, 09:41",
        IsExpanded: false,
        IsOpenedAt: false,
        IsUnread: false,
        IsFlagged: false,
        IsAnswered: false,
        HasAttachments: false,
        AttachmentCount: 0,
        Message: null,
        WholeMessage: null,
        IsReadingWholeMessage: false,
        WholeMessageFailed: false);

    private static StubClientSession SessionOffering(params string[] permissions) =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", permissions)));
}
