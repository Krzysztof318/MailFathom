// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Threads;

public sealed class EmailThreadOrderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    [Fact]
    public void Of_SentTimestampsContradictingTheReplyRelation_KeepsTheOrderTheRelationPlaced()
    {
        // Arrange
        var opening = Message(1, "2026-08-16T12:00:00Z");
        var reply = Message(2, "2026-08-16T09:00:00Z", answers: opening);

        // Act
        var placed = EmailThreadOrder.Of([reply, opening]);

        // Assert
        Assert.Equal(
            [opening.StoredEmailId, reply.StoredEmailId],
            placed.Select(message => message.Message.StoredEmailId));
        Assert.Equal([0, 1], placed.Select(message => message.Position));
    }

    [Fact]
    public void Of_TwoRepliesToOneMessage_OrdersThemByTheSentTimestamp()
    {
        // Arrange
        var opening = Message(1, "2026-08-16T09:00:00Z");
        var later = Message(2, "2026-08-16T11:00:00Z", answers: opening);
        var earlier = Message(3, "2026-08-16T10:00:00Z", answers: opening);

        // Act
        var placed = EmailThreadOrder.Of([later, opening, earlier]);

        // Assert
        Assert.Equal(
            [opening.StoredEmailId, earlier.StoredEmailId, later.StoredEmailId],
            placed.Select(message => message.Message.StoredEmailId));
    }

    [Fact]
    public void Of_MessageWhoseParentIsNotShown_PublishesItAsARootNamingNoAncestor()
    {
        // Arrange
        var withheld = Message(1, "2026-08-16T09:00:00Z");
        var shown = Message(2, "2026-08-16T10:00:00Z", answers: withheld);

        // Act
        var placed = EmailThreadOrder.Of([shown]);

        // Assert
        var only = Assert.Single(placed);
        Assert.Equal(shown.StoredEmailId, only.Message.StoredEmailId);
        Assert.Null(only.AnsweredStoredEmailId);
        Assert.Equal(0, only.Position);
    }

    [Fact]
    public void Of_MessageWithNoSentTimestamp_SortsItAfterTheDatedMessagesBesideIt()
    {
        // Arrange
        var undated = Message(1, sentAt: null);
        var dated = Message(2, "2026-08-16T09:00:00Z");

        // Act
        var placed = EmailThreadOrder.Of([undated, dated]);

        // Assert
        Assert.Equal(
            [dated.StoredEmailId, undated.StoredEmailId],
            placed.Select(message => message.Message.StoredEmailId));
    }

    [Fact]
    public void Of_MessagesSharingASentTimestamp_SettlesTheOrderOnTheLocalIdentity()
    {
        // Arrange
        var later = Message(2, "2026-08-16T09:00:00Z");
        var earlier = Message(1, "2026-08-16T09:00:00Z");

        // Act
        var placed = EmailThreadOrder.Of([later, earlier]);

        // Assert
        Assert.Equal(
            [earlier.StoredEmailId, later.StoredEmailId],
            placed.Select(message => message.Message.StoredEmailId));
    }

    [Fact]
    public void Of_TheSameConversationReadTwiceInDifferentInputOrders_ReturnsTheSameOrder()
    {
        // Arrange
        var opening = Message(1, "2026-08-16T09:00:00Z");
        var reply = Message(2, "2026-08-16T10:00:00Z", answers: opening);
        var answerToTheReply = Message(3, "2026-08-16T11:00:00Z", answers: reply);

        // Act
        var read = EmailThreadOrder.Of([opening, reply, answerToTheReply]);
        var readAgain = EmailThreadOrder.Of([answerToTheReply, opening, reply]);

        // Assert
        Assert.Equal(
            read.Select(message => message.Message.StoredEmailId),
            readAgain.Select(message => message.Message.StoredEmailId));
    }

    [Fact]
    public void Of_MessagesCaughtInAReplyCycle_ReturnsEveryOneOfThemExactlyOnce()
    {
        // Arrange
        var one = Message(1, "2026-08-16T09:00:00Z");
        var other = Message(2, "2026-08-16T10:00:00Z", answers: one);
        var looping = one with { ParentStoredEmailId = other.StoredEmailId };

        // Act
        var placed = EmailThreadOrder.Of([looping, other]);

        // Assert
        Assert.Equal(
            [looping.StoredEmailId.Value, other.StoredEmailId.Value],
            placed.Select(message => message.Message.StoredEmailId.Value).Order());
        Assert.Equal([0, 1], placed.Select(message => message.Position));
    }

    [Fact]
    public void Of_AConversationOfSeveralBranches_WritesEachBranchOutBeforeTheNextSiblingBegins()
    {
        // Arrange
        var opening = Message(1, "2026-08-16T09:00:00Z");
        var firstBranch = Message(2, "2026-08-16T10:00:00Z", answers: opening);
        var withinFirstBranch = Message(3, "2026-08-16T12:00:00Z", answers: firstBranch);
        var secondBranch = Message(4, "2026-08-16T11:00:00Z", answers: opening);

        // Act
        var placed = EmailThreadOrder.Of([opening, secondBranch, withinFirstBranch, firstBranch]);

        // Assert
        Assert.Equal(
            [
                opening.StoredEmailId,
                firstBranch.StoredEmailId,
                withinFirstBranch.StoredEmailId,
                secondBranch.StoredEmailId,
            ],
            placed.Select(message => message.Message.StoredEmailId));
    }

    private static EmailThreadMessage Message(int ordinal, string? sentAt, EmailThreadMessage? answers = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(new Guid($"00000000-0000-0000-0000-{ordinal:D12}")),
        AccountId = Account,
        FolderAlias = Inbox,
        ParentStoredEmailId = answers?.StoredEmailId,
        Subject = $"Message {ordinal}",
        SentAt = sentAt is null ? null : DateTimeOffset.Parse(sentAt, null),
        SenderAddress = $"sender{ordinal}@example.test",
    };
}
