// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Cleaning;

/// <summary>
/// Covers the pass a reader waits on: which blocks it keeps, how many times it asks, and what it serves when the answer
/// is unusable. Two properties are asserted throughout rather than in one test of their own — the document that comes
/// back is never empty, and every block in it is the block the reduction produced.
/// </summary>
public sealed class MailBodyCleaningTests
{
    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.Parse("3f1a0d6e-0000-4000-8000-000000000001"));

    [Fact]
    public async Task CleanAsync_AProposalThatPartitionsTheDocument_KeepsExactlyTheBlocksItNamed()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132", "Unsubscribe");
        var cleaner = ScriptedMailBodyCleaner.Keeping(new MailBodyCleaningSegment(0, 0, Keep: false), new MailBodyCleaningSegment(1, 1, Keep: true), new MailBodyCleaningSegment(2, 2, Keep: false));

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.Cleaned, cleaned!.Outcome);
        Assert.Equal([document.Blocks[1]], cleaned.Document!.Blocks);
    }

    /// <summary>
    /// The one retry. A first answer the reading refuses is asked again, and the second answer is the one served — which
    /// is what stops a producer's single misnumbered turn from costing the reader the view.
    /// </summary>
    [Fact]
    public async Task CleanAsync_AFirstAnswerTheReadingRefuses_AsksOnceMoreAndServesTheSecond()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Proposing(
            MailBodyCleaningProposal.Proposing([new MailBodyCleaningSegment(1, 1, Keep: true)]),
            MailBodyCleaningProposal.Proposing(
                [new MailBodyCleaningSegment(0, 0, Keep: false), new MailBodyCleaningSegment(1, 1, Keep: true)]));

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.Cleaned, cleaned!.Outcome);
        Assert.Equal([document.Blocks[1]], cleaned.Document!.Blocks);
        Assert.Equal(2, cleaner.Asked.Count);
    }

    /// <summary>The retry is one, not as many as it takes, because the reader is standing in front of the message while it runs.</summary>
    [Fact]
    public async Task CleanAsync_TwoAnswersTheReadingRefuses_StopsAskingAndServesTheReducedDocument()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var renumbered = MailBodyCleaningProposal.Proposing([new MailBodyCleaningSegment(0, 0, Keep: true)]);
        var cleaner = ScriptedMailBodyCleaner.Proposing(renumbered, renumbered);

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.AnswerRejected, cleaned!.Outcome);
        Assert.Equal(document, cleaned.Document);
        Assert.Equal(MailBodyCleaning.MaximumAttempts, cleaner.Asked.Count);
    }

    /// <summary>An answer that kept nothing is an answer this cannot use, because a reader handed an empty pane has lost the message.</summary>
    [Fact]
    public async Task CleanAsync_AnAnswerDroppingEveryBlock_IsReadAsUnusableRatherThanDrawnAsNothing()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Keeping(new MailBodyCleaningSegment(0, 1, Keep: false));

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.AnswerRejected, cleaned!.Outcome);
        Assert.Equal(document, cleaned.Document);
    }

    /// <summary>A spent period says the same thing twice, so it is not asked again and the reduced document is served with the reason.</summary>
    [Fact]
    public async Task CleanAsync_AnExhaustedAllowance_ServesTheReducedDocumentWithoutAskingAgain()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Proposing(
            MailBodyCleaningProposal.Withheld(MailBodyCleaningWithholding.AllowanceExhausted));

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.AllowanceExhausted, cleaned!.Outcome);
        Assert.Equal(document, cleaned.Document);
        Assert.Single(cleaner.Asked);
    }

    [Fact]
    public async Task CleanAsync_AnEndpointThatDidNotAnswer_ServesTheReducedDocumentAndSaysSo()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Proposing(
            MailBodyCleaningProposal.Withheld(MailBodyCleaningWithholding.ProviderUnavailable));

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.ProviderUnavailable, cleaned!.Outcome);
        Assert.Equal(document, cleaned.Document);
    }

    /// <summary>A deployment that did not turn the pass on spends nothing finding that out, so nothing is asked at all.</summary>
    [Fact]
    public async Task CleanAsync_ADeploymentThatDidNotTurnThePassOn_ServesTheReducedDocumentWithoutAskingAnything()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Inactive();

        // Act
        var cleaned = await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.NotActivated, cleaned!.Outcome);
        Assert.Equal(document, cleaned.Document);
        Assert.Empty(cleaner.Asked);
    }

    /// <summary>A body that produced no document has nothing to clean, and the answer says that rather than reporting a failure.</summary>
    [Fact]
    public async Task CleanAsync_ABodyThatProducedNoDocument_SaysThereWasNothingToClean()
    {
        // Arrange
        var cleaner = ScriptedMailBodyCleaner.Keeping(new MailBodyCleaningSegment(0, 0, Keep: true));

        // Act
        var cleaned = await CleaningOf(document: null, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningOutcome.NothingToClean, cleaned!.Outcome);
        Assert.Null(cleaned.Document);
        Assert.Empty(cleaner.Asked);
    }

    /// <summary>The outline carries the envelope, because the rule about a pasted header block is decided against it.</summary>
    [Fact]
    public async Task CleanAsync_AMessageWithASenderAndASubject_AsksWithBothOfThemAndWithNoneOfTheMarkup()
    {
        // Arrange
        var document = DocumentSaying("Preheader", "Your code is 558132");
        var cleaner = ScriptedMailBodyCleaner.Keeping(new MailBodyCleaningSegment(0, 1, Keep: true));

        // Act
        await CleaningOf(document, cleaner).CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        var outline = Assert.Single(cleaner.Asked);

        Assert.Equal("Your receipt", outline.Subject);
        Assert.Equal("The Shop", outline.SenderName);
        Assert.Equal(["Preheader", "Your code is 558132"], outline.Blocks.Select(block => block.Opening));
    }

    /// <summary>
    /// A read this caller's grant does not reach is nothing rather than a refusal of its own, which is the same answer
    /// the body route gives: a message somebody does not hold does not exist as far as this surface is concerned.
    /// </summary>
    [Fact]
    public async Task CleanAsync_AMessageThisCallerDoesNotHold_AnswersNothing()
    {
        // Arrange
        var cleaning = new MailBodyCleaning(
            ReaderOver(summary: null, document: null),
            ScriptedMailBodyCleaner.Keeping(new MailBodyCleaningSegment(0, 0, Keep: true)));

        // Act
        var cleaned = await cleaning.CleanAsync(
            Message,
            retainRemoteImageReferences: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(cleaned);
    }

    private static MailBodyCleaning CleaningOf(MailDocument? document, IMailBodyCleaner cleaner) =>
        new(ReaderOver(SummaryOf(), document), cleaner);

    private static EmailSummary SummaryOf() => SyntheticEmailSummaries.Create() with { StoredEmailId = Message };

    private static MailDocument DocumentSaying(params string[] paragraphs) => MailDocument.Reduced(
        [
            .. paragraphs.Select(text => new MailParagraphBlock(
                [new MailInlineRun(text, MailTextEmphasis.None, Foreground: null, Link: null)],
                MailBlockAlignment.Inherited)),
        ],
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false);

    private static EmailContentReader ReaderOver(EmailSummary? summary, MailDocument? document)
    {
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        summaries.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(summary));

        var contentStore = ContentStores.Substituted();
        contentStore.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(
                new StoredEmailContent(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime))));

        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.OwnedAccounts.Returns(
            [SyntheticServedAccount.Of(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId))]);

        return new EmailContentReader(
            summaries,
            new StubEmailThreadReader(),
            contentStore,
            RendererReturning(document),
            new RecordingEmailContentRepairRequestStore(),
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation.Mapping(summary is null
                    ? []
                    : [new MailFolderIdentity(summary.AccountId, summary.FolderAlias)]),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            new RecordingAttachmentDownloadLinkIssuer(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            new RecordingMailboxReadTelemetry(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    private static IEmailContentRenderer RendererReturning(MailDocument? document)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(RenderingOf(document))));

        return renderer;
    }

    private static EmailContentRendering RenderingOf(MailDocument? document) => new(
        new EmailContentHeaders(
            "Your receipt",
            SentAt: null,
            ReceivedAt: null,
            [ParticipantOf("The Shop", "shop@example.test")],
            EmailThreadReferences.None),
        new EmailBodyRepresentation("Body", 4, EmailBodyTruncation.None),
        SanitizedHtmlBody: null,
        new EmailBodyForms(PlainText: true, Html: false),
        BodyIsEncrypted: false,
        EmailAttachmentSummary.Create(
            [],
            inlineResourceCount: 0,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false),
        [])
    {
        Document = document,
    };

    private static EmailParticipant ParticipantOf(string displayName, string address) =>
        EmailAddress.TryCreate(displayName, address, out var emailAddress)
            ? new EmailParticipant(EmailAddressRole.From, emailAddress)
            : throw new InvalidOperationException($"'{address}' is not a usable address.");
}
