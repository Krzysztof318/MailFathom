// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Globalization;
using MailFathom.Client.Backend.Threads;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Threads;

/// <summary>What a conversation reads as: its header, its messages, and which of them a reading opens at.</summary>
public sealed class ThreadShapeTests
{
    private static readonly DateTimeOffset Written = new(2026, 3, 14, 9, 15, 0, TimeSpan.Zero);

    /// <summary>The header is answered about the whole conversation rather than about the messages in hand.</summary>
    [Fact]
    public void Header_AConversationLargerThanItsPage_SaysWhatTheDeploymentSaidAboutTheWholeOfIt()
    {
        // Arrange
        var window = Opened(
            MailThreads.Page(
                [MailThreads.Message(1)],
                [new DeploymentThreadParticipant("first@example.test", "First", 4),
                 new DeploymentThreadParticipant("second@example.test", null, 2)],
                messageCount: 6,
                moreMessagesNotAssembled: true,
                moreParticipantsNotNamed: true));

        // Act
        var header = ThreadShape.Header(window, Words());

        // Assert
        Assert.True(header.IsOpen);
        Assert.False(header.IsClosed);
        Assert.Equal("Quarterly review", header.Subject);
        Assert.Equal("6 messages", header.MessageCount);
        Assert.Equal("Conversation Quarterly review. 6 messages", header.Announcement);
        Assert.True(header.HasUnnamedParticipants);
        Assert.True(header.RunsPastAssembly);

        Assert.Equal(["First", "second@example.test"], header.Participants.Select(person => person.Author));
        Assert.Equal(["(4)", "(2)"], header.Participants.Select(person => person.MessageCount));
        Assert.Equal("First wrote 4", header.Participants[0].Announcement);
    }

    /// <summary>
    /// What the conversation is about comes from the message that began it, so paging forward does not rewrite the
    /// header somebody is reading under.
    /// </summary>
    [Fact]
    public void Header_AReplyThatRewroteTheSubject_TakesTheSubjectOfTheMessageThatBeganIt()
    {
        // Arrange
        var window = Opened(MailThreads.Page(
        [
            MailThreads.Message(1, subject: "Quarterly review"),
            MailThreads.Message(2, subject: "Re: something else entirely"),
        ]));

        // Act
        var header = ThreadShape.Header(window, Words());

        // Assert
        Assert.Equal("Quarterly review", header.Subject);
    }

    /// <summary>An author the answer named with neither a name nor an address names nobody rather than a blank.</summary>
    [Fact]
    public void Header_AnAuthorNamedByNeitherANameNorAnAddress_IsLeftOutOfTheHeader()
    {
        // Arrange
        var window = Opened(MailThreads.Page(
            [MailThreads.Message(1)],
            [new DeploymentThreadParticipant(null, null, 1),
             new DeploymentThreadParticipant("first@example.test", "First", 1)]));

        // Act
        var header = ThreadShape.Header(window, Words());

        // Assert
        Assert.Equal(["First"], header.Participants.Select(person => person.Author));
    }

    /// <summary>A screen nothing has been opened in draws the empty header rather than one about nothing.</summary>
    [Fact]
    public void Header_NoConversationOpened_IsTheEmptyOne()
    {
        // Act
        var header = ThreadShape.Header(ThreadWindow.Nothing, Words());

        // Assert
        Assert.False(header.IsOpen);
        Assert.True(header.IsClosed);
        Assert.Empty(header.Participants);
    }

    /// <summary>
    /// A conversation opens showing the newest message, which is what somebody catching up on one came for, with the
    /// rest collapsed to a line each.
    /// </summary>
    [Fact]
    public void Messages_AConversationNobodyArrivedAtAMessageIn_OpensTheNewestAndCollapsesTheRest()
    {
        // Arrange
        var window = Opened(MailThreads.Page(
            [MailThreads.Message(1), MailThreads.Message(2), MailThreads.Message(3)]));

        // Act
        var messages = ThreadShape.Messages(window, Nothing, Words());

        // Assert
        Assert.Equal([false, false, true], messages.Select(message => message.IsExpanded));
        Assert.Equal([false, false, true], messages.Select(message => message.IsOpenedAt));
    }

    /// <summary>
    /// Arriving at a message is how a search result and a citation reach mail, so the message named is the one opened
    /// whether or not it is the newest.
    /// </summary>
    [Fact]
    public void Messages_AConversationArrivedAtAMessageIn_OpensThatMessageRatherThanTheNewest()
    {
        // Arrange
        var window = ThreadWindow.Opening(
            new ThreadOpening(MailThreads.Identity, MailMessages.Identity(1)),
            MailThreads.Page([MailThreads.Message(1), MailThreads.Message(2)]));

        // Act
        var messages = ThreadShape.Messages(window, Nothing, Words());

        // Assert
        Assert.Equal([true, false], messages.Select(message => message.IsExpanded));
        Assert.Equal([true, false], messages.Select(message => message.IsOpenedAt));
    }

    /// <summary>
    /// A message the conversation no longer shows names nothing, which leaves the newest opened rather than the
    /// conversation opened at nothing.
    /// </summary>
    [Fact]
    public void Messages_AnArrivalAtAMessageTheConversationNoLongerShows_FallsBackToTheNewest()
    {
        // Arrange
        var window = ThreadWindow.Opening(
            new ThreadOpening(MailThreads.Identity, MailMessages.Identity(9)),
            MailThreads.Page([MailThreads.Message(1), MailThreads.Message(2)]));

        // Act
        var messages = ThreadShape.Messages(window, Nothing, Words());

        // Assert
        Assert.Equal([false, true], messages.Select(message => message.IsExpanded));
    }

    /// <summary>The reader's own act wins over the message a conversation opened at.</summary>
    [Fact]
    public void Messages_TheOpenedMessageCollapsedByTheReader_IsDrawnCollapsed()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1), MailThreads.Message(2)]));

        var closed = Nothing.SetItem(MailMessages.Key(2), ThreadMessageDetail.Collapsed);

        // Act
        var messages = ThreadShape.Messages(window, closed, Words());

        // Assert
        Assert.Equal([false, false], messages.Select(message => message.IsExpanded));
        Assert.True(messages[1].IsOpenedAt);
    }

    /// <summary>Everything a message draws comes out of the conversation, so drawing thirty of them costs one request.</summary>
    [Fact]
    public void Messages_AMessageTheDeploymentDescribed_DrawsItFromTheConversationAlone()
    {
        // Arrange
        var window = Opened(MailThreads.Page(
        [
            MailThreads.Message(
                1,
                contribution: "Numbers are attached.",
                sentAt: Written,
                senderDisplayName: "First",
                recipients: ["owner@example.test", "second@example.test"],
                unread: true),
        ]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Equal(MailMessages.Key(1), message.Key);
        Assert.Equal("First", message.Author);
        Assert.Equal("Quarterly review", message.Subject);
        Assert.Equal("Numbers are attached.", message.Contribution);
        Assert.True(message.HasContribution);
        Assert.False(message.AwaitsContribution);
        Assert.Equal("To owner@example.test and 1 more", message.Recipients);
        Assert.True(message.HasRecipients);
        Assert.True(message.IsUnread);
        Assert.Equal(
            Written.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            message.Written);
    }

    /// <summary>
    /// A message this deployment holds but has not extracted yet has nothing to show of what it added, which is a state
    /// of a mailbox still being taken in rather than a message somebody sent empty.
    /// </summary>
    [Fact]
    public void Messages_AMessageNothingHasExtracted_SaysSoRatherThanDrawingABlank()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1, contribution: null)]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Empty(message.Contribution);
        Assert.False(message.HasContribution);
        Assert.True(message.AwaitsContribution);
    }

    /// <summary>A message no header dated is written under a sentence rather than under a blank.</summary>
    [Fact]
    public void Messages_AMessageNoHeaderDated_IsWrittenUnderASentence()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1, sentAt: null)]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Equal("No date", message.Written);
    }

    /// <summary>A message the header carried no sender for is drawn under a sentence rather than under a blank.</summary>
    [Fact]
    public void Messages_AMessageWithNoUsableSender_IsDrawnUnderASentence()
    {
        // Arrange
        var window = Opened(MailThreads.Page(
            [MailThreads.Message(1, senderDisplayName: null, senderAddress: null)]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Equal("Unknown sender", message.Author);
    }

    /// <summary>A message naming nobody it went to draws one line less rather than a label with nothing after it.</summary>
    [Fact]
    public void Messages_AMessageNamingNobodyItWentTo_DrawsNoRecipientLine()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1, recipients: [])]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Empty(message.Recipients);
        Assert.False(message.HasRecipients);
    }

    /// <summary>
    /// A message states itself once for a screen reader, through the same entries a list row does: a conversation's
    /// messages and a folder's rows stand for the same thing.
    /// </summary>
    [Fact]
    public void Messages_AMessageCarryingMarks_StatesItselfOnceWithThemAppended()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1, sentAt: Written, unread: true)]));

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, Nothing, Words()));

        // Assert
        Assert.Equal(
            $"Someone, Quarterly review, {message.Written} unread",
            message.Announcement);
    }

    /// <summary>What the reader has asked of one message travels with that message and with nothing else.</summary>
    [Fact]
    public void Messages_AMessageWhoseWholeReadFailed_SaysSoOnThatMessageAlone()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1), MailThreads.Message(2)]));

        var failed = Nothing.SetItem(
            MailMessages.Key(1),
            ThreadMessageDetail.Opened with { WholeMessageFailed = true });

        // Act
        var messages = ThreadShape.Messages(window, failed, Words());

        // Assert
        Assert.True(messages[0].ShowsWholeMessageFailure);
        Assert.False(messages[0].OffersWholeMessage);
        Assert.False(messages[1].ShowsWholeMessageFailure);
    }

    /// <summary>The whole message is offered only while a message is open and only while there is something to ask for.</summary>
    [Fact]
    public void Messages_AMessageBeingRead_OffersNothingWhileTheReadIsOnItsWay()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1)]));

        var reading = Nothing.SetItem(
            MailMessages.Key(1),
            ThreadMessageDetail.Opened with { IsReadingWholeMessage = true });

        // Act
        var message = Assert.Single(ThreadShape.Messages(window, reading, Words()));

        // Assert
        Assert.True(message.AwaitsWholeMessage);
        Assert.False(message.OffersWholeMessage);
        Assert.False(message.ShowsWholeMessage);
    }

    /// <summary>A collapsed message offers nothing of the whole of it, because the offer is made on an open one.</summary>
    [Fact]
    public void Messages_ACollapsedMessage_OffersNothingOfTheWholeOfIt()
    {
        // Arrange
        var window = Opened(MailThreads.Page([MailThreads.Message(1), MailThreads.Message(2)]));

        // Act
        var messages = ThreadShape.Messages(window, Nothing, Words());

        // Assert
        Assert.False(messages[0].OffersWholeMessage);
        Assert.True(messages[1].OffersWholeMessage);
    }

    private static IImmutableDictionary<string, ThreadMessageDetail> Nothing { get; } =
        ImmutableDictionary<string, ThreadMessageDetail>.Empty.WithComparers(StringComparer.Ordinal);

    private static ThreadWindow Opened(DeploymentMailThreadPage page) =>
        ThreadWindow.Opening(new ThreadOpening(MailThreads.Identity, AtMessage: null), page);

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
        [ThreadWords.MessageCountKey] = "{0} messages",
        [ThreadWords.AnnouncementKey] = "Conversation {0}. {1}",
        [ThreadWords.RecipientsKey] = "To {0}",
        [ThreadWords.ParticipantMessagesKey] = "({0})",
        [ThreadWords.ParticipantAnnouncementKey] = "{0} wrote {1}",
    });
}
