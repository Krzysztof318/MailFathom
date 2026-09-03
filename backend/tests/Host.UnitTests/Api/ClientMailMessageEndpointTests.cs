// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the message route asks the read for, and what it puts on the wire around a body it never sends.</summary>
/// <remarks>
/// The read itself is covered where it happens. What is asserted here is the transport: that a pane's read asks for a
/// description and for no representation and no capability at all, that the file list carries the positions its own
/// download route is asked with, that the sender verdict travels as the two states a screen draws it from, and that the
/// body block says what the sender wrote rather than what a request would return.
/// </remarks>
public sealed class ClientMailMessageEndpointTests
{
    private static readonly Guid Message = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Conversation = new("33333333-3333-3333-3333-333333333333");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailMessageRoute_IsThePathAClientComposes() =>
        Assert.Equal("/messages/{storedEmailId:guid}", ClientMailMessageEndpoint.MailMessageRoute);

    /// <summary>
    /// The pane asks for a description of the message and for nothing produced from its body, and it mints no capability:
    /// the words and the tree are the body route's, and a link nobody asked for is a bearer credential handed out.
    /// </summary>
    [Fact]
    public void RequestFor_AnyRead_AsksForNoRepresentationAndNoAttachmentLink()
    {
        // Act
        var request = ClientMailMessageEndpoint.RequestFor(Message);

        // Assert
        Assert.False(request.IncludeMailDocument);
        Assert.False(request.IncludeSanitizedHtml);
        Assert.False(request.IncludeAttachmentDownloadLinks);
        Assert.False(request.RetainRemoteImageReferences);
        Assert.Equal([StoredEmailId.Create(Message)], request.StoredEmailIds);
    }

    /// <summary>The header block a pane draws is what the message displayed, including what a list row deliberately narrows away.</summary>
    [Fact]
    public void For_AReadMessage_CarriesTheHeadersTheMessageDisplayed()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.Equal(Message, response.StoredEmailId);
        Assert.Equal("primary", response.Account);
        Assert.Equal("INBOX", response.Folder);
        Assert.Equal(Conversation, response.ThreadId);
        Assert.Equal("Quarterly invoice", response.Headers.Subject);
        Assert.Equal("abc@example.test", response.Headers.MessageId);
        Assert.Collection(
            response.Headers.Participants,
            participant =>
            {
                Assert.Equal("From", participant.Role);
                Assert.Equal("billing@example.test", participant.Address);
                Assert.Equal("Billing", participant.DisplayName);
            },
            participant => Assert.Equal("To", participant.Role));
    }

    /// <summary>
    /// A file is described and never carried, and the position it is described at is the one the download route is asked
    /// with — a list that renumbered or reordered would hand a reader the wrong file rather than fail.
    /// </summary>
    [Fact]
    public void For_AMessageCarryingFiles_DescribesEachAtThePositionItsDownloadIsAskedWith()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.Collection(
            response.Attachments,
            first =>
            {
                Assert.Equal(0, first.Position);
                Assert.Equal("invoice.pdf", first.FileName);
                Assert.Equal("application/pdf", first.MediaType);
                Assert.Equal(2048, first.SizeOctets);
                Assert.False(first.WasFileNameNormalized);
            },
            second =>
            {
                Assert.Equal(1, second.Position);
                Assert.Equal("photo.jpg", second.FileName);
            });
    }

    /// <summary>The counts say what the message carries besides its files, which is what keeps a signed message from drawing a paperclip.</summary>
    [Fact]
    public void For_AMessageWhosePartsWereRead_CarriesTheCountsBesideTheDescriptions()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.NotNull(response.Carried);
        Assert.Equal(2, response.Carried.AttachmentCount);
        Assert.Equal(3, response.Carried.InlineResourceCount);
        Assert.False(response.Carried.Encrypted);
    }

    /// <summary>
    /// A message whose content the size limit kept out of storage has had its parts read by nothing, so the counts are
    /// absent rather than zero: zero would tell a reader the message carries no files, which nothing here established.
    /// </summary>
    [Fact]
    public void For_AMessageNothingHasParsed_CarriesNoCountsAtAll()
    {
        // Arrange
        var message = MessageWith() with
        {
            AttachmentSummary = null,
            Attachments = [],
            Body = EmailContentBody.NotStoredExceededSizeLimit,
        };

        // Act
        var response = ClientMailMessageResponse.For(message);

        // Assert
        Assert.Null(response.Carried);
        Assert.Empty(response.Attachments);
        Assert.Equal("NotStoredExceededSizeLimit", response.Body.Availability);
        Assert.False(response.Body.PlainText);
        Assert.False(response.Body.Html);
    }

    /// <summary>
    /// The body block says which forms the sender wrote rather than which the surface would return, because the words
    /// come back for every readable message and therefore say nothing about what arrived.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void For_AReadableBody_SaysWhichFormsTheSenderWrote(bool plainText, bool html)
    {
        // Arrange
        var message = MessageWith() with
        {
            Body = EmailContentBody.Readable(
                new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
                sanitizedHtml: null,
                document: null,
                selfContainedHtml: null,
                new EmailBodyForms(plainText, html)),
        };

        // Act
        var response = ClientMailMessageResponse.For(message);

        // Assert
        Assert.Equal("Readable", response.Body.Availability);
        Assert.Equal(plainText, response.Body.PlainText);
        Assert.Equal(html, response.Body.Html);
    }

    /// <summary>
    /// The verdict travels as the two states it was stored as. A screen draws a badge from both, because an authenticated
    /// author nobody has named carries the same trust value as one whose authentication failed.
    /// </summary>
    [Fact]
    public void For_AReadMessage_CarriesTheSenderVerdictAsTwoStates()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.Equal("Authenticated", response.Sender.AuthorAuthentication);
        Assert.Equal("Trusted", response.Sender.DeploymentTrust);
    }

    /// <summary>
    /// The domain that authenticated travels beside the verdict, so a reading pane can name who actually sent a message
    /// rather than repeating the <c>From</c> value it displays — which is the one thing an impersonation gets wrong.
    /// </summary>
    [Fact]
    public void For_AMessageWhoseAuthorAuthenticated_NamesTheDomainThatDidSo()
    {
        // Arrange
        var evidence = new SenderAuthenticationEvidence
        {
            AuthenticatedDomain = SenderDomain.TryCreate("mail.example.test", out var domain) ? domain : default,
            AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            Dmarc = DmarcOutcome.Pass,
            Source = SenderAuthenticationSource.ReceivingServer,
        };

        // Act
        var response = ClientMailMessageResponse.For(MessageWith(evidence));

        // Assert
        Assert.Equal("mail.example.test", response.Sender.AuthenticatedDomain);
    }

    /// <summary>A message nothing authenticated names no domain, which is an ordinary outcome rather than missing data.</summary>
    [Fact]
    public void For_AMessageNothingAuthenticated_NamesNoDomain()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.Null(response.Sender.AuthenticatedDomain);
    }

    /// <summary>The flags a pane draws are the ones the mail server last showed, and unread is the absence of the seen flag.</summary>
    [Fact]
    public void For_AReadMessage_CarriesTheFlagsTheServerLastShowed()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith());

        // Assert
        Assert.True(response.Unread);
        Assert.False(response.Flagged);
        Assert.False(response.Answered);
    }

    private static ReadEmailContent MessageWith(SenderAuthenticationEvidence? evidence = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        AccountId = MailAccountId.Create("primary"),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        SizeOctets = 40_960,
        Headers = new EmailContentHeaders(
            "Quarterly invoice",
            SentAt: null,
            ReceivedAt: null,
            [
                Participant(EmailAddressRole.From, "Billing", "billing@example.test"),
                Participant(EmailAddressRole.To, displayName: null, "reader@example.test"),
            ],
            EmailThreadReferences.Create("abc@example.test", inReplyTo: null, references: null)),
        Body = EmailContentBody.Readable(
            new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
            sanitizedHtml: null,
            document: null,
            selfContainedHtml: null,
            new EmailBodyForms(PlainText: true, Html: true)),
        AttachmentSummary = new StoredEmailAttachmentSummary(
            AttachmentCount: 2,
            TotalSizeOctets: 2_148,
            InlineResourceCount: 3,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        Attachments =
        [
            Attachment("invoice.pdf", "application/pdf", 2_048),
            Attachment("photo.jpg", "image/jpeg", 100),
        ],
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Trusted,
        },
        SenderAuthenticationEvidence = evidence ?? SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Thread = new ReadEmailThread
        {
            ThreadId = EmailThreadId.Create(Conversation),
            EmailCount = 1,
            MoreEmailsNotNamed = false,
            OtherEmails = [],
        },
    };

    private static EmailParticipant Participant(EmailAddressRole role, string? displayName, string address) =>
        new(role, EmailAddress.TryCreate(displayName, address, out var parsed) ? parsed : default);

    private static ReadEmailAttachment Attachment(string fileName, string mediaType, long sizeOctets) => new(
        new ExtractedEmailAttachment(
            AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            mediaType,
            sizeOctets),
        AttachmentDownload.NotRequested);
}
