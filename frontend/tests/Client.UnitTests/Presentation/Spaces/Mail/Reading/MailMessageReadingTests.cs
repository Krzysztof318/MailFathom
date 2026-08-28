// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail.Reading;

/// <summary>How the message contract becomes the honest, quiet reading a pane draws.</summary>
public sealed class MailMessageReadingTests
{
    [Fact]
    public void Of_AnAuthenticatedAuthorNobodyNamed_LeavesTheOrdinaryCaseQuiet()
    {
        // Arrange
        var message = Message(authorAuthentication: "Authenticated", deploymentTrust: "Unknown");

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        Assert.False(reading.ShowsSenderNotice);
        Assert.False(reading.WarnsAboutSender);
    }

    [Fact]
    public void Of_AnAuthorWhoseAuthenticationWasNotEstablished_LeavesTheOrdinaryCaseQuiet()
    {
        // Arrange
        var message = Message(authorAuthentication: "NotEstablished", deploymentTrust: "Unknown");

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        Assert.False(reading.ShowsSenderNotice);
        Assert.False(reading.WarnsAboutSender);
    }

    [Fact]
    public void Of_AnAuthenticatedAuthorTheDeploymentTrusts_NamesTheAuthenticatedDomain()
    {
        // Arrange
        var message = Message(authorAuthentication: "Authenticated", deploymentTrust: "Trusted");

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        Assert.True(reading.ShowsSenderNotice);
        Assert.False(reading.WarnsAboutSender);
        Assert.Equal("Trusted example.test", reading.SenderNotice);
    }

    [Fact]
    public void Of_ADisplayedAuthorWhoseAuthenticationFailed_WarnsWithoutCallingTheClaimAuthenticated()
    {
        // Arrange
        var message = Message(authorAuthentication: "Failed", deploymentTrust: "Unknown");

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        Assert.True(reading.ShowsSenderNotice);
        Assert.True(reading.WarnsAboutSender);
        Assert.Equal("Failed release@example.test", reading.SenderNotice);
    }

    [Fact]
    public void Of_AVerdictThisBuildDoesNotKnow_WarnsRatherThanReadingItAsOrdinary()
    {
        // Arrange
        var message = Message(authorAuthentication: "FutureOutcome", deploymentTrust: "Unknown");

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        Assert.True(reading.WarnsAboutSender);
        Assert.Equal("Unrecognized", reading.SenderNotice);
    }

    [Fact]
    public void Of_AnAttachment_StatesItsSafeNameTypeAndSizeBeforeItIsFetched()
    {
        // Arrange
        var message = Message(
            authorAuthentication: "Authenticated",
            deploymentTrust: "Unknown",
            attachments:
            [
                new DeploymentMailAttachment(
                    Position: 2,
                    FileName: "release-notes.pdf",
                    WasFileNameNormalized: true,
                    MediaType: "application/pdf",
                    SizeOctets: 2_401_337),
            ]);

        // Act
        var reading = MailMessageReading.Of(message, NoDownloads, Words());

        // Assert
        var attachment = Assert.Single(reading.Attachments);
        Assert.Equal("release-notes.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal("2.3 MB", attachment.Size);
        Assert.True(attachment.WasFileNameNormalized);
        Assert.True(attachment.CanDownload);
        Assert.False(attachment.CanCancel);
    }

    private static DeploymentMailMessageDetail Message(
        string authorAuthentication,
        string deploymentTrust,
        IReadOnlyList<DeploymentMailAttachment>? attachments = null) => new(
        StoredEmailId: Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
        Account: "personal",
        Folder: "INBOX",
        ThreadId: Guid.Parse("5de1bfc2-e8ca-4388-8e7f-1f304b07d671"),
        SizeOctets: 2_416_381,
        Headers: new DeploymentMailHeaders(
            Subject: "Release 0.8.0",
            SentAt: DateTimeOffset.Parse("2026-08-27T09:14:00+00:00", CultureInfo.InvariantCulture),
            ReceivedAt: DateTimeOffset.Parse("2026-08-27T09:14:06+00:00", CultureInfo.InvariantCulture),
            Participants:
            [
                new DeploymentMailParticipant("From", "release@example.test", "Release notices"),
                new DeploymentMailParticipant("To", "reader@example.test", null),
            ],
            MessageId: "release-0-8-0@example.test",
            InReplyTo: null,
            References: []),
        Body: new DeploymentMailBodyForms("Readable", PlainText: true, Html: true),
        Sender: new DeploymentMailSenderVerdict(authorAuthentication, deploymentTrust),
        Attachments: attachments ?? [],
        Carried: null,
        Unread: true,
        Flagged: false,
        Answered: false);

    private static IReadOnlyDictionary<int, MailAttachmentStanding> NoDownloads { get; } =
        new Dictionary<int, MailAttachmentStanding>();

    private static StubStringLocalizer Words() => new(
        new Dictionary<string, string>
        {
            [MailMessageWords.TrustedSenderKey] = "Trusted {0}",
            [MailMessageWords.FailedSenderKey] = "Failed {0}",
            [MailMessageWords.UnrecognizedSenderKey] = "Unrecognized",
            [MailMessageWords.AttachmentFallbackKey] = "Attachment {0}",
            [MailMessageWords.NormalizedFileNameKey] = "The sender's file name was made safe.",
            [MailMessageWords.HeaderRoleKey("From")] = "From",
            [MailMessageWords.HeaderRoleKey("To")] = "To",
        });
}
