// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>What a loaded window reads as on the screen, which is the whole of what a row says.</summary>
public sealed class MessageListShapeTests
{
    private static readonly MessagePlace Inbox = new("work", "INBOX", Role: null);

    private static readonly MessagePlace Sent = new(Account: null, Folder: null, "Sent");

    private static readonly DateTimeOffset Now = new(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Local));

    /// <summary>A row is drawn under the name the sender wrote, which is what a reader recognizes them by.</summary>
    [Fact]
    public void Of_ASenderWhoWroteTheirName_DrawsTheNameRatherThanTheAddress()
    {
        // Act
        var row = Single(MailMessages.Message(1, senderDisplayName: "Someone", senderAddress: "someone@example.test"));

        // Assert
        Assert.Equal("Someone", row.Correspondent);
    }

    /// <summary>A header carrying no name falls back to the address, which is still something a reader recognizes.</summary>
    [Fact]
    public void Of_ASenderWhoWroteNoName_DrawsTheAddress()
    {
        // Act
        var row = Single(MailMessages.Message(1, senderDisplayName: " ", senderAddress: "someone@example.test"));

        // Assert
        Assert.Equal("someone@example.test", row.Correspondent);
    }

    /// <summary>A message nothing names either end of is drawn under a sentence, because a blank column reads as a row that failed.</summary>
    [Fact]
    public void Of_AMessageNamingNobodyAtAll_DrawsTheSentenceStandingInForOne()
    {
        // Act
        var row = Single(MailMessages.Message(
            1,
            senderDisplayName: null,
            senderAddress: null,
            recipients: []));

        // Assert
        Assert.Equal("Unknown sender", row.Correspondent);
    }

    /// <summary>Mail this owner sent is drawn by who it went to, because every row of it came from the same person.</summary>
    [Fact]
    public void Of_APlaceHoldingMailThisOwnerSent_DrawsTheRecipient()
    {
        // Act
        var row = Single(
            MailMessages.Message(1, recipients: ["someone@example.test"]),
            Sent);

        // Assert
        Assert.Equal("someone@example.test", row.Correspondent);
    }

    /// <summary>Mail that went to several people is drawn by the first of them beside how many others there were.</summary>
    [Fact]
    public void Of_AMessageThatWentToSeveralPeople_DrawsTheFirstBesideHowManyOthers()
    {
        // Act
        var row = Single(
            MailMessages.Message(1, recipients: ["first@example.test", "second@example.test", "third@example.test"]),
            Sent);

        // Assert
        Assert.Equal("first@example.test and 2 more", row.Correspondent);
    }

    /// <summary>A message nobody gave a subject is drawn under a sentence rather than under an empty line.</summary>
    [Fact]
    public void Of_AMessageCarryingNoSubject_DrawsTheSentenceStandingInForOne()
    {
        // Act
        var row = Single(MailMessages.Message(1, subject: "  "));

        // Assert
        Assert.Equal("No subject", row.Subject);
    }

    /// <summary>Mail that arrived today is dated by its time, which is what tells two of today's messages apart.</summary>
    [Fact]
    public void Of_AMessageThatArrivedToday_IsDatedByItsTime()
    {
        // Arrange
        var arrived = new DateTimeOffset(new DateTime(2026, 8, 25, 9, 41, 0, DateTimeKind.Local));

        // Act
        var row = Single(MailMessages.Message(1, receivedAt: arrived));

        // Assert
        Assert.Equal(arrived.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), row.Received);
    }

    /// <summary>Mail from earlier this year is dated by its day and month, and older mail by its whole date.</summary>
    [Fact]
    public void Of_AMessageOlderThanToday_IsDatedByItsDayAndByItsYearOnceThatChanges()
    {
        // Arrange
        var thisYear = new DateTimeOffset(new DateTime(2026, 3, 2, 9, 41, 0, DateTimeKind.Local));
        var yearsBack = new DateTimeOffset(new DateTime(2024, 11, 18, 9, 41, 0, DateTimeKind.Local));

        // Act
        var earlier = Single(MailMessages.Message(1, receivedAt: thisYear));
        var older = Single(MailMessages.Message(1, receivedAt: yearsBack));

        // Assert
        Assert.Equal(thisYear.ToLocalTime().ToString("m", CultureInfo.CurrentCulture), earlier.Received);
        Assert.Equal(yearsBack.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), older.Received);
    }

    /// <summary>A message no header dated is drawn under a sentence, since a row has to say something in that column.</summary>
    [Fact]
    public void Of_AMessageNoHeaderDated_DrawsTheSentenceStandingInForADate()
    {
        // Act
        var row = Single(MailMessages.Message(1, receivedAt: null));

        // Assert
        Assert.Equal("No date", row.Received);
    }

    /// <summary>
    /// The row states itself once for a screen reader, which is what a list of fifty rows of six unlabelled controls
    /// would otherwise be read out as. What is true of the message is appended; what is not is absent.
    /// </summary>
    [Fact]
    public void Of_ARowWithMarksOnIt_AnnouncesItselfAsOneSentenceCarryingThem()
    {
        // Arrange
        var arrived = new DateTimeOffset(new DateTime(2026, 8, 25, 9, 41, 0, DateTimeKind.Local));

        // Act
        var marked = Single(MailMessages.Message(
            1,
            receivedAt: arrived,
            subject: "Quarterly review",
            senderDisplayName: "Someone",
            unread: true,
            flagged: true,
            answered: true,
            attachmentCount: 2));

        var plain = Single(MailMessages.Message(
            1,
            receivedAt: arrived,
            subject: "Quarterly review",
            senderDisplayName: "Someone"));

        // Assert
        var dated = arrived.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        Assert.Equal(
            $"Someone, Quarterly review, {dated} unread flagged answered attachment",
            marked.Announcement);
        Assert.Equal($"Someone, Quarterly review, {dated}", plain.Announcement);
    }

    /// <summary>Every mark the deployment reported reaches the row, because the view draws each of them separately.</summary>
    [Fact]
    public void Of_AMessageTheDeploymentMarked_CarriesEachMarkOntoTheRow()
    {
        // Act
        var row = Single(MailMessages.Message(
            1,
            unread: true,
            flagged: true,
            answered: true,
            attachmentCount: 3,
            preview: "The numbers for the quarter are"));

        // Assert
        Assert.Equal(MailMessages.Key(1), row.Key);
        Assert.True(row.IsUnread);
        Assert.True(row.IsFlagged);
        Assert.True(row.IsAnswered);
        Assert.True(row.HasAttachments);
        Assert.True(row.ShowsAttachmentCount);
        Assert.Equal(3.ToString("N0", CultureInfo.CurrentCulture), row.AttachmentCountText);
        Assert.True(row.HasPreview);
        Assert.Equal("The numbers for the quarter are", row.Preview);
    }

    /// <summary>
    /// A message this deployment holds but has not extracted the text of yet has no preview, which is a folder still
    /// being taken in rather than a message with nothing in it — so the row draws one line less rather than an empty one.
    /// </summary>
    [Fact]
    public void Of_AMessageNothingHasExtractedYet_HasNoPreviewToDraw()
    {
        // Act
        var row = Single(MailMessages.Message(1, preview: null, attachmentCount: 1));

        // Assert
        Assert.Equal(string.Empty, row.Preview);
        Assert.False(row.HasPreview);
        Assert.True(row.HasAttachments);
        Assert.False(row.ShowsAttachmentCount);
    }

    /// <summary>The rows are drawn in the order the window holds them, which is the order the deployment sorted them in.</summary>
    [Fact]
    public void Of_AWindowOfSeveralPages_DrawsEveryRowInTheOrderItIsRead()
    {
        // Arrange
        var window = MessageWindow.Opening(
            Inbox,
            MessageListArrangement.Default,
            new MessagePage(
                [MailMessages.Message(1), MailMessages.Message(2), MailMessages.Message(3)],
                NextCursor: null,
                PreviousCursor: null,
                ReadCursor: null,
                MailTimelinePageDirection.Forward));

        // Act
        var rows = MessageListShape.Of(window, Now, Words());

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2), MailMessages.Key(3)], rows.Select(row => row.Key));
    }

    /// <summary>A shape drawn from nothing would be a list drawn from no window and no words.</summary>
    [Fact]
    public void Of_NoWindowOrNoWords_IsRefused()
    {
        // Arrange
        var window = MessageWindow.Nothing(Inbox, MessageListArrangement.Default);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MessageListShape.Of(null!, Now, Words()));
        Assert.Throws<ArgumentNullException>(() => MessageListShape.Of(window, Now, null!));
    }

    private static MessageRow Single(DeploymentMailMessage message, MessagePlace? place = null)
    {
        var window = MessageWindow.Opening(
            place ?? Inbox,
            MessageListArrangement.Default,
            new MessagePage(
                [message],
                NextCursor: null,
                PreviousCursor: null,
                ReadCursor: null,
                MailTimelinePageDirection.Forward));

        return MessageListShape.Of(window, Now, Words()).Single();
    }

    private static StubStringLocalizer Words() => new(
        new Dictionary<string, string>(StringComparer.Ordinal)
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
}
