// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Content;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.Tools.Senders;
using MailFathom.Mcp.Tools.Summaries;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

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
            attachments: [AttachmentOf("invoice.pdf", "application/pdf", decodedSizeOctets: 2048)],
            inlineResourceCount: 1,
            carriesUnverifiedSignature: true);
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(SummaryOf(sentAt: sentAt, receivedAt: receivedAt, observedAt: observedAt)),
            new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(rendering)));

        // Act
        var result = await tool.GetEmailContentAsync(
            [storedEmailId.ToString()],
            includeAttachmentDownloadLinks: true,
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

    /// <summary>An email whose displayed author failed while another domain authenticated is published as exactly that.</summary>
    /// <remarks>
    /// The case the whole verdict exists for. A delivery provider's signature verified, so the transport authenticated
    /// and an unrelated domain is named; the displayed author failed under its own published policy. The read publishes
    /// the conclusion and both domains, and the listing publishes the same conclusion for the same message. What makes
    /// this the spoofed case is the verdict rather than the two domains differing — the test below is the same
    /// difference on mail that authenticated.
    /// </remarks>
    [Fact]
    public async Task GetEmailContentAsync_DisplayedAuthorFailedWhileAnotherDomainAuthenticated_PublishesBothDomainsAndTheFailedVerdict()
    {
        // Arrange
        var summary = SummaryOf(
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Failed,
                DeploymentTrust = SenderTrustLevel.Unknown,
            },
            senderAuthenticationEvidence: new SenderAuthenticationEvidence
            {
                AuthenticatedDomain = DomainOf("delivery.example.test"),
                DisplayedAuthorDomain = DomainOf("bank.example.test"),
                AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
                Dmarc = DmarcOutcome.Fail,
            });
        var tool = ToolOver(new StubStoredEmailSummaryReader(summary));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(AuthorAuthenticationState.Failed, content.SenderVerification.AuthorAuthentication);
        Assert.Equal(DeploymentTrustState.Unknown, content.SenderVerification.DeploymentTrust);
        Assert.Equal("DELIVERY.EXAMPLE.TEST", content.Headers.SenderAuthentication.AuthenticatedDomain);
        Assert.Equal("BANK.EXAMPLE.TEST", content.Headers.SenderAuthentication.DisplayedAuthorDomain);
        Assert.Equal(SenderAuthenticationCheck.Dkim, content.Headers.SenderAuthentication.AuthenticatedBy);
        Assert.Equal(DmarcResult.Fail, content.Headers.SenderAuthentication.Dmarc);
        Assert.Equal(ListedVerdictOf(summary), content.SenderVerification);
    }

    /// <summary>Two different domains on an authenticated email are published as they are, with the verdict unchanged.</summary>
    /// <remarks>
    /// The authenticated domain is whichever identity authenticated the transport, and DKIM is kept where both checks
    /// produced one — so a provider that signs as itself while the envelope sender passes for the author's own domain
    /// publishes two domains that differ on mail that is authenticated exactly as it appears. Nothing on the read path
    /// compares them or lets the difference reach the verdict, which is what the published descriptions promise.
    /// </remarks>
    [Fact]
    public async Task GetEmailContentAsync_AuthenticatedAuthorRelayedByAnotherDomain_PublishesBothDomainsWithoutWeakeningTheVerdict()
    {
        // Arrange
        var summary = SummaryOf(
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            },
            senderAuthenticationEvidence: new SenderAuthenticationEvidence
            {
                AuthenticatedDomain = DomainOf("delivery.example.test"),
                DisplayedAuthorDomain = DomainOf("bank.example.test"),
                AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
                Dmarc = DmarcOutcome.Pass,
            });
        var tool = ToolOver(new StubStoredEmailSummaryReader(summary));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(AuthorAuthenticationState.Authenticated, content.SenderVerification.AuthorAuthentication);
        Assert.Equal(DeploymentTrustState.Trusted, content.SenderVerification.DeploymentTrust);
        Assert.Equal("DELIVERY.EXAMPLE.TEST", content.Headers.SenderAuthentication.AuthenticatedDomain);
        Assert.Equal("BANK.EXAMPLE.TEST", content.Headers.SenderAuthentication.DisplayedAuthorDomain);
        Assert.Equal(DmarcResult.Pass, content.Headers.SenderAuthentication.Dmarc);
    }

    /// <summary>An authenticated author nobody has named is published as unknown rather than as anything against it.</summary>
    [Fact]
    public async Task GetEmailContentAsync_AuthenticatedAuthorOnNoTrustList_PublishesTrustUnknownBesideTheAuthentication()
    {
        // Arrange
        var summary = SummaryOf(
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Unknown,
            });
        var tool = ToolOver(new StubStoredEmailSummaryReader(summary));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(AuthorAuthenticationState.Authenticated, content.SenderVerification.AuthorAuthentication);
        Assert.Equal(DeploymentTrustState.Unknown, content.SenderVerification.DeploymentTrust);
        Assert.Equal(ListedVerdictOf(summary), content.SenderVerification);
    }

    /// <summary>Mail stored before the verdict was recorded is published as it is stored.</summary>
    /// <remarks>
    /// The stored default is what such a row holds, so it is published rather than replaced with a state saying the
    /// value is unfilled. The domains are absent for the same reason, which is an ordinary outcome rather than a gap.
    /// </remarks>
    [Fact]
    public async Task GetEmailContentAsync_EmailStoredBeforeTheVerdictWasRecorded_PublishesTheStoredDefault()
    {
        // Arrange
        var summary = SummaryOf();
        var tool = ToolOver(new StubStoredEmailSummaryReader(summary));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(AuthorAuthenticationState.NotEstablished, content.SenderVerification.AuthorAuthentication);
        Assert.Equal(DeploymentTrustState.Unknown, content.SenderVerification.DeploymentTrust);
        Assert.Null(content.Headers.SenderAuthentication.AuthenticatedDomain);
        Assert.Null(content.Headers.SenderAuthentication.DisplayedAuthorDomain);
        Assert.Equal(SenderAuthenticationCheck.None, content.Headers.SenderAuthentication.AuthenticatedBy);
        Assert.Equal(DmarcResult.NotReported, content.Headers.SenderAuthentication.Dmarc);
    }

    /// <summary>An email whose raw MIME was never stored still carries the verdict its row holds, and its evidence.</summary>
    /// <remarks>
    /// The narrower headers such a read produces come from the row rather than from a parse, and so do the verdict and
    /// the evidence — so a caller reading an oversized message is told the same thing about its author as a listing
    /// tells them, and told what that answer rests on.
    /// </remarks>
    [Fact]
    public async Task GetEmailContentAsync_EmailStoredWithoutItsContent_StillPublishesTheStoredVerdictAndItsEvidence()
    {
        // Arrange
        var summary = SummaryOf(
            contentAvailability: StoredEmailContentAvailability.ExceededSizeLimit,
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            },
            senderAuthenticationEvidence: new SenderAuthenticationEvidence
            {
                AuthenticatedDomain = DomainOf("bank.example.test"),
                DisplayedAuthorDomain = DomainOf("bank.example.test"),
                AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
                Dmarc = DmarcOutcome.Pass,
            });
        var tool = ToolOver(new StubStoredEmailSummaryReader(summary));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailabilityState.NotStoredExceededSizeLimit, content.Body.Availability);
        Assert.Equal(AuthorAuthenticationState.Authenticated, content.SenderVerification.AuthorAuthentication);
        Assert.Equal(DeploymentTrustState.Trusted, content.SenderVerification.DeploymentTrust);
        Assert.Equal("BANK.EXAMPLE.TEST", content.Headers.SenderAuthentication.AuthenticatedDomain);
        Assert.Equal("BANK.EXAMPLE.TEST", content.Headers.SenderAuthentication.DisplayedAuthorDomain);
        Assert.Equal(SenderAuthenticationCheck.Dkim, content.Headers.SenderAuthentication.AuthenticatedBy);
        Assert.Equal(DmarcResult.Pass, content.Headers.SenderAuthentication.Dmarc);
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

    /// <summary>A body the scan ceiling ended is one no call returns more of, which a caller can only act on if it is told.</summary>
    [Fact]
    public async Task GetEmailContentAsync_BodyCutByTheScanCeiling_PublishesThatBoundRatherThanACallLimit()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(plainText: new EmailBodyRepresentation(
                        "The inv",
                        41_000,
                        EmailBodyTruncation.SensitiveContentScanCeiling)))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            EmailBodyTruncationCause.SensitiveContentScanCeiling,
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
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailabilityState.NotStoredExceededSizeLimit, content.Body.Availability);
        Assert.Empty(content.Body.PlainText.Text);
        Assert.Empty(content.Attachments);
        Assert.Null(content.AttachmentCounts);
        Assert.Equal(0, contentStore.ReadCount);
    }

    /// <summary>An email whose content storage had no room yet reports a state a caller can come back to.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailAwaitingStorageHeadroom_PublishesThatItsContentIsNotStoredYet()
    {
        // Arrange
        var contentStore = new StubEmailContentStore(IntactContent());
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(
                SummaryOf(contentAvailability: StoredEmailContentAvailability.AwaitingStorageHeadroom)),
            contentStore: contentStore);

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        Assert.Equal(EmailBodyAvailabilityState.NotStoredAwaitingStorageHeadroom, content.Body.Availability);
        Assert.Empty(content.Body.PlainText.Text);
        Assert.Equal(0, contentStore.ReadCount);
    }

    /// <summary>
    /// An ordinary read describes every attachment and mints nothing, so a caller can tell what a file is before it
    /// decides whether the file is worth a capability. <c>notRequested</c> is what separates that from a deployment
    /// that mints none at all.
    /// </summary>
    [Fact]
    public async Task GetEmailContentAsync_AttachmentLinksNotRequested_DescribesTheAttachmentAndPublishesNoLink()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments:
                        [
                            AttachmentOf("medical-results.pdf", "application/pdf", decodedSizeOctets: 2048),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var content = ContentOf(Assert.Single(result.Emails));
        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("medical-results.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.Equal(EmailAttachmentDownloadState.NotRequested, attachment.DownloadState);
        Assert.Null(attachment.DownloadUrl);
        Assert.Null(attachment.DownloadExpiresAt);

        Assert.NotNull(content.AttachmentCounts);
        Assert.Equal(1, content.AttachmentCounts.AttachmentCount);
        Assert.Equal(2048, content.AttachmentCounts.TotalSizeBytes);
    }

    /// <summary>An email carrying nothing attached says so under either setting, so absence is never guessed at.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEmailContentAsync_EmailWithNoAttachments_PublishesZeroUnderEitherSetting(
        bool includeAttachmentDownloadLinks)
    {
        // Arrange
        var tool = ToolOver();

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDownloadLinks: includeAttachmentDownloadLinks,
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
                            AttachmentOf("invoice.pdf", "application/pdf", decodedSizeOctets: 2048),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [.. Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7().ToString())],
            includeAttachmentDownloadLinks: true,
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
                            AttachmentOf("../../etc/passwd", "application/octet-stream", decodedSizeOctets: 12),
                        ]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments);
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
                        attachments: [AttachmentOf(fileName: null, "image/png", decodedSizeOctets: 64)]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments);
        Assert.Null(attachment.FileName);
        Assert.False(attachment.WasFileNameNormalized);
    }

    /// <summary>A caller that asked for the files receives an address for each of them, with the instant it dies.</summary>
    [Fact]
    public async Task GetEmailContentAsync_AttachmentLinksRequested_PublishesTheAddressAndItsExpiry()
    {
        // Arrange
        var summary = SummaryOf();
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(summary),
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments: [AttachmentOf("invoice.pdf", "application/pdf", decodedSizeOctets: 16)]))));

        // Act
        var result = await tool.GetEmailContentAsync(
            [summary.StoredEmailId.Value.ToString()],
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments);
        Assert.Equal(EmailAttachmentDownloadState.Issued, attachment.DownloadState);
        Assert.Equal(
            $"{StubAttachmentDownloadLinkIssuer.AddressPrefix}{summary.StoredEmailId.Value:N}-0",
            attachment.DownloadUrl);
        Assert.Equal(StubAttachmentDownloadLinkIssuer.Expiry, attachment.DownloadExpiresAt);
    }

    /// <summary>
    /// A deployment that mints no links says so on the attachment rather than answering as though nobody asked. The two
    /// lead a caller to different actions: asking again helps with one and can never help with the other.
    /// </summary>
    [Fact]
    public async Task GetEmailContentAsync_DeploymentIssuesNoLinks_DescribesTheFileAndPublishesUnavailable()
    {
        // Arrange
        var tool = ToolOver(
            renderer: new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(
                    RenderingOf(
                        attachments:
                        [
                            AttachmentOf("archive.zip", "application/zip", decodedSizeOctets: 64 * 1024 * 1024),
                        ]))),
            linkIssuer: new StubAttachmentDownloadLinkIssuer(canIssueLinks: false));

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            includeAttachmentDownloadLinks: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(ContentOf(Assert.Single(result.Emails)).Attachments);
        Assert.Equal(EmailAttachmentDownloadState.Unavailable, attachment.DownloadState);
        Assert.Null(attachment.DownloadUrl);
        Assert.Null(attachment.DownloadExpiresAt);

        // The description survives, which is what tells a caller what it cannot reach.
        Assert.Equal("archive.zip", attachment.FileName);
        Assert.Equal(64 * 1024 * 1024, attachment.SizeBytes);
    }

    /// <summary>
    /// Proves structurally rather than result by result that nothing reachable from the published contract can hold raw
    /// bytes or a stream, which an assertion per test would only establish for the responses someone remembered to
    /// check. This is the guarantee the whole capability rests on: an attachment is fetched over HTTP, so a response
    /// that could carry octets would be a second path to the same file with none of the bounds that one has.
    /// </summary>
    [Fact]
    public void GetEmailContentToolResult_NoPublishedProperty_CanHoldRawBytesOrAStream()
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

    /// <summary>
    /// No published property carries an encoded payload either. A string is the one shape the check above cannot see
    /// through, and base64 in a string is exactly how attachment content used to travel, so a property named for one is
    /// the shape this contract must never grow back.
    /// </summary>
    [Fact]
    public void GetEmailContentToolResult_NoPublishedProperty_CarriesAnEncodedPayload()
    {
        // Arrange, Act
        var encodedPayloadProperties = PublishedProperties(typeof(GetEmailContentToolResult), [])
            .Where(property => property.Name.Contains("Base64", StringComparison.Ordinal))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}");

        // Assert
        Assert.Empty(encodedPayloadProperties);
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

    /// <summary>Walks every property the published contract reaches, in the order the traversal finds them.</summary>
    private static IReadOnlyList<PropertyInfo> PublishedProperties(Type publishedType, HashSet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(publishedType))
        {
            return [];
        }

        PropertyInfo[] properties = [.. publishedType.GetProperties()];

        return
        [
            .. properties,
            .. properties
                .Select(property => ElementTypeOf(property.PropertyType))
                .Where(propertyType => propertyType.Assembly == typeof(GetEmailContentToolResult).Assembly)
                .SelectMany(propertyType => PublishedProperties(propertyType, visitedTypes)),
        ];
    }

    private static Type ElementTypeOf(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? type.GetGenericArguments()[0]
            : type;

    /// <summary>Publishes one summary the way a listing would, so a read's verdict can be compared with a listing's.</summary>
    /// <remarks>
    /// The mapping the listing tool itself uses rather than a restatement of it, which is what makes the comparison a
    /// claim about the two tools agreeing rather than about this test's own arithmetic.
    /// </remarks>
    private static ReportedSenderVerification ListedVerdictOf(EmailSummary summary) =>
        ListedEmailSummary.From(summary, PublishedAccountNames.From(new StubMailAccountCatalog(ServedAccountId)))
            .SenderVerification;

    private static SenderDomain DomainOf(string value)
    {
        Assert.True(SenderDomain.TryCreate(value, out var domain));

        return domain;
    }

    private static RetrievedEmailContent ContentOf(RetrievedEmail email) =>
        email.Content ?? throw new InvalidOperationException(
            $"The email was not served: {email.Failure?.Message}");

    private static RetrievedEmailFailure FailureOf(RetrievedEmail email) =>
        email.Failure ?? throw new InvalidOperationException("The email was served rather than refused.");

    /// <summary>Which of the two the caller meant is theirs to say, so a call carrying both is refused outright.</summary>
    [Fact]
    public async Task GetEmailContentAsync_BothEmailsAndAConversation_IsRefusedWithoutReadingAnything()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadSelectionInvalidException>(
            () => tool.GetEmailContentAsync(
                [Guid.CreateVersion7().ToString()],
                Guid.CreateVersion7().ToString(),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>
    /// An empty list is still a list the caller sent, so the call named both and is refused for that rather than for a
    /// count: reporting the list as too short would answer a question about a selection the caller never made.
    /// </summary>
    [Fact]
    public async Task GetEmailContentAsync_AnEmptyEmailListBesideAConversation_IsRefusedAsBothRatherThanAsTooFewEmails()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadSelectionInvalidException>(
            () => tool.GetEmailContentAsync(
                [],
                Guid.CreateVersion7().ToString(),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    [Fact]
    public async Task GetEmailContentAsync_NeitherEmailsNorAConversation_IsRefusedWithoutReadingAnything()
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<EmailContentReadSelectionInvalidException>(
            () => tool.GetEmailContentAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>The refused text is the caller's own input on its way into a client-readable result, so it is not repeated back.</summary>
    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetEmailContentAsync_ConversationIdentifierThisSystemNeverIssued_IsRefusedWithoutReadingAnything(
        string threadId)
    {
        // Arrange
        var summaryReader = new StubStoredEmailSummaryReader(SummaryOf());
        var tool = ToolOver(summaryReader);

        // Act
        var failure = await Assert.ThrowsAsync<EmailThreadIdentifierMalformedException>(
            () => tool.GetEmailContentAsync(
                threadId: threadId,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailThreadIdentifierMalformed, failure.ErrorCode);
        Assert.DoesNotContain(threadId, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, summaryReader.ReadCount);
    }

    /// <summary>A conversation longer than one read serves comes back bounded, and names what it could not carry.</summary>
    [Fact]
    public async Task GetEmailContentAsync_ConversationLongerThanOneRead_PublishesTheBatchAndNamesTheRest()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());
        var conversation = ConversationOf(GetEmailContentRequest.MaximumEmails + 2);
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(SummaryOf() with { ThreadId = threadId }),
            threadReader: ThreadReaderOver(threadId, conversation));

        // Act
        var result = await tool.GetEmailContentAsync(
            threadId: threadId.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            conversation.Take(GetEmailContentRequest.MaximumEmails).Select(message => message.StoredEmailId.ToString()),
            result.Emails.Select(email => email.StoredEmailId));
        Assert.Equal(
            conversation.Skip(GetEmailContentRequest.MaximumEmails).Select(message => message.StoredEmailId.ToString()),
            result.UnreadThreadMessages);
    }

    /// <summary>Every published email carries its conversation, with the other messages named rather than reproduced.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailOfAConversation_PublishesThatConversationBesideIt()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());
        var conversation = ConversationOf(count: 2);
        var tool = ToolOver(
            new StubStoredEmailSummaryReader(SummaryOf() with { ThreadId = threadId }),
            threadReader: ThreadReaderOver(threadId, conversation));

        // Act
        var result = await tool.GetEmailContentAsync(
            [conversation[1].StoredEmailId.ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var thread = ContentOf(Assert.Single(result.Emails)).Thread;

        Assert.NotNull(thread);
        Assert.Equal(threadId.ToString(), thread.ThreadId);
        Assert.Equal(1, thread.Position);
        Assert.Equal(conversation[0].StoredEmailId.ToString(), thread.InReplyToStoredEmailId);
        Assert.Equal(2, thread.MessageCount);
        Assert.False(thread.MoreMessagesNotNamed);

        var named = Assert.Single(thread.OtherMessages);
        Assert.Equal(conversation[0].StoredEmailId.ToString(), named.StoredEmailId);
        Assert.Equal(conversation[0].Subject, named.Subject);
        Assert.Equal(conversation[0].SenderAddress, named.SenderAddress);
    }

    /// <summary>An email belonging to no conversation publishes nothing about one rather than an empty conversation.</summary>
    [Fact]
    public async Task GetEmailContentAsync_EmailInNoConversation_PublishesNothingAboutOne()
    {
        // Arrange
        var tool = ToolOver();

        // Act
        var result = await tool.GetEmailContentAsync(
            [Guid.CreateVersion7().ToString()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ContentOf(Assert.Single(result.Emails)).Thread);
        Assert.Empty(result.UnreadThreadMessages);
    }

    /// <summary>Builds a conversation as a chain, one message a minute apart, all of it in the served inbox.</summary>
    private static ThreadedEmailSummary[] ConversationOf(int count) =>
    [
        .. Enumerable.Range(0, count).Aggregate(
            new List<ThreadedEmailSummary>(count),
            (chain, ordinal) =>
            {
                chain.Add(new ThreadedEmailSummary
                {
                    StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
                    AccountId = MailAccountId.Create(ServedAccountId),
                    FolderAlias = MailFolderAlias.Create("INBOX"),
                    ParentStoredEmailId = ordinal > 0 ? chain[ordinal - 1].StoredEmailId : null,
                    Subject = $"Quarterly invoice {ordinal}",
                    SentAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero).AddMinutes(ordinal),
                    SenderAddress = "billing@example.test",
                });

                return chain;
            }),
    ];

    private static StubEmailThreadReader ThreadReaderOver(EmailThreadId threadId, ThreadedEmailSummary[] conversation) =>
        new([.. conversation.Select(message => (threadId, message))]);

    private static GetEmailContentTool ToolOver(
        StubStoredEmailSummaryReader? summaryReader = null,
        StubEmailContentRenderer? renderer = null,
        StubEmailContentStore? contentStore = null,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IAttachmentDownloadLinkIssuer? linkIssuer = null,
        IEmailThreadReader? threadReader = null) => new(
        new EmailContentReader(
            summaryReader ?? new StubStoredEmailSummaryReader(SummaryOf()),
            threadReader ?? new StubEmailThreadReader(),
            contentStore ?? new StubEmailContentStore(IntactContent()),
            renderer ?? new StubEmailContentRenderer(EmailContentRenderingResult.Rendered(RenderingOf())),
            repairRequestStore ?? Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                new StubMailAccountCatalog(ServedAccountId),
                MappedInbox,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            linkIssuer ?? new StubAttachmentDownloadLinkIssuer(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            Substitute.For<IMailboxReadTelemetry>(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead)));

    /// <summary>The one folder this deployment maps, which is what makes the mail these tests store readable at all.</summary>
    /// <remarks>
    /// A folder no mapping names does not exist as far as MailFathom is concerned, so a tool arranged over an unmapped
    /// alias answers every read with a refusal. Every summary here is stored in the inbox, so the mapping is one entry.
    /// </remarks>
    private static StubMailFolderParticipation MappedInbox => StubMailFolderParticipation.Mapping(
        new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("INBOX")));

    private static EmailSummary SummaryOf(
        DateTimeOffset? sentAt = null,
        DateTimeOffset? receivedAt = null,
        DateTimeOffset? observedAt = null,
        string accountId = ServedAccountId,
        StoredEmailContentAvailability contentAvailability = StoredEmailContentAvailability.Available,
        SenderVerification? senderVerification = null,
        SenderAuthenticationEvidence? senderAuthenticationEvidence = null) => new()
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
                    IsDeleted: false,
                    Keywords: RemoteEmailKeywords.None)
                : RemoteEmailFlagSnapshot.NeverObserved,
            SenderVerification = senderVerification ?? SenderVerification.NotEstablished,
            SenderAuthenticationEvidence = senderAuthenticationEvidence ?? SenderAuthenticationEvidence.None,
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
            containsUnexpandedTnefPart: false),
        attachments ?? []);

    /// <summary>Builds the description of one attachment a read produced.</summary>
    private static ExtractedEmailAttachment AttachmentOf(
        string? fileName,
        string mediaType,
        long decodedSizeOctets) =>
        new(fileName is null ? null : AttachmentFileNameOf(fileName), mediaType, decodedSizeOctets);

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
