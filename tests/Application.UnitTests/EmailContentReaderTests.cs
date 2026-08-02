// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers the email content use case: what it serves, what it refuses, and what it records when it refuses.</summary>
public sealed class EmailContentReaderTests
{
    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    [Fact]
    public async Task ReadContentAsync_ReadableEmail_ReturnsWhatTheRenderingProduced()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var rendering = RenderingOf(
            plainText: "Body as written",
            attachments: [new ExtractedEmailAttachment(AttachmentFileNameOf("report.pdf"), "application/pdf", 1024)]);
        var reader = ReaderOver(summary, RendererReturning(rendering));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDetails: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailability.Readable, content.Body.Availability);
        Assert.Equal("Body as written", content.Body.PlainText.Text);
        Assert.Equal(rendering.Headers, content.Headers);
        Assert.Equal(["report.pdf"], content.Attachments?.Select(attachment => attachment.FileName?.Value));
        Assert.Equal(summary.AccountId, content.AccountId);
        Assert.Equal(summary.FolderAlias, content.FolderAlias);
        Assert.Equal(summary.RemoteFlags, content.RemoteFlags);
    }

    /// <summary>The per-attachment list is re-derived, and what it describes must be what the stored row counted.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentDetailsRequested_ReturnsAttachmentsConsistentWithThePersistedSummary()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 2, inlineResourceCount: 1);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments:
                [
                    new ExtractedEmailAttachment(AttachmentFileNameOf("first.pdf"), "application/pdf", 1024),
                    new ExtractedEmailAttachment(AttachmentFileNameOf("second.png"), "image/png", 1024),
                ],
                inlineResourceCount: 1)));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDetails: true),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var attachmentSummary = Assert.IsType<StoredEmailAttachmentSummary>(content.AttachmentSummary);
        Assert.NotNull(content.Attachments);
        Assert.Equal(attachmentSummary.AttachmentCount, content.Attachments.Count);
        Assert.Equal(attachmentSummary.TotalSizeOctets, content.Attachments.Sum(attachment => attachment.DecodedSizeOctets));

        // The row counted the same message, so the derived answer and the persisted one agree here — which is the
        // consistency the specification asks for. Where they could disagree, the derived one is what is published.
        Assert.Equal(summary.Attachments.AttachmentCount, attachmentSummary.AttachmentCount);
        Assert.Equal(summary.Attachments.InlineResourceCount, attachmentSummary.InlineResourceCount);
    }

    /// <summary>A file name is sender-chosen mail content, so a read that only wanted the body is told how many and not what.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentDetailsNotRequested_CountsTheAttachmentsAndNamesNone()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(
                attachments: [new ExtractedEmailAttachment(AttachmentFileNameOf("payslip.pdf"), "application/pdf", 2048)])));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId]),
            TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Null(content.Attachments);
        Assert.NotNull(content.AttachmentSummary);
        Assert.Equal(1, content.AttachmentSummary.AttachmentCount);
        Assert.Equal(2048, content.AttachmentSummary.TotalSizeOctets);
    }

    /// <summary>Zero attachments is a finding a caller can act on, so it is stated under either setting.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadContentAsync_EmailCarryingNoAttachments_ReportsZeroUnderEitherSetting(
        bool includeAttachmentDetails)
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDetails: includeAttachmentDetails),
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

    /// <summary>The same holds for the attachment flag: a batch is described consistently or not at all.</summary>
    [Fact]
    public async Task ReadContentAsync_AttachmentDetailsRequestedForSeveralEmails_DescribesEveryOneOfThem()
    {
        // Arrange
        var summaries = SummariesOf(3);
        var reader = ReaderOver(
            summaries,
            RendererReturning(RenderingOf(
                attachments: [new ExtractedEmailAttachment(AttachmentFileNameOf("report.pdf"), "application/pdf", 1024)])));

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor(IdentitiesOf(summaries), includeAttachmentDetails: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [1, 1, 1],
            result.Emails.Select(email => ContentOf(email).Attachments?.Count));
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
        var contentStore = Substitute.For<IEmailContentStore>();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf()), repairRequests, contentStore);

        // Act
        var result = await reader.ReadContentAsync(
            RequestFor([summary.StoredEmailId], includeAttachmentDetails: true),
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
        Assert.Empty(content.Attachments ?? []);
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

    private static GetEmailContentRequest RequestFor(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        bool includeSanitizedHtml = false,
        bool includeAttachmentDetails = false) =>
        GetEmailContentRequest.Create(storedEmailIds, includeSanitizedHtml, includeAttachmentDetails);

    private static ReadEmailContent ContentOf(EmailContentReadOutcome outcome) =>
        outcome.Content ?? throw new InvalidOperationException(
            $"The email was not served: {outcome.Failure?.Message}");

    private static EmailContentReadFailure FailureOf(EmailContentReadOutcome outcome) =>
        outcome.Failure ?? throw new InvalidOperationException("The email was served rather than refused.");

    private static EmailSummary[] SummariesOf(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => SyntheticEmailSummaries.Create())];

    private static StoredEmailId[] IdentitiesOf(IReadOnlyList<EmailSummary> summaries) =>
        [.. summaries.Select(summary => summary.StoredEmailId)];

    private static EmailContentReader ReaderOver(
        EmailSummary? summary,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IEmailContentStore? contentStore = null,
        IMailAccountCatalog? accountCatalog = null,
        EmailContentReadOptions? readOptions = null) => new(
        SummaryReaderReturning(summary),
        contentStore ?? ContentStoreReturning(IntactContent()),
        renderer,
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        accountCatalog ?? CatalogServing(MailAccountId.Create(summary?.AccountId.Value ?? SyntheticEmailSummaries.DefaultAccountId)),
        readOptions ?? new EmailContentReadOptions());

    private static EmailContentReader ReaderOver(
        IReadOnlyList<EmailSummary> summaries,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IEmailContentStore? contentStore = null,
        IMailAccountCatalog? accountCatalog = null,
        EmailContentReadOptions? readOptions = null) => new(
        SummaryReaderOver(summaries),
        contentStore ?? ContentStoreReturning(IntactContent()),
        renderer,
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        accountCatalog ?? CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)),
        readOptions ?? new EmailContentReadOptions());

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
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static IEmailContentStore ContentStoreOver(Dictionary<StoredEmailId, StoredEmailContent?> storedContent)
    {
        var contentStore = Substitute.For<IEmailContentStore>();
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

    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccountIds.Returns([.. servedAccountIds]);

        return catalog;
    }

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    private static EmailContentRendering RenderingOf(
        string plainText = "Body",
        EmailBodyRepresentation? sanitizedHtml = null,
        bool bodyIsEncrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        int inlineResourceCount = 0) =>
        RenderingOf(
            new EmailBodyRepresentation(plainText, plainText.Length, EmailBodyTruncation.None),
            sanitizedHtml,
            bodyIsEncrypted,
            attachments,
            inlineResourceCount);

    private static EmailContentRendering RenderingOf(
        EmailBodyRepresentation plainText,
        EmailBodyRepresentation? sanitizedHtml = null,
        bool bodyIsEncrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        int inlineResourceCount = 0) => new(
        new EmailContentHeaders(
            "Subject",
            SentAt: null,
            ReceivedAt: null,
            [],
            EmailThreadReferences.None),
        plainText,
        sanitizedHtml,
        bodyIsEncrypted,
        EmailAttachmentSummary.Create(
            attachments ?? [],
            inlineResourceCount,
            isEncrypted: bodyIsEncrypted,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false));

    private static AttachmentFileName AttachmentFileNameOf(string fileName) =>
        AttachmentFileName.TryNormalize(fileName, out var normalized)
            ? normalized
            : throw new InvalidOperationException($"'{fileName}' is not a usable attachment file name.");
}
