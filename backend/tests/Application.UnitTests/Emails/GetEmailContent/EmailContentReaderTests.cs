// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Folders;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.GetEmailContent;

/// <summary>Covers the email content use case: what it serves, what it refuses, and what it records when it refuses.</summary>
public sealed class EmailContentReaderTests
{
    /// <summary>The literal the switched-on deployment in these tests detects, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    [Fact]
    public async Task ReadContentAsync_ReadableEmail_ReturnsWhatTheRenderingProduced()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var rendering = RenderingOf(
            plainText: "Body as written",
            attachments: [AttachmentOf("report.pdf", "application/pdf", 1024)]);
        var reader = ReaderOver(summary, RendererReturning(rendering));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailability.Readable, content.Body.Availability);
        Assert.Equal("Body as written", content.Body.PlainText.Text);
        Assert.Equal(rendering.Headers, content.Headers);
        Assert.Equal(
            ["report.pdf"],
            content.Attachments?.Select(attachment => attachment.Description.FileName?.Value));
        Assert.Equal(summary.AccountId, content.AccountId);
        Assert.Equal(summary.FolderAlias, content.FolderAlias);
        Assert.Equal(summary.RemoteFlags, content.RemoteFlags);
    }

    /// <summary>The per-attachment list is re-derived, and what it describes must be what the stored row counted.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentLinksRequested_ReturnsAttachmentsConsistentWithThePersistedSummary()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 2, inlineResourceCount: 1);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments:
                [
                    AttachmentOf("first.pdf", "application/pdf", 1024),
                    AttachmentOf("second.png", "image/png", 1024),
                ],
                inlineResourceCount: 1)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var attachmentSummary = Assert.IsType<StoredEmailAttachmentSummary>(content.AttachmentSummary);
        Assert.NotNull(content.Attachments);
        Assert.Equal(attachmentSummary.AttachmentCount, content.Attachments.Count);
        Assert.Equal(
            attachmentSummary.TotalSizeOctets,
            content.Attachments.Sum(attachment => attachment.Description.DecodedSizeOctets));

        // The row counted the same message, so the derived answer and the persisted one agree here — which is the
        // consistency the content contract asks for. Where they could disagree, the derived one is what is published.
        Assert.Equal(summary.Attachments.AttachmentCount, attachmentSummary.AttachmentCount);
        Assert.Equal(summary.Attachments.InlineResourceCount, attachmentSummary.InlineResourceCount);
    }

    /// <summary>
    /// A read that wanted the message rather than its files still learns what those files are, because that is what a
    /// caller decides against when it chooses whether to fetch one. Only the capability waits for the flag.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentLinksNotRequested_DescribesTheAttachmentAndMintsNothing()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var linkIssuer = new RecordingAttachmentDownloadLinkIssuer();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments: [AttachmentOf("payslip.pdf", "application/pdf", 2048)])),
            linkIssuer: linkIssuer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("payslip.pdf", attachment.Description.FileName?.Value);
        Assert.Equal("application/pdf", attachment.Description.MediaType);
        Assert.Equal(2048, attachment.Description.DecodedSizeOctets);
        Assert.Equal(AttachmentDownloadAvailability.NotRequested, attachment.Download.Availability);
        Assert.Null(attachment.Download.Link);
        Assert.Empty(linkIssuer.Requested);

        Assert.NotNull(content.AttachmentSummary);
        Assert.Equal(1, content.AttachmentSummary.AttachmentCount);
        Assert.Equal(2048, content.AttachmentSummary.TotalSizeOctets);
    }

    /// <summary>A link is what a caller receives for a file, and it names the file it was minted for.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentLinksRequested_IssuesOneLinkPerAttachmentInTheOrderTheyWereDescribed()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 2);
        var linkIssuer = new RecordingAttachmentDownloadLinkIssuer();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments:
                [
                    AttachmentOf("first.pdf", "application/pdf", 1024),
                    AttachmentOf("second.png", "image/png", 2048),
                ])),
            linkIssuer: linkIssuer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var attachments = ContentOf(Assert.Single(result.Emails)).Attachments;
        Assert.Equal([(summary.StoredEmailId, 2)], linkIssuer.Requested);
        Assert.Equal(
            [AttachmentDownloadAvailability.Issued, AttachmentDownloadAvailability.Issued],
            attachments.Select(attachment => attachment.Download.Availability));
        Assert.Equal(
            [
                $"https://mailfathom.example.test/attachments/{summary.StoredEmailId.Value:N}-0",
                $"https://mailfathom.example.test/attachments/{summary.StoredEmailId.Value:N}-1",
            ],
            attachments.Select(attachment => attachment.Download.Link?.Address.AbsoluteUri));
        Assert.All(
            attachments,
            attachment => Assert.Equal(
                RecordingAttachmentDownloadLinkIssuer.DefaultExpiry,
                attachment.Download.Link?.ExpiresAt));
    }

    /// <summary>
    /// A deployment that mints no links still answers everything else a read asks. The attachment says so rather than
    /// arriving as one nobody asked about, because the two lead a caller to different actions: one is worth asking
    /// again for, and only an operator can change the other.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_DeploymentIssuesNoLinks_DescribesTheAttachmentAndReportsThatNoneCanBeIssued()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var linkIssuer = new RecordingAttachmentDownloadLinkIssuer(canIssueLinks: false);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments: [AttachmentOf("report.pdf", "application/pdf", 1024)])),
            linkIssuer: linkIssuer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments);
        Assert.Equal(AttachmentDownloadAvailability.Unavailable, attachment.Download.Availability);
        Assert.Null(attachment.Download.Link);
        Assert.Equal("report.pdf", attachment.Description.FileName?.Value);
        Assert.Empty(linkIssuer.Requested);
    }

    /// <summary>
    /// A link list shorter than the attachment list must not cost the descriptions. The key ring is reloadable, so an
    /// operator emptying it between the guard and the call gets fewer links back than the message has files — and an
    /// email answered with no attachments at all, beside counts saying it has some, is the one inconsistency a caller
    /// has no way to detect.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_FewerLinksThanAttachments_KeepsEveryDescriptionAndReportsTheRestUnavailable()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 3);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments:
                [
                    AttachmentOf("first.pdf", "application/pdf", 1024),
                    AttachmentOf("second.pdf", "application/pdf", 2048),
                    AttachmentOf("third.pdf", "application/pdf", 4096),
                ])),
            linkIssuer: new RecordingAttachmentDownloadLinkIssuer(issueAtMost: 1));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var attachments = ContentOf(Assert.Single(result.Emails)).Attachments;
        Assert.Equal(
            ["first.pdf", "second.pdf", "third.pdf"],
            attachments.Select(attachment => attachment.Description.FileName?.Value));
        Assert.Equal(
            [
                AttachmentDownloadAvailability.Issued,
                AttachmentDownloadAvailability.Unavailable,
                AttachmentDownloadAvailability.Unavailable,
            ],
            attachments.Select(attachment => attachment.Download.Availability));
    }

    /// <summary>Minting resolves key material, so a message with nothing to mint for must not reach the issuer at all.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailCarryingNoAttachments_MintsNothingEvenWhenLinksWereRequested()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var linkIssuer = new RecordingAttachmentDownloadLinkIssuer();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), linkIssuer: linkIssuer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(ContentOf(Assert.Single(result.Emails)).Attachments);
        Assert.Empty(linkIssuer.Requested);
    }

    /// <summary>Zero attachments is a finding a caller can act on, so it is stated under either setting.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadContentAsync_EmailCarryingNoAttachments_ReportsZeroUnderEitherSetting(
        bool includeAttachmentDownloadLinks)
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: includeAttachmentDownloadLinks),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.NotNull(content.AttachmentSummary);
        Assert.Equal(0, content.AttachmentSummary.AttachmentCount);
    }

    /// <summary>An attachment reaches the result as metadata only; the contract has nowhere to put its bytes.</summary>
    [Fact]
    public void ExtractedEmailAttachment_EveryMember_CarriesNoContent()
    {
        // Arrange
        var byteBearingTypes = new[] { typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>), typeof(Stream) };

        // Act
        var contentCarryingMembers = typeof(ExtractedEmailAttachment)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => byteBearingTypes.Contains(property.PropertyType))
            .Select(property => property.Name);

        // Assert
        Assert.Empty(contentCarryingMembers);
    }

    [Fact]
    public async Task ReadContentAsync_SanitizedHtmlRequested_AsksTheRendererForItAndReturnsIt()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var renderer = RendererReturning(RenderingOf(
            sanitizedHtml: new EmailBodyRepresentation("<p>Body</p>", 11, EmailBodyTruncation.None)));
        var reader = ReaderOver(summary, renderer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("<p>Body</p>", ContentOf(Assert.Single(result.Emails)).Body.SanitizedHtml?.Text);
        await renderer.Received(1).RenderAsync(
            Arg.Any<StoredEmailContent>(),
            Arg.Is<EmailContentRenderingBounds>(bounds => bounds != null && bounds.IncludeSanitizedHtml),
            Arg.Any<CancellationToken>());
    }

    /// <summary>One flag governs the whole call, so a caller does not receive markup for some of what it named.</summary>
    [Fact]
    public async Task ReadContentAsync_SanitizedHtmlRequestedForSeveralEmails_AsksForItOnEveryOneOfThem()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var renderer = RendererReturning(RenderingOf(
            sanitizedHtml: new EmailBodyRepresentation("<p>Body</p>", 11, EmailBodyTruncation.None)));
        var reader = ReaderOver(summaries, renderer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries), includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(result.Emails, email => Assert.NotNull(ContentOf(email).Body.SanitizedHtml));
        await renderer.Received(3).RenderAsync(
            Arg.Any<StoredEmailContent>(),
            Arg.Is<EmailContentRenderingBounds>(bounds => bounds != null && bounds.IncludeSanitizedHtml),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The same holds for the attachment flag: a batch mints links consistently or not at all.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentLinksRequestedForSeveralEmails_IssuesThemForEveryOneOfThem()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var linkIssuer = new RecordingAttachmentDownloadLinkIssuer();
        var reader = ReaderOver(
            summaries,
            RendererReturning(RenderingOf(
                attachments: [AttachmentOf("report.pdf", "application/pdf", 1024)])),
            linkIssuer: linkIssuer);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries), includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 1, 1], result.Emails.Select(email => ContentOf(email).Attachments.Count));
        Assert.All(
            result.Emails,
            email => Assert.Equal(
                AttachmentDownloadAvailability.Issued,
                ContentOf(email).Attachments.Single().Download.Availability));

        // One call per email rather than one for the batch, because the position a capability names is a position
        // within its own message.
        Assert.Equal(
            [.. IdentitiesOf(summaries).Select(storedEmailId => (storedEmailId, 1))],
            linkIssuer.Requested);
    }

    /// <summary>A message with no HTML part returns none of it even when it was asked for.</summary>
    [Fact]
    public async Task ReadContentAsync_SanitizedHtmlRequestedAndTheEmailHasNone_ReturnsNoHtmlRepresentation()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf(sanitizedHtml: null)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Null(content.Body.SanitizedHtml);
        Assert.Equal(EmailBodyAvailability.Readable, content.Body.Availability);
    }

    /// <summary>The truncation the rendering reported travels to the caller rather than being recomputed or dropped.</summary>
    [Fact]
    public async Task ReadContentAsync_BodyBeyondTheBound_ReportsTheTruncationAndTheOriginalLength()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                plainText: new EmailBodyRepresentation("Body", 4096, EmailBodyTruncation.BodyCharacterLimit))));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var plainText = ContentOf(Assert.Single(result.Emails)).Body.PlainText;
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, plainText.Truncation);
        Assert.Equal(4096, plainText.OriginalCharacterCount);
    }

    /// <summary>
    /// The budget is what stops ten emails from each returning the per-body bound. It is spent in the order the emails
    /// were named, so the total a call returns never exceeds it however large the emails are.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_SeveralLargeEmails_ReturnsNoMoreCharactersThanTheReadsBudgetAllows()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var reader = ReaderOver(
            summaries,
            new BoundedBodyEmailContentRenderer(new string('a', 200)),
            readOptions: new EmailContentReadOptions { MaxBodyCharacters = 100, MaxCharactersPerRead = 250 });

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([100, 100, 50], result.Emails.Select(email => ContentOf(email).Body.PlainText.Text.Length));
        Assert.Equal(250, result.Emails.Sum(email => ContentOf(email).Body.PlainText.Text.Length));
    }

    /// <summary>
    /// Which limit cut a body decides what a caller does next, so the two are reported apart: the first two emails are
    /// longer than one call ever returns, and the third is short only because the first two came before it.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_BudgetSpentByEarlierEmails_AttributesTheCutToTheBudgetRatherThanTheBodyBound()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var reader = ReaderOver(
            summaries,
            new BoundedBodyEmailContentRenderer(new string('a', 200)),
            readOptions: new EmailContentReadOptions { MaxBodyCharacters = 100, MaxCharactersPerRead = 250 });

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                EmailBodyTruncation.BodyCharacterLimit,
                EmailBodyTruncation.BodyCharacterLimit,
                EmailBodyTruncation.ReadCharacterBudget,
            ],
            result.Emails.Select(email => ContentOf(email).Body.PlainText.Truncation));
    }

    /// <summary>A budget already spent leaves an email with no text at all, and says so rather than reporting an empty message.</summary>
    [Fact]
    public async Task ReadContentAsync_BudgetExhaustedBeforeAnEmailIsReached_ReturnsItEmptyAndSaysTheBudgetCutIt()
    {
        // Arrange
        var summaries = SummariesOf(2);
        var reader = ReaderOver(
            summaries,
            new BoundedBodyEmailContentRenderer(new string('a', 200)),
            readOptions: new EmailContentReadOptions { MaxBodyCharacters = 100, MaxCharactersPerRead = 100 });

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries)),
            TestContext.Current.CancellationToken);

        // Assert
        var starved = ContentOf(result.Emails[1]).Body.PlainText;
        Assert.Equal(string.Empty, starved.Text);
        Assert.Equal(200, starved.OriginalCharacterCount);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, starved.Truncation);
    }

    /// <summary>Markup is content too, so what it returned is taken off the budget the next email is measured against.</summary>
    [Fact]
    public async Task ReadContentAsync_SanitizedHtmlRequested_SpendsTheBudgetOnBothRepresentations()
    {
        // Arrange
        var summaries = SummariesOf(2);
        var renderer = new BoundedBodyEmailContentRenderer(new string('a', 60), new string('b', 60));
        var reader = ReaderOver(
            summaries,
            renderer,
            readOptions: new EmailContentReadOptions { MaxBodyCharacters = 100, MaxCharactersPerRead = 200 });

        // Act
        await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries), includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([200, 80], renderer.ObservedRemainingCharacters);
    }

    /// <summary>
    /// A file's size decides nothing about what a read returns, because a link is the same few hundred characters
    /// whatever it points at. The character budget is untouched by one, which is what makes the two bounds this use case
    /// still carries about text alone.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_EmailsCarryingLargeAttachments_LeavesTheCharacterBudgetToTheBodies()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var renderer = new BoundedBodyEmailContentRenderer(
            new string('a', 60),
            attachmentOctetCounts: [50 * 1024 * 1024]);
        var reader = ReaderOver(
            summaries,
            renderer,
            readOptions: new EmailContentReadOptions { MaxBodyCharacters = 100, MaxCharactersPerRead = 200 });

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries), includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([200, 140, 80], renderer.ObservedRemainingCharacters);
        Assert.All(
            result.Emails,
            email => Assert.Equal(
                AttachmentDownloadAvailability.Issued,
                ContentOf(email).Attachments.Single().Download.Availability));
    }

    /// <summary>An encrypted body is a state of its own, never an empty message.</summary>
    [Fact]
    public async Task ReadContentAsync_EncryptedBody_ReportsItAsUnreadableRatherThanEmpty()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf(bodyIsEncrypted: true)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var body = ContentOf(Assert.Single(result.Emails)).Body;
        Assert.Equal(EmailBodyAvailability.EncryptedNotReadableLocally, body.Availability);
        Assert.Null(body.SanitizedHtml);
        Assert.Equal(string.Empty, body.PlainText.Text);
    }

    /// <summary>Mail the size limit kept out of storage is answered with what exists, and no repair is asked for.</summary>
    [Fact]
    public async Task ReadContentAsync_ContentNeverStoredBecauseItExceededTheSizeLimit_AnswersFromTheRowAndRequestsNoRepair()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(
            subject: "Quarterly report",
            senderAddress: "sender@example.test",
            toAddresses: ["recipient@example.test"]) with
        {
            ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
        };
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var contentStore = ContentStores.Substituted();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), repairRequests, contentStore);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailability.NotStoredExceededSizeLimit, content.Body.Availability);

        // Nobody ever read this message's parts, so its attachment counts are unknown rather than zero. The row holds
        // what the envelope reported, and an envelope describes no attachments.
        Assert.Null(content.AttachmentSummary);
        Assert.Equal("Quarterly report", content.Headers.Subject);
        Assert.Equal(
            ["sender@example.test", "recipient@example.test"],
            content.Headers.Participants.Select(participant => participant.Address.Address));
        Assert.Equal(
            [EmailAddressRole.From, EmailAddressRole.To],
            content.Headers.Participants.Select(participant => participant.Role));
        Assert.Empty(content.Attachments);
        Assert.Empty(repairRequests.Recorded);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Mail waiting for storage room reports that it is waiting, rather than reading as a damaged local copy.</summary>
    /// <remarks>
    /// Both halves matter. Recording a repair request would put a deliberate gap into the queue of copies that need
    /// re-fetching, and reporting the size-limit state would tell a caller that asking again is pointless when a later
    /// run is going to fetch exactly this message.
    /// </remarks>
    [Fact]
    public async Task ReadContentAsync_ContentNotStoredWhileStorageWasFull_ReportsItAsWaitingAndRequestsNoRepair()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(
            subject: "Quarterly report",
            senderAddress: "sender@example.test",
            toAddresses: ["recipient@example.test"]) with
        {
            ContentAvailability = StoredEmailContentAvailability.AwaitingStorageHeadroom,
        };
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var contentStore = ContentStores.Substituted();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), repairRequests, contentStore);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDownloadLinks: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailability.NotStoredAwaitingStorageHeadroom, content.Body.Availability);
        Assert.Equal("Quarterly report", content.Headers.Subject);
        Assert.Empty(repairRequests.Recorded);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadContentAsync_UnknownEmail_ReportsItAsNotFoundRatherThanRaising()
    {
        // Arrange
        var reader = ReaderOver(summary: null, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([StoredEmailId.Create(Guid.CreateVersion7())]),
            TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>Stored mail of an account the deployment stopped serving is refused, and refused the same way.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAnAccountThisDeploymentDoesNotServe_IsReportedAsNotFound()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(accountId: "retired");
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            accountCatalog: CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>
    /// Mail in an account another owner owns is refused by the same answer, so holding an identifier is not a way to
    /// read somebody else's correspondence.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAnAccountTheCallersOwnerDoesNotOwn_IsReportedAsNotFound()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var authorization = AccessAuthorizations.ForOwnerGranted(
            SyntheticMailOwner.Another,
            MailFathomPermission.MailRead);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            accountCatalog: OwnedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(summary.AccountId)),
            authorization: authorization);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        // The code an identifier naming nothing is answered with, which is what keeps "not yours" from being told apart
        // from "not here".
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>A folder an operator withheld from tools is unreadable by identifier too, and refused the same way as mail that is not there.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAFolderWithheldFromTools_IsReportedAsNotFound()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            folderParticipation: StubMailFolderParticipation
                .Mapping(new MailFolderIdentity(summary.AccountId, summary.FolderAlias))
                .Hiding(new MailFolderIdentity(summary.AccountId, summary.FolderAlias)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>
    /// Stored mail under an alias no mapping names is a folder this deployment does not have, so it is answered exactly
    /// as mail that is not there — which is what stops a removed mapping from leaving a mailbox readable and unrefreshed.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAFolderNoMappingNames_IsReportedAsNotFound()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            folderParticipation: StubMailFolderParticipation.Nothing);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>One email nobody here can serve costs the caller that email, and nothing else it asked about.</summary>
    [Fact]
    public async Task ReadContentAsync_OneUnknownEmailAmongKnownOnes_ServesTheRestAndReportsOnlyThatOne()
    {
        // Arrange
        var summaries = SummariesOf(2);
        var unknownEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var reader = ReaderOver(summaries, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summaries[0].StoredEmailId, unknownEmailId, summaries[1].StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [summaries[0].StoredEmailId, unknownEmailId, summaries[1].StoredEmailId],
            result.Emails.Select(email => email.StoredEmailId));
        Assert.NotNull(result.Emails[0].Content);
        Assert.Null(result.Emails[1].Content);
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, FailureOf(result.Emails[1]).ErrorCode);
        Assert.NotNull(result.Emails[2].Content);
    }

    /// <summary>An email belonging to someone else is one email refused, not a call refused.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAnUnservedAccountAmongServedOnes_ServesTheRest()
    {
        // Arrange
        var served = SyntheticEmailSummaries.Create();
        var unserved = SyntheticEmailSummaries.Create(accountId: "someone-elses");
        var reader = ReaderOver(
            [served, unserved],
            RendererReturning(RenderingOf()),
            accountCatalog: CatalogServing(served.AccountId));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([served.StoredEmailId, unserved.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Emails[0].Content);
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound, FailureOf(result.Emails[1]).ErrorCode);
    }

    /// <summary>Results are paired with what was asked for by position as well as by identity, so the order is the contract.</summary>
    [Fact]
    public async Task ReadContentAsync_SeveralEmails_ReturnsThemInTheOrderTheyWereNamed()
    {
        // Arrange
        var summaries = SummariesOf(4);
        var namedOutOfStorageOrder = new[]
        {
            summaries[2].StoredEmailId,
            summaries[0].StoredEmailId,
            summaries[3].StoredEmailId,
            summaries[1].StoredEmailId,
        };
        var reader = ReaderOver(summaries, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(namedOutOfStorageOrder),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(namedOutOfStorageOrder, result.Emails.Select(email => email.StoredEmailId));
    }

    [Fact]
    public async Task ReadContentAsync_ContentRecordedAsStoredButAbsent_ReportsAConsistencyFailureAndRequestsRepair()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            repairRequests,
            ContentStoreReturning(storedContent: null));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.EmailContentUnavailable, failure.ErrorCode);
        Assert.Contains("Missing", failure.Message, StringComparison.Ordinal);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.Missing)],
            repairRequests.Recorded);
    }

    /// <summary>A damaged local copy is one email's problem, so the emails read beside it are still returned.</summary>
    [Fact]
    public async Task ReadContentAsync_OneEmailWithAnUnusableLocalCopy_ServesTheRestAndRecordsOneRepairRequest()
    {
        // Arrange
        var summaries = SummariesOf(2);
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summaries,
            RendererReturning(RenderingOf()),
            repairRequests,
            ContentStoreOver(new Dictionary<StoredEmailId, StoredEmailContent?>
            {
                [summaries[0].StoredEmailId] = null,
                [summaries[1].StoredEmailId] = IntactContent(),
            }));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentUnavailable, FailureOf(result.Emails[0]).ErrorCode);
        Assert.NotNull(result.Emails[1].Content);
        Assert.Equal(
            [new EmailContentRepairRequest(summaries[0].StoredEmailId, EmailContentDefect.Missing)],
            repairRequests.Recorded);
    }

    [Theory]
    [MemberData(nameof(DamagedContent))]
    public async Task ReadContentAsync_StoredContentDiffersFromWhatWasRecorded_ReportsTheDefectAndRequestsRepair(
        StoredEmailContent damagedContent,
        EmailContentDefect expectedDefect)
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            repairRequests,
            ContentStoreReturning(damagedContent));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(expectedDefect.ToString(), FailureOf(Assert.Single(result.Emails)).Message, StringComparison.Ordinal);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, expectedDefect)],
            repairRequests.Recorded);
    }

    public static TheoryData<StoredEmailContent, EmailContentDefect> DamagedContent => new()
    {
        {
            new StoredEmailContent(StoredRawMime, StoredRawMime.Length + 1, SHA256.HashData(StoredRawMime)),
            EmailContentDefect.ByteLengthMismatch
        },
        {
            new StoredEmailContent(StoredRawMime, StoredRawMime.Length, SHA256.HashData([0x01])),
            EmailContentDefect.HashMismatch
        },
    };

    /// <summary>
    /// A payload the move carried and an operator has not released is held twice, and the object is the authoritative
    /// copy. Where the store had to serve the database's instead, the caller is answered from the bytes the deployment
    /// has — refusing over a message it holds would be a self-inflicted outage — and the object is recorded as the thing
    /// to repair, because a bucket answering nothing is exactly what must not be released against.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_ContentServedFromTheRetainedCopy_AnswersTheCallerAndRequestsRepairOfTheObject()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            repairRequests,
            ContentStoreReturning(IntactContent() with { WasServedFromRetainedCopy = true }));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(Assert.Single(result.Emails).Content);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.ObjectUnreadable)],
            repairRequests.Recorded);
    }

    [Fact]
    public async Task ReadContentAsync_StoredContentThatNoReaderCanRender_ReportsItAsUnreadableAndRequestsRepair()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Unreadable()));
        var reader = ReaderOver(summary, renderer, repairRequests);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            EmailContentDefect.Unreadable.ToString(),
            FailureOf(Assert.Single(result.Emails)).Message,
            StringComparison.Ordinal);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.Unreadable)],
            repairRequests.Recorded);
    }

    /// <summary>A failure a caller reads must name the email and nothing about the mail itself.</summary>
    [Fact]
    public async Task ReadContentAsync_UnknownEmail_WritesAFailureCarryingNoMailContent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(subject: "Payslip for March", senderAddress: "hr@example.test");
        var reader = ReaderOver(summary: null, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var message = FailureOf(Assert.Single(result.Emails)).Message;
        Assert.DoesNotContain("Payslip", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hr@example.test", message, StringComparison.Ordinal);
        Assert.Contains(summary.StoredEmailId.Value.ToString(), message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves the acceptance criterion structurally rather than path by path: the use case is constructed from ports
    /// that cannot reach a mail server, so no branch of it can download a message or touch a remote flag. An assertion
    /// per path would only cover the paths someone remembered to write.
    /// </summary>
    [Fact]
    public void EmailContentReader_EveryConstructorDependency_IsUnableToReachAMailServer()
    {
        // Arrange
        var mailboxPorts = new[]
        {
            typeof(IMailboxSession),
            typeof(IMailboxSessionFactory),
            typeof(MailboxSynchronizer),
        };

        // Act
        var dependencies = typeof(EmailContentReader)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        // Assert
        Assert.Empty(dependencies.Intersect(mailboxPorts));
    }

    /// <summary>Reading one's own mail through an assistant must not put a live credential into the conversation.</summary>
    [Theory]
    [InlineData(SensitiveContentScannerKind.Secrets)]
    [InlineData(SensitiveContentScannerKind.Pii)]
    public async Task ReadContentAsync_ABodyCarryingACredential_ReturnsItRedacted(
        SensitiveContentScannerKind scanner)
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System, scanner);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: $"the key is {Marker}, use it")),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal("the key is [redacted:CloudKey], use it", content.Body.PlainText.Text);
    }

    /// <summary>Both representations are message content, so a credential the markup kept is a credential handed out.</summary>
    [Fact]
    public async Task ReadContentAsync_ACredentialInBothRepresentations_ReturnsBothRedacted()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                new EmailBodyRepresentation($"the key is {Marker}", 19, EmailBodyTruncation.None),
                new EmailBodyRepresentation($"<p>the key is {Marker}</p>", 26, EmailBodyTruncation.None))),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal("the key is [redacted:CloudKey]", content.Body.PlainText.Text);
        Assert.Equal("<p>the key is [redacted:CloudKey]</p>", content.Body.SanitizedHtml?.Text);
    }

    /// <summary>
    /// The document is a third rendering of the same body, so it passes the same guard: a credential the markup carried
    /// would otherwise reach a reading pane by the one representation nobody had put under a scanner.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_ACredentialInTheDocument_ReturnsTheWordsOfEveryBlockRedacted()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: "Body") with { Document = DocumentSaying($"the key is {Marker}") }),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]) with { IncludeMailDocument = true },
            TestContext.Current.CancellationToken);

        // Assert
        var document = ContentOf(Assert.Single(result.Emails)).Body.Document;
        Assert.NotNull(document);
        Assert.Equal(
            ["the key is [redacted:CloudKey]", "the key is [redacted:CloudKey]"],
            MailDocumentTexts.Collect(document));
    }

    /// <summary>The message is the unit a caller waits for, so its guarding is reported once rather than once per field.</summary>
    [Fact]
    public async Task ReadContentAsync_ASwitchedOnScanner_ReportsOneGuardedOperationForTheWholeMessage()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                new EmailBodyRepresentation($"the key is {Marker}", 19, EmailBodyTruncation.None),
                new EmailBodyRepresentation($"<p>the key is {Marker}</p>", 26, EmailBodyTruncation.None))),
            egressGuard: egress.Guard);

        // Act
        await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        var operation = Assert.Single(egress.Telemetry.Operations);

        Assert.Equal(SensitiveContentEgressPoint.McpEmailContent, operation.EgressPoint);
        Assert.Equal(egress.Telemetry.Guarded.Count, operation.GuardedTextCount);
        Assert.True(operation.GuardedTextCount > 1);
        Assert.True(operation.WasClosed);
    }

    /// <summary>A listing redacts a subject and a display name, and two tools disagreeing about one message is what a caller cannot resolve.</summary>
    [Fact]
    public async Task ReadContentAsync_ACredentialInTheHeaders_RedactsTheSubjectAndTheDisplayNameAndKeepsTheAddress()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(headers: HeadersOf(
                subject: $"fwd: {Marker}",
                participants: [ParticipantOf($"{Marker} bot", "alerts@example.test")]))),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var participant = Assert.Single(content.Headers.Participants);
        Assert.Equal("fwd: [redacted:CloudKey]", content.Headers.Subject);
        Assert.Equal("[redacted:CloudKey] bot", participant.Address.DisplayName);
        Assert.Equal("alerts@example.test", participant.Address.Address);
    }

    /// <summary>
    /// A scan is a round trip where the analyzer runs in a container, so an addressee list somebody expanded must not
    /// turn one local read into thousands of them. What is dropped past the bound is the name, never the address, and
    /// never a name nothing scanned.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_MoreNamedParticipantsThanOneReadScans_PublishesTheRestWithoutADisplayName()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var participants = Enumerable
            .Range(0, 45)
            .Select(position => ParticipantOf($"{Marker} {position}", $"recipient{position}@example.test"))
            .ToArray();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(headers: HeadersOf(subject: null, participants))),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var published = ContentOf(Assert.Single(result.Emails)).Headers.Participants;

        Assert.Equal(45, published.Count);
        Assert.Equal(
            40,
            egress.Scanner.ScannedTexts.Count(text => text.StartsWith(Marker, StringComparison.Ordinal)));
        Assert.Equal(
            [.. Enumerable.Repeat("[redacted:CloudKey]", 40).Select((name, position) => $"{name} {position}")],
            published.Take(40).Select(participant => participant.Address.DisplayName));
        Assert.All(published.Skip(40), participant => Assert.Null(participant.Address.DisplayName));
        Assert.Equal(
            [.. participants.Select(participant => participant.Address.Address)],
            published.Select(participant => participant.Address.Address));
    }

    /// <summary>Text nothing analyzed is text this deployment does not hand out, and a caller has to be told which bound ended it.</summary>
    [Fact]
    public async Task ReadContentAsync_ABodyBeyondTheAnalyzedCeiling_ReturnsWhatWasScannedAndNamesTheCeiling()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(
            Marker,
            TimeProvider.System,
            bounds: SensitiveContentScanBounds.Create(11, TimeSpan.FromSeconds(5), 4));
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: "the key is beyond what one scan reads")),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var plainText = ContentOf(Assert.Single(result.Emails)).Body.PlainText;
        Assert.Equal("the key is ", plainText.Text);
        Assert.Equal(EmailBodyTruncation.SensitiveContentScanCeiling, plainText.Truncation);
        Assert.Equal(37, plainText.OriginalCharacterCount);
    }

    /// <summary>The ceiling is where the returned text now ends, whichever bound had already cut it.</summary>
    [Fact]
    public async Task ReadContentAsync_ABodyTheReadHadAlreadyCut_NamesTheCeilingWhenTheScanCutItFurther()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(
            Marker,
            TimeProvider.System,
            bounds: SensitiveContentScanBounds.Create(11, TimeSpan.FromSeconds(5), 4));
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(new EmailBodyRepresentation(
                "the key is beyond what one scan reads",
                OriginalCharacterCount: 4096,
                EmailBodyTruncation.BodyCharacterLimit))),
            egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            EmailBodyTruncation.SensitiveContentScanCeiling,
            ContentOf(Assert.Single(result.Emails)).Body.PlainText.Truncation);
    }

    /// <summary>A read that fell back to the stored text would hand out the one message nobody scanned.</summary>
    [Fact]
    public async Task ReadContentAsync_ADetectorThatCannotAnswer_FailsTheReadRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: "whatever the message said")),
            egressGuard: egress.Guard);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            reader.ReadContentAsync(RequestFor([summary.StoredEmailId]), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.DoesNotContain("whatever the message said", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Scanning in flight is what keeps the local copy the artifact it was fetched as.</summary>
    [Fact]
    public async Task ReadContentAsync_ABodyCarryingACredential_RewritesNothingItRead()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var contentStore = ContentStoreReturning(IntactContent());
        var repairRequestStore = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: $"the key is {Marker}")),
            repairRequestStore,
            contentStore,
            egressGuard: egress.Guard);

        // Act
        await reader.ReadContentAsync(RequestFor([summary.StoredEmailId]), TestContext.Current.CancellationToken);

        // Assert
        await contentStore.DidNotReceiveWithAnyArgs().SaveContentAsync(
            default!,
            default!,
            default!,
            default!,
            CancellationToken.None);
        Assert.Empty(repairRequestStore.Recorded);
    }

    /// <summary>A message with no words to scan must cost no scan at all, whichever reason it has none.</summary>
    [Theory]
    [InlineData(StoredEmailContentAvailability.ExceededSizeLimit)]
    [InlineData(StoredEmailContentAvailability.AwaitingStorageHeadroom)]
    public async Task ReadContentAsync_AnEmailWhoseContentWasNeverStored_ReachesNoScannerForItsBody(
        StoredEmailContentAvailability availability)
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create() with { ContentAvailability = availability };
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(EmailBodyAvailability.Readable, ContentOf(Assert.Single(result.Emails)).Body.Availability);
        Assert.Empty(egress.Scanner.ScannedTexts);
    }

    /// <summary>
    /// A citation drawn from a redacted chunk has to land on the same redacted text when the reader opens the message,
    /// or the citation reads as wrong and the assistant looks like it invented the quote. Asserted over both paths
    /// directly rather than inferred from their sharing a port: what has to agree is the text, not the wiring.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_TheBodyTheDerivedPathIndexed_RedactsToExactlyTheSameText()
    {
        // Arrange
        const string body = $"the key is {Marker}, and {Marker} again, in a message somebody sent";

        var scanner = new MarkerSensitiveContentScanner(
            Marker,
            SensitiveContentScannerKind.Secrets,
            TimeProvider.System);
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    scanner.Scanner,
                    [MarkerSensitiveContentScanner.Category],
                    []),
            ]);
        using var redactor = new SensitiveContentRedactor(plan, [scanner], TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: body)),
            egressGuard: new SensitiveContentEgressGuard(
                redactor,
                new RecordingSensitiveContentEgressTelemetry(),
                TimeProvider.System));
        var derivedReader = new RedactingEmailMimeReader(
            MimeReaderYielding(body),
            new SensitiveContentDerivationGuard(
                redactor,
                SensitiveContentDerivationStamp.Compute(plan, [scanner]),
                new RecordingSensitiveContentDerivationTelemetry(),
                TimeProvider.System));

        // Act
        var read = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);
        var derived = await derivedReader.ReadMetadataAsync(
            RemoteContentOf(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey], and [redacted:CloudKey] again, in a message somebody sent", derived.Metadata?.Text.OriginalText);
        Assert.Equal(derived.Metadata?.Text.OriginalText, ContentOf(Assert.Single(read.Emails)).Body.PlainText.Text);
    }

    /// <summary>
    /// A message whose content was never stored is answered from the listing row, and that row's subject and sender
    /// name are the whole of what it publishes that a message's author wrote. Serving them here while
    /// <c>list_emails</c> redacts the same two would be the two tools disagreeing about one message.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_UnstoredContentWhoseRowCarriesACredential_RedactsWhatTheRowPublishes()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create(
            subject: $"re: {Marker}",
            senderAddress: "alerts@example.test") with
        {
            ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
            SenderDisplayName = $"{Marker} bot",
        };
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), egressGuard: egress.Guard);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var sender = Assert.Single(content.Headers.Participants);

        Assert.Equal(EmailBodyAvailability.NotStoredExceededSizeLimit, content.Body.Availability);
        Assert.Equal("re: [redacted:CloudKey]", content.Headers.Subject);
        Assert.Equal("[redacted:CloudKey] bot", sender.Address.DisplayName);
        Assert.Equal("alerts@example.test", sender.Address.Address);
    }

    /// <summary>With both switches off the read is the one it was, and no detector is constructed to prove it.</summary>
    [Fact]
    public async Task ReadContentAsync_ADeploymentThatScansNothing_ReturnsWhatTheRenderingProduced()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                plainText: $"the key is {Marker}",
                headers: HeadersOf(
                    subject: $"fwd: {Marker}",
                    participants: [ParticipantOf($"{Marker} bot", "alerts@example.test")]))));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal($"the key is {Marker}", content.Body.PlainText.Text);
        Assert.Equal($"fwd: {Marker}", content.Headers.Subject);
        Assert.Equal($"{Marker} bot", Assert.Single(content.Headers.Participants).Address.DisplayName);
    }

    /// <summary>
    /// The read is reported as the operation it is, so the content-store reads it causes have a use case above them in
    /// a trace rather than only the protocol call that reached it.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_EmailsThatWereServed_ReportsTheReadAndHowManyItServed()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var summaries = SummariesOf(2);
        var reader = ReaderOver(
            summaries,
            RendererReturning(RenderingOf()),
            readTelemetry: readTelemetry);

        // Act
        await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries)),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.ReadEmailContent, read.Operation);
        Assert.Equal(2, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    /// <summary>
    /// What is counted is what was served rather than what was named, because the gap between the two is a caller
    /// working from a listing that has moved on.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_AnIdentifierThisDeploymentDoesNotServe_CountsOnlyTheEmailsItServed()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var summaries = SummariesOf(1);
        var reader = ReaderOver(
            summaries,
            RendererReturning(RenderingOf()),
            readTelemetry: readTelemetry);

        // Act
        await reader.ReadContentAsync(
            RequestFor([.. IdentitiesOf(summaries), StoredEmailId.Create(Guid.CreateVersion7())]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, Assert.Single(readTelemetry.Reads).ResultCount);
    }

    /// <summary>
    /// A conversation is bounded by the same count a caller's own list is held to, and what the bound left out is named
    /// rather than dropped, so a second call asks for those messages directly instead of guessing they exist.
    /// </summary>
    [Fact]
    public async Task ReadContentAsync_ConversationLongerThanOneRead_ServesTheBoundedBatchAndNamesWhatItLeft()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());
        var conversation = ConversationOf(threadId, GetEmailContentRequest.MaximumEmails + 3);
        var reader = ReaderOver(
            conversation,
            RendererReturning(RenderingOf()),
            threadReader: ThreadReaderOver(threadId, conversation));

        // Act
        var result = await reader.ReadContentAsync(
            GetEmailContentRequest.CreateForThread(threadId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            IdentitiesOf(conversation).Take(GetEmailContentRequest.MaximumEmails),
            result.Emails.Select(outcome => outcome.StoredEmailId));
        Assert.Equal(
            IdentitiesOf(conversation).Skip(GetEmailContentRequest.MaximumEmails),
            result.UnreadThreadEmails);
    }

    /// <summary>Every published message carries where it sits in its conversation and which message it answers.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailAnsweringAnotherOfItsConversation_PublishesItsPlaceAndThatAncestor()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());
        var conversation = ConversationOf(threadId, count: 2);
        var reader = ReaderOver(
            conversation,
            RendererReturning(RenderingOf()),
            threadReader: ThreadReaderOver(threadId, conversation, asAChain: true));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([conversation[1].StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var thread = ContentOf(Assert.Single(result.Emails)).Thread;

        Assert.NotNull(thread);
        Assert.Equal(threadId, thread.ThreadId);
        Assert.Equal(1, thread.Position);
        Assert.Equal(conversation[0].StoredEmailId, thread.AnsweredStoredEmailId);
        Assert.Equal(2, thread.EmailCount);
        Assert.Equal(
            conversation[0].StoredEmailId,
            Assert.Single(thread.OtherEmails).Email.StoredEmailId);
        Assert.False(thread.MoreEmailsNotNamed);
    }

    /// <summary>A conversation this deployment holds nothing of is served as nothing rather than as a refusal.</summary>
    [Fact]
    public async Task ReadContentAsync_ConversationNoStoredMailBelongsTo_ServesNoEmailAndNamesNone()
    {
        // Arrange
        var reader = ReaderOver(SummariesOf(0), RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            GetEmailContentRequest.CreateForThread(EmailThreadId.Create(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Emails);
        Assert.Empty(result.UnreadThreadEmails);
    }

    /// <summary>Builds a conversation of stored mail, one message a minute apart, all of it readable by tools.</summary>
    private static EmailSummary[] ConversationOf(EmailThreadId threadId, int count) =>
    [
        .. Enumerable.Range(0, count).Select(ordinal =>
            SyntheticEmailSummaries.Create(
                receivedAt: new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero).AddMinutes(ordinal),
                subject: $"Message {ordinal}") with
            {
                ThreadId = threadId,
            }),
    ];

    /// <summary>Answers the conversation those summaries belong to, optionally as a chain each message answers.</summary>
    private static StubEmailThreadReader ThreadReaderOver(
        EmailThreadId threadId,
        EmailSummary[] conversation,
        bool asAChain = false) =>
        new(
        [
            .. conversation.Select((summary, ordinal) => (threadId, new ThreadedEmailSummary
            {
                StoredEmailId = summary.StoredEmailId,
                AccountId = summary.AccountId,
                FolderAlias = summary.FolderAlias,
                ParentStoredEmailId = asAChain && ordinal > 0 ? conversation[ordinal - 1].StoredEmailId : null,
                Subject = summary.Subject,
                SentAt = summary.SentAt,
                SenderAddress = summary.SenderAddress,
            })),
        ]);

    private static GetEmailContentRequest RequestFor(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        bool includeSanitizedHtml = false,
        bool includeAttachmentDownloadLinks = false) =>
        GetEmailContentRequest.Create(storedEmailIds, includeSanitizedHtml, includeAttachmentDownloadLinks);

    private static ReadEmailContent ContentOf(EmailContentReadOutcome outcome) =>
        outcome.Content ?? throw new InvalidOperationException(
            $"The email was not served: {outcome.Failure?.Message}");

    private static EmailContentReadFailure FailureOf(EmailContentReadOutcome outcome) =>
        outcome.Failure ?? throw new InvalidOperationException("The email was served rather than refused.");

    private static EmailSummary[] SummariesOf(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => SyntheticEmailSummaries.Create())];

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task ReadContentAsync_ACallerGrantedOnlyTheAnsweringPermission_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: "Body as written")),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    private static StoredEmailId[] IdentitiesOf(IReadOnlyList<EmailSummary> summaries) =>
        [.. summaries.Select(summary => summary.StoredEmailId)];

    private static EmailContentReader ReaderOver(
        EmailSummary? summary,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IEmailContentStore? contentStore = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        IAttachmentDownloadLinkIssuer? linkIssuer = null,
        EmailContentReadOptions? readOptions = null,
        IMailFolderParticipationReader? folderParticipation = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null,
        IEmailThreadReader? threadReader = null) => new(
        SummaryReaderReturning(summary),
        threadReader ?? new StubEmailThreadReader(),
        contentStore ?? ContentStoreReturning(IntactContent()),
        renderer,
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(MailAccountId.Create(summary?.AccountId.Value ?? SyntheticEmailSummaries.DefaultAccountId)),
            folderParticipation ?? MappingFoldersOf(summary is null ? [] : [summary]),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        linkIssuer ?? new RecordingAttachmentDownloadLinkIssuer(),
        egressGuard ?? SensitiveContentEgressGuards.Inactive(),
        readOptions ?? new EmailContentReadOptions(),
        readTelemetry ?? new RecordingMailboxReadTelemetry(),
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    private static EmailContentReader ReaderOver(
        IReadOnlyList<EmailSummary> summaries,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IEmailContentStore? contentStore = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        IAttachmentDownloadLinkIssuer? linkIssuer = null,
        EmailContentReadOptions? readOptions = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null,
        IEmailThreadReader? threadReader = null) => new(
        SummaryReaderOver(summaries),
        threadReader ?? new StubEmailThreadReader(),
        contentStore ?? ContentStoreReturning(IntactContent()),
        renderer,
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)),
            MappingFoldersOf(summaries),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        linkIssuer ?? new RecordingAttachmentDownloadLinkIssuer(),
        egressGuard ?? SensitiveContentEgressGuards.Inactive(),
        readOptions ?? new EmailContentReadOptions(),
        readTelemetry ?? new RecordingMailboxReadTelemetry(),
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    /// <summary>Maps the folders these emails were stored from, which is what a deployment holding them has configured.</summary>
    /// <remarks>
    /// A folder no mapping names does not exist as far as MailFathom is concerned, so a reader arranged without one
    /// answers every read with nothing. Stating the mapping is therefore part of arranging stored mail at all, rather
    /// than something only a test about folder participation does.
    /// </remarks>
    private static StubMailFolderParticipation MappingFoldersOf(IReadOnlyList<EmailSummary> summaries) =>
        StubMailFolderParticipation.Mapping(
            [.. summaries.Select(summary => new MailFolderIdentity(summary.AccountId, summary.FolderAlias))]);

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summary));

        return reader;
    }

    private static IStoredEmailSummaryReader SummaryReaderOver(IReadOnlyList<EmailSummary> summaries)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                summaries.FirstOrDefault(summary => summary.StoredEmailId == call.Arg<StoredEmailId>())));

        return reader;
    }

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static IEmailContentStore ContentStoreOver(Dictionary<StoredEmailId, StoredEmailContent?> storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                storedContent.TryGetValue(call.Arg<StoredEmailId>(), out var content) ? content : null));

        return contentStore;
    }

    private static IEmailContentRenderer RendererReturning(EmailContentRendering rendering)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(rendering)));

        return renderer;
    }

    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([.. servedAccountIds.Select(accountId => SyntheticServedAccount.Of(accountId))]);

        return catalog;
    }

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    private static EmailContentRendering RenderingOf(
        string plainText = "Body",
        EmailBodyRepresentation? sanitizedHtml = null,
        bool bodyIsEncrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        int inlineResourceCount = 0,
        EmailContentHeaders? headers = null) =>
        RenderingOf(
            new EmailBodyRepresentation(plainText, plainText.Length, EmailBodyTruncation.None),
            sanitizedHtml,
            bodyIsEncrypted,
            attachments,
            inlineResourceCount,
            headers);

    private static EmailContentRendering RenderingOf(
        EmailBodyRepresentation plainText,
        EmailBodyRepresentation? sanitizedHtml = null,
        bool bodyIsEncrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        int inlineResourceCount = 0,
        EmailContentHeaders? headers = null) => new(
        headers ?? HeadersOf("Subject"),
        plainText,
        sanitizedHtml,
        bodyIsEncrypted,
        EmailAttachmentSummary.Create(
            attachments ?? [],
            inlineResourceCount,
            isEncrypted: bodyIsEncrypted,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false),
        attachments ?? []);

    /// <summary>A document saying the same thing twice, so a rewrite that lost a place would report the wrong words.</summary>
    private static MailDocument DocumentSaying(string text) => MailDocument.Reduced(
        [
            new MailParagraphBlock(
                [new MailInlineRun(text, MailTextEmphasis.None, Foreground: null, Link: null)],
                MailBlockAlignment.Inherited),
            new MailQuoteBlock(
                1,
                [
                    new MailParagraphBlock(
                        [new MailInlineRun(text, MailTextEmphasis.None, Foreground: null, Link: null)],
                        MailBlockAlignment.Inherited),
                ]),
        ],
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false);

    /// <summary>The extraction the derived path performs over one body, which is the reading the index is built from.</summary>
    private static IEmailMimeReader MimeReaderYielding(string body)
    {
        var reader = Substitute.For<IEmailMimeReader>();

        reader.ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                call.Arg<RemoteEmailContent>()!.OccurrenceId,
                Subject: "Subject",
                SentAt: null,
                ReceivedAt: null,
                Participants: [],
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                ExtractedEmailText.FromPlainTextBody(body, body),
                SenderAuthentication.NotEstablished()))));

        return reader;
    }

    private static RemoteEmailContent RemoteContentOf() => new(
        EmailOccurrenceId.Create(
            MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
            new MailFolderResolutionId(
                MailFolderAlias.Create(SyntheticEmailSummaries.DefaultFolderAlias),
                MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(5),
            ImapUid.Create(11)),
        StoredRawMime);

    private static EmailContentHeaders HeadersOf(
        string? subject,
        IReadOnlyList<EmailParticipant>? participants = null) => new(
        subject,
        SentAt: null,
        ReceivedAt: null,
        participants ?? [],
        EmailThreadReferences.None);

    private static EmailParticipant ParticipantOf(string? displayName, string address) =>
        EmailAddress.TryCreate(displayName, address, out var emailAddress)
            ? new EmailParticipant(EmailAddressRole.From, emailAddress)
            : throw new InvalidOperationException($"'{address}' is not a usable address.");

    /// <summary>Builds the description of one attachment a rendering returned.</summary>
    private static ExtractedEmailAttachment AttachmentOf(
        string fileName,
        string mediaType,
        long decodedSizeOctets) =>
        new(AttachmentFileNameOf(fileName), mediaType, decodedSizeOctets);

    private static AttachmentFileName AttachmentFileNameOf(string fileName) =>
        AttachmentFileName.TryNormalize(fileName, out var normalized)
            ? normalized
            : throw new InvalidOperationException($"'{fileName}' is not a usable attachment file name.");
}
