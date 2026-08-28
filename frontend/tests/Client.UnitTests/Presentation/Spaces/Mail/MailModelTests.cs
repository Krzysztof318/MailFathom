// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Spaces.Mail;
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
        await using var withheldModel = new MailModel(withheld, new StubMessageList(), new StubMailThread());

        using var offered = SessionOffering("mailfathom.mail.read");
        await using var offeredModel = new MailModel(offered, new StubMessageList(), new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

        // Act
        var rows = await model.Messages;

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows!.Select(row => row.Key));
        Assert.Same(list.Chosen, model.Chosen);
        Assert.Same(list.PagingFailed, model.PagingFailed);
    }

    /// <summary>Both ends of the loaded window are reported, because each is a control of its own on the screen.</summary>
    [Fact]
    public async Task HasMoreAfter_AWindowWithMailEitherSideOfIt_ReportsEachEndSeparately()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        var list = new StubMessageList(Row(1)) { More = true, Earlier = false };
        await using var model = new MailModel(session, list, new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

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
        await using var model = new MailModel(session, new StubMessageList(), new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

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
        await using var model = new MailModel(session, list, new StubMailThread());

        // Act
        await model.ToggleUnreadOnly(TestContext.Current.CancellationToken);
        await model.ToggleUnreadOnly(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true, false], list.Arranged.Select(arrangement => arrangement.UnreadOnly));
    }

    /// <summary>A space that could be built without either of these would be one that can say neither what it shows nor whether it may.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new MailModel(null!, new StubMessageList(), new StubMailThread()));

        Assert.Throws<ArgumentNullException>(() => new MailModel(session, null!, new StubMailThread()));
        Assert.Throws<ArgumentNullException>(() => new MailModel(session, new StubMessageList(), null!));
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

    private static StubClientSession SessionOffering(params string[] permissions) =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", permissions)));
}
