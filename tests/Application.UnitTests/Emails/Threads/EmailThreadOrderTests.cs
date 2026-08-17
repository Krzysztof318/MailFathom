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
            placed.Select(message => message.Email.StoredEmailId));
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
            placed.Select(message => message.Email.StoredEmailId));
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
        Assert.Equal(shown.StoredEmailId, only.Email.StoredEmailId);
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
            placed.Select(message => message.Email.StoredEmailId));
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
            placed.Select(message => message.Email.StoredEmailId));
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
            read.Select(message => message.Email.StoredEmailId),
            readAgain.Select(message => message.Email.StoredEmailId));
    }

    /// <summary>
    /// The published order must never contain the loop it exists to rule out, so the message the fallback promotes is
    /// published as a root in full: the edge that would close the cycle is dropped along with its place.
    /// </summary>
    [Fact]
    public void Of_MessagesCaughtInAReplyCycle_PublishesTheFirstAsARootAnsweringNothing()
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
            placed.Select(message => message.Email.StoredEmailId.Value).Order());
        Assert.Equal([0, 1], placed.Select(message => message.Position));

        var root = placed[0];
        Assert.Equal(looping.StoredEmailId, root.Email.StoredEmailId);
        Assert.Null(root.AnsweredStoredEmailId);
        Assert.Equal(looping.StoredEmailId, placed[1].AnsweredStoredEmailId);
    }

    /// <summary>A longer loop is unwound the same way: one root, and every other message answering the one before it.</summary>
    [Fact]
    public void Of_ThreeMessagesCaughtInAReplyCycle_PublishesOneRootAndNoMessageAnsweringItself()
    {
        // Arrange
        var first = Message(1, "2026-08-16T09:00:00Z");
        var second = Message(2, "2026-08-16T10:00:00Z", answers: first);
        var third = Message(3, "2026-08-16T11:00:00Z", answers: second);
        var looping = first with { ParentStoredEmailId = third.StoredEmailId };

        // Act
        var placed = EmailThreadOrder.Of([looping, second, third]);

        // Assert
        Assert.Equal(3, placed.Count);
        Assert.Single(placed, message => message.AnsweredStoredEmailId is null);
        Assert.All(
            placed,
            message => Assert.NotEqual(message.Email.StoredEmailId, message.AnsweredStoredEmailId));
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
            placed.Select(message => message.Email.StoredEmailId));
    }

    private static ThreadedEmailSummary Message(int ordinal, string? sentAt, ThreadedEmailSummary? answers = null) => new()
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
