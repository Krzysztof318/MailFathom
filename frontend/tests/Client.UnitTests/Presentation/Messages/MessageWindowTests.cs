// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>The bounded part of a list that is loaded, and what taking a page onto it does.</summary>
public sealed class MessageWindowTests
{
    private static readonly MessagePlace Inbox = new("work", "INBOX", Role: null);

    /// <summary>A window opens on the page it was given, with the cursors that page answered with.</summary>
    [Fact]
    public void Opening_AFirstPage_HoldsItAndTheCursorsThatContinueIt()
    {
        // Arrange
        var page = Page(1, next: "after-1", previous: null, readCursor: null);

        // Act
        var window = MessageWindow.Opening(Inbox, MessageListArrangement.Default, page);

        // Assert
        Assert.Equal(2, window.Messages.Count);
        Assert.Equal("after-1", window.ForwardCursor);
        Assert.Null(window.BackwardCursor);
        Assert.True(window.HasMoreAfter);
        Assert.False(window.HasMoreBefore);
        Assert.False(window.IsEmpty);
    }

    /// <summary>A place holding no mail opens on no page at all, which is a state rather than a page to keep.</summary>
    [Fact]
    public void Opening_APageWithNothingInIt_HoldsNothing()
    {
        // Act
        var window = MessageWindow.Opening(Inbox, MessageListArrangement.Default, EmptyPage());

        // Assert
        Assert.True(window.IsEmpty);
        Assert.Null(window.ForwardCursor);
        Assert.Null(window.BackwardCursor);
        Assert.Null(window.LeadingPage);
    }

    /// <summary>Taking a page forward appends its rows and moves the cursor the next page is asked with.</summary>
    [Fact]
    public void Extended_APageTakenForward_AppendsItAndMovesTheForwardCursor()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(1, next: "after-1", previous: null, readCursor: null));

        // Act
        var extended = window.Extended(
            Page(3, next: "after-3", previous: "before-3", readCursor: "after-1"),
            MailTimelinePageDirection.Forward,
            Inbox,
            MessageListArrangement.Default);

        // Assert
        Assert.Equal(
            [MailMessages.Identity(1), MailMessages.Identity(2), MailMessages.Identity(3), MailMessages.Identity(4)],
            extended.Messages.Select(message => message.Id));
        Assert.Equal("after-3", extended.ForwardCursor);
        Assert.Null(extended.BackwardCursor);
    }

    /// <summary>Taking a page backward puts its rows in front and moves the cursor the previous page is asked with.</summary>
    [Fact]
    public void Extended_APageTakenBackward_PrependsItAndMovesTheBackwardCursor()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(3, next: "after-3", previous: "before-3", readCursor: "after-1"));

        // Act
        var extended = window.Extended(
            Page(1, next: "after-1", previous: null, readCursor: "before-3"),
            MailTimelinePageDirection.Backward,
            Inbox,
            MessageListArrangement.Default);

        // Assert
        Assert.Equal(
            [MailMessages.Identity(1), MailMessages.Identity(2), MailMessages.Identity(3), MailMessages.Identity(4)],
            extended.Messages.Select(message => message.Id));
        Assert.Null(extended.BackwardCursor);
        Assert.Equal("after-3", extended.ForwardCursor);
    }

    /// <summary>
    /// The window is bounded, so scrolling on drops the far page rather than holding a folder's worth of mail. What
    /// makes that safe is the page that becomes the far end carrying the cursor that reads back towards what went.
    /// </summary>
    [Fact]
    public void Extended_MorePagesThanTheBound_DropsTheFarOneAndKeepsAWayBackToIt()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(1, next: "after-1", previous: null, readCursor: null));

        // Act
        for (var page = 1; page <= MessageWindow.MaximumPages; page++)
        {
            window = window.Extended(
                Page(
                    (page * 2) + 1,
                    next: $"after-{page + 1}",
                    previous: $"before-{page + 1}",
                    readCursor: $"after-{page}"),
                MailTimelinePageDirection.Forward,
                Inbox,
                MessageListArrangement.Default);
        }

        // Assert
        Assert.Equal(MessageWindow.MaximumPages, window.Pages.Count);
        Assert.Equal(MessageWindow.MaximumPages * 2, window.Messages.Count);
        Assert.True(window.HasMoreBefore);
        Assert.DoesNotContain(MailMessages.Identity(1), window.Messages.Select(message => message.Id));
    }

    /// <summary>
    /// A page that came back empty is the list having ended between two requests rather than a page to keep: mail can
    /// be expunged, so a cursor that named a row can outlive it. Keeping it would take the window's own cursors away.
    /// </summary>
    [Fact]
    public void Extended_AnEmptyPage_MarksThatEndOfTheListRatherThanBeingKept()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(1, next: "after-1", previous: "before-1", readCursor: null));

        // Act
        var extended = window.Extended(
            EmptyPage(),
            MailTimelinePageDirection.Forward,
            Inbox,
            MessageListArrangement.Default);

        // Assert
        Assert.Equal(2, extended.Messages.Count);
        Assert.False(extended.HasMoreAfter);
        Assert.True(extended.HasMoreBefore);
    }

    /// <summary>
    /// A page arriving for a list somebody has already left is an ordinary race rather than an error, so the window
    /// declines it — which is what keeps a request in flight from putting another folder's mail on the screen.
    /// </summary>
    [Fact]
    public void Extended_APageForAnotherPlaceOrArrangement_IsDeclinedRatherThanTaken()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(1, next: "after-1", previous: null, readCursor: null));

        var page = Page(3, next: "after-3", previous: "before-3", readCursor: "after-1");

        // Act
        var elsewhere = window.Extended(
            page,
            MailTimelinePageDirection.Forward,
            new MessagePlace("home", "INBOX", Role: null),
            MessageListArrangement.Default);

        var rearranged = window.Extended(
            page,
            MailTimelinePageDirection.Forward,
            Inbox,
            MessageListArrangement.Default with { UnreadOnly = true });

        // Assert
        Assert.Equal(2, elsewhere.Messages.Count);
        Assert.Equal(2, rearranged.Messages.Count);
    }

    /// <summary>
    /// The leading page keeps the request that produced it, which is what reopens the window where it was: neither
    /// cursor a page answers with reads the page it came from.
    /// </summary>
    [Fact]
    public void LeadingPage_AWindowThatHasMovedOn_KeepsTheRequestThatReopensIt()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            Page(1, next: "after-1", previous: null, readCursor: null));

        // Act
        var extended = window.Extended(
            Page(3, next: null, previous: "before-3", readCursor: "after-1"),
            MailTimelinePageDirection.Forward,
            Inbox,
            MessageListArrangement.Default);

        // Assert
        Assert.Null(extended.LeadingPage?.ReadCursor);
        Assert.Equal(MailTimelinePageDirection.Forward, extended.LeadingPage?.ReadDirection);
    }

    /// <summary>A window built over nothing would be one that could not say where its pages came from.</summary>
    [Fact]
    public void Opening_AMissingPlaceOrArrangement_IsRefused()
    {
        // Arrange
        var page = Page(1, next: null, previous: null, readCursor: null);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            MessageWindow.Opening(null!, MessageListArrangement.Default, page));
        Assert.Throws<ArgumentNullException>(() => MessageWindow.Opening(Inbox, null!, page));
        Assert.Throws<ArgumentNullException>(() =>
            MessageWindow.Opening(Inbox, MessageListArrangement.Default, null!));
        Assert.Throws<ArgumentNullException>(() => MessageWindow.Nothing(null!, MessageListArrangement.Default));
        Assert.Throws<ArgumentNullException>(() => MessageWindow.Nothing(Inbox, null!));
    }

    private static MessagePage Page(int first, string? next, string? previous, string? readCursor) =>
        new(
            [MailMessages.Message(first), MailMessages.Message(first + 1)],
            next,
            previous,
            readCursor,
            MailTimelinePageDirection.Forward);

    private static MessagePage EmptyPage() =>
        new([], NextCursor: null, PreviousCursor: null, ReadCursor: "after-1", MailTimelinePageDirection.Forward);
}
