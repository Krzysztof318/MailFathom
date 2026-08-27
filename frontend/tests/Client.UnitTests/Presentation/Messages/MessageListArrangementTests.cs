// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>How the list is arranged, and the request one page of it is asked for with.</summary>
public sealed class MessageListArrangementTests
{
    /// <summary>A list nobody has arranged reads newest first and keeps everything, which is what a mail client does.</summary>
    [Fact]
    public void Default_AListNobodyHasArranged_ReadsNewestFirstAndKeepsEverything()
    {
        // Act
        var arrangement = MessageListArrangement.Default;

        // Assert
        Assert.Equal(MailTimelineOrder.NewestFirst, arrangement.Order);
        Assert.False(arrangement.OldestFirst);
        Assert.False(arrangement.KeepsLessThanEverything);
    }

    /// <summary>Each of the four filters narrows the list, so each of them says so on the control that did it.</summary>
    [Fact]
    public void KeepsLessThanEverything_AnyFilterInForce_SaysSo()
    {
        // Act, Assert
        Assert.True((MessageListArrangement.Default with { UnreadOnly = true }).KeepsLessThanEverything);
        Assert.True((MessageListArrangement.Default with { FlaggedOnly = true }).KeepsLessThanEverything);
        Assert.True((MessageListArrangement.Default with { WithAttachmentsOnly = true }).KeepsLessThanEverything);
        Assert.True((MessageListArrangement.Default with { IncludeJunk = true }).KeepsLessThanEverything);
    }

    /// <summary>Reading from the other end is the order rather than a second reading, so both halves say the same thing.</summary>
    [Fact]
    public void OldestFirst_AnOrderReadFromTheOtherEnd_IsStatedAsItsOwnAffirmative()
    {
        // Act, Assert
        Assert.True((MessageListArrangement.Default with { Order = MailTimelineOrder.OldestFirst }).OldestFirst);
        Assert.False(MessageListArrangement.Default.OldestFirst);
    }

    /// <summary>An account and a folder reach the request as themselves, beside where the page continues from.</summary>
    [Fact]
    public void QueryFor_APlaceNamingAnAccountAndAFolder_AsksForThatFolder()
    {
        // Arrange
        var place = new MessagePlace("work", "INBOX", Role: null);

        // Act
        var query = MessageListArrangement.Default.QueryFor(
            place,
            "the-cursor",
            MailTimelinePageDirection.Backward,
            50);

        // Assert
        Assert.Equal("work", query.Account);
        Assert.Equal("INBOX", query.Folder);
        Assert.Equal("the-cursor", query.Cursor);
        Assert.Equal(MailTimelinePageDirection.Backward, query.Direction);
        Assert.Equal(50, query.PageSize);
    }

    /// <summary>A role taken across mailboxes is written as a folder reference, which is what it is on this surface.</summary>
    [Fact]
    public void QueryFor_APlaceNamingARole_AsksForItAsAFolderReference()
    {
        // Arrange
        var place = new MessagePlace(Account: null, Folder: null, "Sent");

        // Act
        var query = MessageListArrangement.Default.QueryFor(place, cursor: null, MailTimelinePageDirection.Forward, 50);

        // Assert
        Assert.Equal("role:Sent", query.Folder);
        Assert.Null(query.Account);
    }

    /// <summary>
    /// Junk takes part in a folder somebody opened on purpose and stays out of a list nobody narrowed, which is what
    /// keeps a junk folder from being served as an empty one.
    /// </summary>
    [Fact]
    public void QueryFor_APlaceSomebodyChose_LetsItsJunkTakePartWithoutBeingAsked()
    {
        // Act
        var chosen = MessageListArrangement.Default.QueryFor(
            new MessagePlace("work", "JUNK", Role: null),
            cursor: null,
            MailTimelinePageDirection.Forward,
            50);

        var everything = MessageListArrangement.Default.QueryFor(
            MessagePlace.Everything,
            cursor: null,
            MailTimelinePageDirection.Forward,
            50);

        var asked = (MessageListArrangement.Default with { IncludeJunk = true }).QueryFor(
            MessagePlace.Everything,
            cursor: null,
            MailTimelinePageDirection.Forward,
            50);

        // Assert
        Assert.True(chosen.IncludeJunk);
        Assert.False(everything.IncludeJunk);
        Assert.True(asked.IncludeJunk);
    }

    /// <summary>A filter keeping everything is unstated, so a request says what somebody narrowed and nothing else.</summary>
    [Fact]
    public void QueryFor_AFilterKeepingEverything_LeavesTheParameterUnstated()
    {
        // Act
        var kept = new MessageListArrangement
        {
            UnreadOnly = true,
            FlaggedOnly = true,
            WithAttachmentsOnly = true,
        }.QueryFor(MessagePlace.Everything, cursor: null, MailTimelinePageDirection.Forward, 50);

        var everything = MessageListArrangement.Default.QueryFor(
            MessagePlace.Everything,
            cursor: null,
            MailTimelinePageDirection.Forward,
            50);

        // Assert
        Assert.True(kept.Unread);
        Assert.True(kept.Flagged);
        Assert.True(kept.HasAttachments);
        Assert.Null(everything.Unread);
        Assert.Null(everything.Flagged);
        Assert.Null(everything.HasAttachments);
    }

    /// <summary>A request composed from nowhere would be one asking a deployment about no place at all.</summary>
    [Fact]
    public void QueryFor_NoPlace_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            MessageListArrangement.Default.QueryFor(null!, cursor: null, MailTimelinePageDirection.Forward, 50));
    }
}
