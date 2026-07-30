// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MailMcp.Application.Accounts;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.GetEmailContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;
using NSubstitute;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers the email content use case: what it serves, what it refuses, and what it records when it refuses.</summary>
public sealed class EmailContentReaderTests
{
    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    [Fact]
    public async Task ReadContentAsync_ReadableMessage_ReturnsWhatTheRenderingProduced()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var rendering = RenderingOf(
            plainText: "Body as written",
            attachments: [new ExtractedEmailAttachment(AttachmentFileNameOf("report.pdf"), "application/pdf", 1024)]);
        var reader = ReaderOver(summary, RendererReturning(rendering));

        // Act
        var result = await reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailBodyAvailability.Readable, result.Body.Availability);
        Assert.Equal("Body as written", result.Body.PlainText.Text);
        Assert.Equal(rendering.Headers, result.Headers);
        Assert.Equal(["report.pdf"], result.Attachments.Select(attachment => attachment.FileName?.Value));
        Assert.Equal(summary.AccountId, result.AccountId);
        Assert.Equal(summary.FolderAlias, result.FolderAlias);
        Assert.Equal(summary.RemoteFlags, result.RemoteFlags);
    }

    /// <summary>The per-attachment list is re-derived, and what it describes must be what the stored row counted.</summary>
    [Fact]
    public async Task ReadContentAsync_ReadableMessage_ReturnsAttachmentsConsistentWithThePersistedSummary()
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
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(summary.Attachments.AttachmentCount, result.Attachments.Count);
        Assert.Equal(summary.Attachments.TotalSizeOctets, result.Attachments.Sum(attachment => attachment.DecodedSizeOctets));
        Assert.Equal(summary.Attachments.InlineResourceCount, result.AttachmentSummary.InlineResourceCount);
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
        var renderer = RendererReturning(RenderingOf(sanitizedHtml: new EmailBodyRepresentation("<p>Body</p>", 11, WasTruncated: false)));
        var reader = ReaderOver(summary, renderer);

        // Act
        var result = await reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId, IncludeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("<p>Body</p>", result.Body.SanitizedHtml?.Text);
        await renderer.Received(1).RenderAsync(
            Arg.Any<StoredEmailContent>(),
            includeSanitizedHtml: true,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message with no HTML part returns none of it even when it was asked for.</summary>
    [Fact]
    public async Task ReadContentAsync_SanitizedHtmlRequestedAndTheMessageHasNone_ReturnsNoHtmlRepresentation()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, RendererReturning(RenderingOf(sanitizedHtml: null)));

        // Act
        var result = await reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId, IncludeSanitizedHtml: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Body.SanitizedHtml);
        Assert.Equal(EmailBodyAvailability.Readable, result.Body.Availability);
    }

    /// <summary>The truncation the rendering reported travels to the caller rather than being recomputed or dropped.</summary>
    [Fact]
    public async Task ReadContentAsync_BodyBeyondTheBound_ReportsTheTruncationAndTheOriginalLength()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf(plainText: new EmailBodyRepresentation("Body", OriginalCharacterCount: 4096, WasTruncated: true))));

        // Act
        var result = await reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Body.PlainText.WasTruncated);
        Assert.Equal(4096, result.Body.PlainText.OriginalCharacterCount);
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
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailBodyAvailability.EncryptedNotReadableLocally, result.Body.Availability);
        Assert.Null(result.Body.SanitizedHtml);
        Assert.Equal(string.Empty, result.Body.PlainText.Text);
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
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailBodyAvailability.NotStoredExceededSizeLimit, result.Body.Availability);
        Assert.Equal("Quarterly report", result.Headers.Subject);
        Assert.Equal(
            ["sender@example.test", "recipient@example.test"],
            result.Headers.Participants.Select(participant => participant.Address.Address));
        Assert.Equal(
            [EmailAddressRole.From, EmailAddressRole.To],
            result.Headers.Participants.Select(participant => participant.Role));
        Assert.Empty(result.Attachments);
        Assert.Empty(repairRequests.Recorded);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadContentAsync_UnknownEmail_IsRefusedAsNotFound()
    {
        // Arrange
        var reader = ReaderOver(summary: null, RendererReturning(RenderingOf()));

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailNotFoundException>(() => reader.ReadContentAsync(
            new GetEmailContentRequest(StoredEmailId.Create(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailMcpErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    /// <summary>Stored mail of an account the deployment stopped serving is refused, and refused the same way.</summary>
    [Fact]
    public async Task ReadContentAsync_EmailOfAnAccountThisDeploymentDoesNotServe_IsRefusedAsNotFound()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(accountId: "retired");
        var reader = ReaderOver(
            summary,
            RendererReturning(RenderingOf()),
            accountCatalog: CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)));

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailNotFoundException>(() => reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailMcpErrorCode.StoredEmailNotFound, failure.ErrorCode);
    }

    [Fact]
    public async Task ReadContentAsync_ContentRecordedAsStoredButAbsent_ReportsAConsistencyErrorAndRequestsRepair()
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
        var failure = await Assert.ThrowsAsync<EmailContentUnavailableException>(() => reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailMcpErrorCode.EmailContentUnavailable, failure.ErrorCode);
        Assert.Equal(EmailContentDefect.Missing, failure.Defect);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.Missing)],
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
        var failure = await Assert.ThrowsAsync<EmailContentUnavailableException>(() => reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expectedDefect, failure.Defect);
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
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Unreadable()));
        var reader = ReaderOver(summary, renderer, repairRequests);

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentUnavailableException>(() => reader.ReadContentAsync(
            new GetEmailContentRequest(summary.StoredEmailId),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmailContentDefect.Unreadable, failure.Defect);
        Assert.Equal(
            [new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.Unreadable)],
            repairRequests.Recorded);
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

    private static EmailContentReader ReaderOver(
        EmailSummary? summary,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IEmailContentStore? contentStore = null,
        IMailAccountCatalog? accountCatalog = null) => new(
        SummaryReaderReturning(summary),
        contentStore ?? ContentStoreReturning(IntactContent()),
        renderer,
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        accountCatalog ?? CatalogServing(MailAccountId.Create(summary?.AccountId.Value ?? SyntheticEmailSummaries.DefaultAccountId)));

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summary));

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

    private static IEmailContentRenderer RendererReturning(EmailContentRendering rendering)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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
            new EmailBodyRepresentation(plainText, plainText.Length, WasTruncated: false),
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
