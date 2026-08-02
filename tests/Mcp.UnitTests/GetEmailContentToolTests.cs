// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Covers what the <c>get_email_content</c> tool itself owns: naming emails and publishing what was read.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="EmailContentReader" /> rather than a substitute for it, because the use case is
/// where the authorization, the integrity check, the character budget, and the repair request live, and a substitute
/// would only prove that the tool composes with a fiction. What the stubs replace is storage and the parse, the
/// boundaries below the use case.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a refusal never reaches storage, and no
/// failure message carries the text that was refused. Both hold for every path through the boundary.
/// </para>
/// </remarks>
public sealed class GetEmailContentToolTests
{
    private const string ServedAccountId = "personal";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    [Fact]
    public async Task GetEmailContentAsync_ReadableEmail_PublishesEveryFieldOfTheResult()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7();
        var sentAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var receivedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 5, TimeSpan.Zero);
        var observedAt = new DateTimeOffset(2026, 3, 2, 6, 0, 0, TimeSpan.Zero);
        var rendering = RenderingOf(
            headers: new EmailContentHeaders(
                "Quarterly invoice",
                sentAt,
                receivedAt,
                [
                    ParticipantOf(EmailAddressRole.From, "Accounts Payable", "billing@example.test"),
                    ParticipantOf(EmailAddressRole.To, displayName: null, "finance@example.test"),
                ],
                EmailThreadReferences.Create("abc@example.test", "root@example.test", ["root@example.test"])),
            plainText: new EmailBodyRepresentation("Please find the invoice attached.", 33, EmailBodyTruncation.None),
            attachments: [new ExtractedEmailAttachment(AttachmentFileNameOf("invoice.pdf"), "application/pdf", DecodedSizeOctets: 2048)],
            inlineResourceCount: 1,
            carriesUnverifiedSignature: true);
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(SummaryOf(sentAt: sentAt, receivedAt: receivedAt, observedAt: observedAt)),
            new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(rendering)));

        // Act
        var result = await tool.GetEmailContentAsync(
            [storedEmailId.ToString()],
            includeAttachmentDetails: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var email = Assert.Single(result.Emails);
        Assert.Equal(storedEmailId.ToString(), email.StoredEmailId);
        Assert.Null(email.Failure);
        var content = Assert.IsType<RetrievedEmailContent>(email.Content);
        Assert.Equal(ServedAccountId, content.AccountId);
        Assert.Equal("INBOX", content.FolderAlias);
        Assert.Equal(4096, content.SizeBytes);
        Assert.Equal("Quarterly invoice", content.Headers.Subject);
        Assert.Equal(sentAt, content.Headers.SentAt);
        Assert.Equal(receivedAt, content.Headers.ReceivedAt);
        Assert.Equal(
            [(EmailHeaderRole.From, "billing@example.test", "Accounts Payable"), (EmailHeaderRole.To, "finance@example.test", null)],
            [.. content.Headers.Participants.Select(participant => (participant.Role, participant.Address, participant.DisplayName))]);
        Assert.Equal("abc@example.test", content.Headers.MessageId);
        Assert.Equal("root@example.test", content.Headers.InReplyTo);
        Assert.Equal(["root@example.test"], content.Headers.References);
        Assert.Equal(EmailBodyAvailabilityState.Readable, content.Body.Availability);
        Assert.Equal("Please find the invoice attached.", content.Body.PlainText.Text);
        Assert.Equal(33, content.Body.PlainText.OriginalCharacterCount);
        Assert.Equal(EmailBodyTruncationCause.None, content.Body.PlainText.TruncatedBy);
        Assert.Null(content.Body.SanitizedHtml);
        Assert.NotNull(content.Attachments);
        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("invoice.pdf", attachment.FileName);
        Assert.False(attachment.WasFileNameNormalized);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.NotNull(content.AttachmentCounts);
        Assert.Equal(1, content.AttachmentCounts.AttachmentCount);
        Assert.Equal(2048, content.AttachmentCounts.TotalSizeBytes);
        Assert.Equal(1, content.AttachmentCounts.InlineResourceCount);
        Assert.False(content.AttachmentCounts.IsEncrypted);
        Assert.True(content.AttachmentCounts.CarriesUnverifiedSignature);
        Assert.False(content.AttachmentCounts.ContainsUnexpandedTnefPart);
        Assert.True(content.RemoteFlags.Seen);
        Assert.Equal(observedAt, content.RemoteFlags.ObservedAt);
        Assert.True(content.RemoteFlags.WasObserved);
    }

    /// <summary>A body and the fact that it is incomplete are never useful apart, so the second travels inside the first.</summary>
    [Fact]
    public async Task GetEmailContentAsync_BodyLongerThanTheBound_PublishesTheTruncationBesideTheText()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(plainText: new EmailBodyRepresentation("The invoice beg", 41_000, EmailBodyTruncation.BodyCharacterLimit)))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var plainText = ContentOf(Assert.Single(result.Emails)).Body.PlainText;
        Assert.Equal(EmailBodyTruncationCause.BodyCharacterLimit, plainText.TruncatedBy);
        Assert.Equal(41_000, plainText.OriginalCharacterCount);
        Assert.Equal("The invoice beg", plainText.Text);
    }

    /// <summary>Splitting the call is only worth suggesting when the call's own budget is what cut the body.</summary>
    [Fact]
    public async Task GetEmailContentAsync_BodyCutByTheReadsBudget_PublishesThatBoundRatherThanTheBodyBound()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(plainText: new EmailBodyRepresentation("The inv", 41_000, EmailBodyTruncation.ReadCharacterBudget)))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            EmailBodyTruncationCause.ReadCharacterBudget,
            ContentOf(Assert.Single(result.Emails)).Body.PlainText.TruncatedBy);
    }

    [Fact]
    public async Task GetEmailContentAsync_SanitizedHtmlRequested_AsksForItAndPublishesItWithItsOwnTruncation()
    {
        // Arrange
        var renderer = new StubEmailContentRenderer(
            EmailContentRenderingResult.Rendered(
                RenderingOf(sanitizedHtml: new EmailBodyRepresentation("<p>Invoice</p>", 12_000, EmailBodyTruncation.BodyCharacterLimit))));
        var tool = ToolOver(renderer: renderer);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeSanitizedHtml: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(renderer.LastIncludeSanitizedHtml);
        var body = ContentOf(Assert.Single(result.Emails)).Body;
        Assert.NotNull(body.SanitizedHtml);
        Assert.Equal("<p>Invoice</p>", body.SanitizedHtml.Text);
        Assert.Equal(EmailBodyTruncationCause.BodyCharacterLimit, body.SanitizedHtml.TruncatedBy);
        Assert.Equal(EmailBodyTruncationCause.None, body.PlainText.TruncatedBy);
    }

    /// <summary>The markup costs a sanitization pass, so it is produced only for a caller that asked for it.</summary>
    [Fact]
    public async Task GetEmailContentAsync_SanitizedHtmlNotRequested_AsksForPlainTextAlone()
    {
        // Arrange
        var renderer = new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(RenderingOf()));
        var tool = ToolOver(renderer: renderer);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(renderer.LastIncludeSanitizedHtml);
        Assert.Null(ContentOf(Assert.Single(result.Emails)).Body.SanitizedHtml);
    }

    /// <summary>"The caller did not want HTML" and "this message has no HTML" must not be reported as the same thing.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailCarryingNoHtmlPart_PublishesNoneThoughItWasRequested()
    {
        // Arrange
        var tool = ToolOver(renderer: new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(RenderingOf())));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeSanitizedHtml: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var body = ContentOf(Assert.Single(result.Emails)).Body;
        Assert.Null(body.SanitizedHtml);
        Assert.Equal(EmailBodyAvailabilityState.Readable, body.Availability);
    }

    /// <summary>Mail this deployment cannot decrypt is stated as such, so an empty body is never read as an empty message.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EncryptedEmail_PublishesTheNotReadableStateRatherThanAnEmptyBody()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(RenderingOf(bodyIsEncrypted: true))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailabilityState.EncryptedNotReadableLocally, content.Body.Availability);
        Assert.Empty(content.Body.PlainText.Text);
        Assert.NotNull(content.AttachmentCounts);
        Assert.True(content.AttachmentCounts.IsEncrypted);
    }

    /// <summary>An email the size limit kept out of storage reports why, and reports no count nothing ever established.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailStoredWithoutItsContent_PublishesTheSizeLimitStateAndCountsNothing()
    {
        // Arrange
        var contentStore = new StubEmailContentStore(IntactContent());
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(
                SummaryOf(contentAvailability: StoredEmailContentAvailability.ExceededSizeLimit)),
            contentStore: contentStore);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDetails: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailabilityState.NotStoredExceededSizeLimit, content.Body.Availability);
        Assert.Empty(content.Body.PlainText.Text);
        Assert.Empty(content.Attachments ?? []);
        Assert.Null(content.AttachmentCounts);
        Assert.Equal(0, contentStore.ReadCount);
    }

    /// <summary>A file name is sender-chosen mail content, so an ordinary read is told how many attachments there are and not what they are called.</summary>
    [Fact]
    public async Task GetEmailContentAsync_AttachmentDetailsNotRequested_PublishesTheCountsAndNoAttachmentList()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments:
                        [
                            new ExtractedEmailAttachment(AttachmentFileNameOf("medical-results.pdf"), "application/pdf", DecodedSizeOctets: 2048),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Null(content.Attachments);
        Assert.NotNull(content.AttachmentCounts);
        Assert.Equal(1, content.AttachmentCounts.AttachmentCount);
        Assert.Equal(2048, content.AttachmentCounts.TotalSizeBytes);
    }

    /// <summary>An email carrying nothing attached says so under either setting, so absence is never guessed at.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEmailContentAsync_EmailWithNoAttachments_PublishesZeroUnderEitherSetting(
        bool includeAttachmentDetails)
    {
        // Arrange
        var tool = ToolOver();

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDetails: includeAttachmentDetails,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.NotNull(content.AttachmentCounts);
        Assert.Equal(0, content.AttachmentCounts.AttachmentCount);
    }

    /// <summary>One flag governs the whole call, so a caller never receives descriptions for only part of what it named.</summary>
    [Fact]
    public async Task GetEmailContentAsync_AttachmentDetailsRequestedForSeveralEmails_DescribesEveryOneOfThem()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments:
                        [
                            new ExtractedEmailAttachment(AttachmentFileNameOf("invoice.pdf"), "application/pdf", DecodedSizeOctets: 2048),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [.. Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7().ToString())],
            includeAttachmentDetails: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 1, 1], result.Emails.Select(email => ContentOf(email).Attachments?.Count));
    }

    /// <summary>An email that is not here is one entry refused, not a call refused, so what could be read still comes back.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailThisMailboxCopyDoesNotHold_ReportsItInPlaceWithoutReadingItsContent()
    {
        // Arrange
        var contentStore = new StubEmailContentStore(IntactContent());
        var tool = ToolOver(new StubStoredEmailSummaryReader(), contentStore: contentStore);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound.Value, failure.Code);
        Assert.Equal(0, contentStore.ReadCount);
    }

    /// <summary>"No such email" and "not yours" are deliberately one answer, so a read cannot discover another mailbox's identifiers.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailOfAnAccountThisDeploymentDoesNotServe_IsReportedAsNotFound()
    {
        // Arrange
        var contentStore = new StubEmailContentStore(IntactContent());
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(SummaryOf(accountId: "someone-elses")),
            contentStore: contentStore);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound.Value, FailureOf(Assert.Single(result.Emails)).Code);
        Assert.Equal(0, contentStore.ReadCount);
    }

    /// <summary>Emails come back paired with what was asked for, in the order it was asked for.</summary>
    [Fact]
    public async Task GetEmailContentAsync_SeveralEmails_PublishesThemInTheOrderTheyWereNamed()
    {
        // Arrange
        var namedEmailIds = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7().ToString()).ToArray();
        var tool = ToolOver();

        // Act
        var result = await tool.GetEmailContentAsync(
            namedEmailIds,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(namedEmailIds, result.Emails.Select(email => email.StoredEmailId));
    }

    /// <summary>One identifier this deployment cannot serve must not discard the content of the others.</summary>
    [Fact]
    public async Task GetEmailContentAsync_OneUnknownEmailAmongKnownOnes_PublishesTheContentOfTheRest()
    {
        // Arrange
        var known = Enumerable.Range(0, 2).Select(_ => Guid.CreateVersion7()).ToArray();
        var unknown = Guid.CreateVersion7();
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(
                SummaryOf(),
                [.. known.Select(StoredEmailId.Create)]));

        // Act
        var result = await tool.GetEmailContentAsync(
            [known[0].ToString(), unknown.ToString(), known[1].ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Emails[0].Content);
        Assert.Null(result.Emails[1].Content);
        Assert.Equal(MailFathomErrorCode.StoredEmailNotFound.Value, FailureOf(result.Emails[1]).Code);
        Assert.NotNull(result.Emails[2].Content);
    }

    /// <summary>A local copy being repaired must not read as an email that was never stored, so the codes stay apart.</summary>
    [Fact]
    public async Task GetEmailContentAsync_MissingLocalContent_ReportsACodeDistinctFromNotFound()
    {
        // Arrange
        var repairRequests = Substitute.For<IEmailContentRepairRequestStore>();
        var tool = ToolOver(contentStore: new StubEmailContentStore(), repairRequestStore: repairRequests);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.EmailContentUnavailable.Value, failure.Code);
        Assert.NotEqual(MailFathomErrorCode.StoredEmailNotFound.Value, failure.Code);
        await repairRequests.Received(1).RecordAsync(
            Arg.Is<EmailContentRepairRequest>(request => request != null && request.Defect == EmailContentDefect.Missing),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmailContentAsync_DamagedLocalContent_ReportsTheSameConsistencyCode()
    {
        // Arrange
        var tool = ToolOver(
            contentStore: new StubEmailContentStore(
                new StoredEmailContent(StoredRawMime, StoredRawMime.Length, SHA256.HashData([0x01]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var failure = FailureOf(Assert.Single(result.Emails));
        Assert.Equal(MailFathomErrorCode.EmailContentUnavailable.Value, failure.Code);
        Assert.Contains(nameof(EmailContentDefect.HashMismatch), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A file name is attacker-controlled text that reaches a model, so what is published is the repaired form.</summary>
    [Fact]
    public async Task GetEmailContentAsync_AttachmentNamedAsAPath_PublishesTheNormalizedNameAndSaysItWasRepaired()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments:
                        [
                            new ExtractedEmailAttachment(
                                AttachmentFileNameOf("../../etc/passwd"),
                                "application/octet-stream",
                                DecodedSizeOctets: 12),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDetails: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments ?? []);
        Assert.Equal("passwd", attachment.FileName);
        Assert.True(attachment.WasFileNameNormalized);
    }

    /// <summary>An unnamed part is reported as unnamed rather than given a name MailFathom invented.</summary>
    [Fact]
    public async Task GetEmailContentAsync_UnnamedAttachment_PublishesNoFileName()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments: [new ExtractedEmailAttachment(FileName: null, "image/png", DecodedSizeOctets: 64)]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDetails: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments ?? []);
        Assert.Null(attachment.FileName);
        Assert.False(attachment.WasFileNameNormalized);
    }

    /// <summary>
    /// Proves the privacy bound structurally rather than result by result: nothing reachable from the published contract
    /// can hold bytes, so no shape of any response can carry attachment content or raw MIME. An assertion per test would
    /// only cover the responses someone remembered to check.
    /// </summary>
    [Fact]
    public void GetEmailContentToolResult_NoPublishedProperty_CanHoldContentBytes()
    {
        // Arrange
        Type[] byteCarryingTypes =
        [
            typeof(byte[]),
            typeof(Memory<byte>),
            typeof(ReadOnlyMemory<byte>),
            typeof(IReadOnlyList<byte>),
            typeof(Stream),
        ];

        // Act
        var publishedTypes = PublishedPropertyTypes(typeof(GetEmailContentToolResult), []);

        // Assert
        Assert.Empty(publishedTypes.Intersect(byteCarryingTypes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-stored-email")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetEmailContentAsync_IdentifierThisSystemDoesNotIssue_IsRefusedWithoutReading(string unusable)
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.GetEmailContentAsync([unusable], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailIdentifierMalformed, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>One unusable identifier refuses the whole call, because no email was named to report an outcome against.</summary>
    [Fact]
    public async Task GetEmailContentAsync_OneUnusableIdentifierAmongUsableOnes_RefusesTheCallWithoutReadingAny()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.GetEmailContentAsync(
                [Guid.CreateVersion7().ToString(), "not-a-stored-email"],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailIdentifierMalformed, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>
    /// The parse scans whatever it is handed and the caller decides how long that is, so the length is refused before
    /// anything tries to read an identity out of it.
    /// </summary>
    [Fact]
    public async Task GetEmailContentAsync_IdentifierLongerThanAnyUuidForm_IsRefusedWithoutParsingIt()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);
        var overlongIdentifier = $"{Guid.CreateVersion7()}{new string('0', 1024)}";

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.GetEmailContentAsync([overlongIdentifier], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailIdentifierMalformed, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>The count is what decides how much parsing a caller can ask for, so it is checked before the first parse.</summary>
    [Fact]
    public async Task GetEmailContentAsync_MoreEmailsThanTheCallServes_IsRefusedBeforeAnyIdentifierIsParsed()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);
        var namedEmailIds = Enumerable
            .Range(0, GetEmailContentRequest.MaximumEmails + 1)
            .Select(_ => "not-a-stored-email")
            .ToArray();

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadCountOutOfRangeException>(
            () => tool.GetEmailContentAsync(namedEmailIds, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadCountOutOfRange, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>The call is refused rather than truncated, so a caller is never left comparing the answer against its own list.</summary>
    [Fact]
    public async Task GetEmailContentAsync_ExactlyTheGreatestNumberOfEmails_IsServedRatherThanRefused()
    {
        // Arrange
        var tool = ToolOver();
        var namedEmailIds = Enumerable
            .Range(0, GetEmailContentRequest.MaximumEmails)
            .Select(_ => Guid.CreateVersion7().ToString())
            .ToArray();

        // Act
        var result = await tool.GetEmailContentAsync(
            namedEmailIds,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GetEmailContentRequest.MaximumEmails, result.Emails.Count);
    }

    /// <summary>A call naming nothing asks for nothing, and is the same finding about a count as a call naming too much.</summary>
    [Fact]
    public async Task GetEmailContentAsync_NoEmailNamed_IsRefusedWithTheSameCountRefusal()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadCountOutOfRangeException>(
            () => tool.GetEmailContentAsync([], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadCountOutOfRange, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>A repeat is refused rather than served twice, whichever way the caller spelled the second one.</summary>
    [Fact]
    public async Task GetEmailContentAsync_TheSameEmailNamedTwice_IsRefusedWithoutReadingIt()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);
        var repeated = Guid.CreateVersion7();

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadDuplicateEmailException>(
            () => tool.GetEmailContentAsync(
                [repeated.ToString(), Guid.CreateVersion7().ToString(), repeated.ToString().ToUpperInvariant()],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadDuplicateEmail, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>A refused identifier is caller input, and a boundary that echoes input back has started returning content.</summary>
    [Fact]
    public async Task GetEmailContentAsync_IdentifierCarryingText_NamesNoRefusedValue()
    {
        // Arrange
        const string CallerText = "victim@example.test\nINJECTED admin login";
        var tool = ToolOver();

        // Act
        var failure = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.GetEmailContentAsync([CallerText], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("victim@example.test", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The identity the use case is asked about is the one the caller named, whichever case they spelled it in.</summary>
    [Fact]
    public async Task GetEmailContentAsync_IdentifierSpelledInUpperCase_NamesTheSameEmail()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7();
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        await tool.GetEmailContentAsync(
            [storedEmailId.ToString().ToUpperInvariant()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailId.Create(storedEmailId), summaryReader.LastStoredEmailId);
    }

    [Fact]
    public async Task GetEmailContentAsync_CancelledCaller_StopsRatherThanAnsweringFromWhatItHad()
    {
        // Arrange
        var tool = ToolOver();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.GetEmailContentAsync(
                [Guid.CreateVersion7().ToString()],
                cancellationToken: cancellation.Token));
    }

    /// <summary>Collects every property type the published contract can carry, following the types this boundary declares.</summary>
    /// <remarks>
    /// Each level is materialized, because the walk carries the set of types it has already visited: a deferred sequence
    /// would decide what it returns from when it is enumerated rather than from the type it was asked about.
    /// </remarks>
    private static IReadOnlyList<Type> PublishedPropertyTypes(Type publishedType, HashSet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(publishedType))
        {
            return [];
        }

        Type[] propertyTypes = [.. publishedType.GetProperties().Select(property => property.PropertyType)];

        return
        [
            .. propertyTypes,
            .. propertyTypes
                .Select(ElementTypeOf)
                .Where(propertyType => propertyType.Assembly == typeof(GetEmailContentToolResult).Assembly)
                .SelectMany(propertyType => PublishedPropertyTypes(propertyType, visitedTypes)),
        ];
    }

    private static Type ElementTypeOf(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? type.GetGenericArguments()[0]
            : type;

    private static RetrievedEmailContent ContentOf(RetrievedEmail email) =>
        email.Content ?? throw new InvalidOperationException(
            $"The email was not served: {email.Failure?.Message}");

    private static RetrievedEmailFailure FailureOf(RetrievedEmail email) =>
        email.Failure ?? throw new InvalidOperationException("The email was served rather than refused.");

    private static GetEmailContentTool ToolOver(
        StubStoredEmailSummaryReader? summaryReader = null,
        StubEmailContentRenderer? renderer = null,
        StubEmailContentStore? contentStore = null,
        IEmailContentRepairRequestStore? repairRequestStore = null) => new(
        new EmailContentReader(
            summaryReader ?? new StubStoredEmailSummaryReader(SummaryOf()),
            contentStore ?? new StubEmailContentStore(IntactContent()),
            renderer ?? new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(RenderingOf())),
            repairRequestStore ?? Substitute.For<IEmailContentRepairRequestStore>(),
            new StubMailAccountCatalog(ServedAccountId),
            new EmailContentReadOptions()));

    private static EmailSummary SummaryOf(
        DateTimeOffset? sentAt = null,
        DateTimeOffset? receivedAt = null,
        DateTimeOffset? observedAt = null,
        string accountId = ServedAccountId,
        StoredEmailContentAvailability contentAvailability = StoredEmailContentAvailability.Available) => new()
        {
            StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(accountId),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            InternetMessageId = "<abc@example.test>",
            Subject = "Quarterly invoice",
            SenderAddress = "billing@example.test",
            SenderDisplayName = "Accounts Payable",
            ToAddresses = ["finance@example.test"],
            SentAt = sentAt,
            ReceivedAt = receivedAt,
            SizeOctets = 4096,
            Attachments = StoredEmailAttachmentSummary.None,
            ContentAvailability = contentAvailability,
            RemoteFlags = observedAt is { } flagsObservedAt
                ? new RemoteEmailFlagSnapshot(
                    flagsObservedAt,
                    IsSeen: true,
                    IsAnswered: false,
                    IsFlagged: false,
                    IsDraft: false,
                    IsDeleted: false)
                : RemoteEmailFlagSnapshot.NeverObserved,
        };

    private static EmailContentRendering RenderingOf(
        EmailContentHeaders? headers = null,
        EmailBodyRepresentation? plainText = null,
        EmailBodyRepresentation? sanitizedHtml = null,
        bool bodyIsEncrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        int inlineResourceCount = 0,
        bool carriesUnverifiedSignature = false) => new(
        headers ?? new EmailContentHeaders("Quarterly invoice", SentAt: null, ReceivedAt: null, [], EmailThreadReferences.None),
        plainText ?? (bodyIsEncrypted
            ? EmailBodyRepresentation.Empty
            : new EmailBodyRepresentation("Body", 4, EmailBodyTruncation.None)),
        sanitizedHtml,
        bodyIsEncrypted,
        EmailAttachmentSummary.Create(
            attachments ?? [],
            inlineResourceCount,
            bodyIsEncrypted,
            carriesUnverifiedSignature,
            containsUnexpandedTnefPart: false));

    private static EmailParticipant ParticipantOf(EmailAddressRole role, string? displayName, string address) =>
        EmailAddress.TryCreate(displayName, address, out var emailAddress)
            ? new EmailParticipant(role, emailAddress)
            : throw new InvalidOperationException($"'{address}' is not a usable mail address.");

    private static AttachmentFileName AttachmentFileNameOf(string fileName) =>
        AttachmentFileName.TryNormalize(fileName, out var normalized)
            ? normalized
            : throw new InvalidOperationException($"'{fileName}' is not a usable attachment file name.");

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));
}
