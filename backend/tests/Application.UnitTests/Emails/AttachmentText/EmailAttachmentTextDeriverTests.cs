// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText;

/// <summary>Covers what reading one message's attachments decides, defers, and refuses to write down.</summary>
public sealed class EmailAttachmentTextDeriverTests
{
    private const string Contract = "The tenant pays for the roof above the west stairwell.";

    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private readonly IEmailContentStore contentStore = Substitute.For<IEmailContentStore>();
    private readonly IEmailAttachmentContentReader attachmentReader = Substitute.For<IEmailAttachmentContentReader>();
    private readonly IOpenedEmailAttachmentWalk attachmentWalk = Substitute.For<IOpenedEmailAttachmentWalk>();
    private readonly IAttachmentTextExtractor extractor = Substitute.For<IAttachmentTextExtractor>();
    private readonly IEmailAttachmentImageDescriber describer = Substitute.For<IEmailAttachmentImageDescriber>();
    private readonly IEmailContentRepairRequestStore repairRequestStore = Substitute.For<IEmailContentRepairRequestStore>();

    /// <summary>A document is read by the extractor, and its words and pagination reach the record whole.</summary>
    [Fact]
    public async Task DeriveAsync_ADocumentAttachment_RecordsWhatTheExtractorRead()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "lease.pdf", octets: 2048);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText(Contract, PageCount: 1, [], [Page(1, 0)])));

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(derived);
        var attachment = Assert.Single(derived.Attachments);
        Assert.Equal(AttachmentTextKind.Document, attachment.Kind);
        Assert.Equal(Contract, attachment.Text);
        Assert.True(attachment.BelongsInLexicalIndex);
        Assert.True(derived.YieldedText);
    }

    /// <summary>
    /// The extractor decides which of the two ports reads a file, and it decides it before buffering an octet: anything
    /// whose declaration names no document format falls through to the describer. Keeping a second copy of that format
    /// table here is what would let the two disagree about what a picture is.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AnAttachmentTheExtractorDoesNotRecognize_IsOfferedToTheDescriber()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "image/png", "roof.png", octets: 4096);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.FormatNotRecognized());
        this.describer
            .DescribeAsync("image/png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ImageAttachmentDescription.Described("A tiled roof with a tarpaulin over one corner."));

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(derived!.Attachments);
        Assert.Equal(AttachmentTextKind.ImageDescription, attachment.Kind);
        Assert.False(attachment.BelongsInLexicalIndex);
    }

    /// <summary>
    /// Everything the extractor answers other than *not a format I read* is final. A spreadsheet no parser could open is
    /// not then shown to a vision model, which would spend a provider call on a file nothing depicts.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_ADocumentTheExtractorRefused_IsNeverOfferedToTheDescriber()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "scan.pdf", octets: 2048);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Encrypted());

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(derived!.Attachments);
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted.ToString(), attachment.Outcome);
        Assert.Null(attachment.Text);
        Assert.False(derived.YieldedText);
        await this.describer.DidNotReceive()
            .DescribeAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The count ceiling bounds the walk, so a part past it is never opened and leaves no row behind.</summary>
    /// <remarks>
    /// The one ceiling that records nothing, because the number of parts is the sender's: a row per declared part would
    /// let a stranger decide how many MIME parts one message costs to walk and how many rows it costs to store.
    /// </remarks>
    [Fact]
    public async Task DeriveAsync_MoreAttachmentsThanTheMessageMayRead_LeavesTheRestUnopened()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "one.pdf", octets: 16);
        this.Opens(1, "application/pdf", "two.pdf", octets: 16);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText(Contract, PageCount: 1, [], [Page(1, 0)])));

        var bounds = Bounds() with { MaxAttachmentsPerEmail = 1 };

        // Act
        var derived = await this.Deriver(bounds)
            .DeriveAsync(Awaiting(2), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(derived!.Attachments);
        Assert.Equal(Contract, derived.Attachments[0].Text);
        await this.attachmentWalk
            .DidNotReceive()
            .OpenAsync(1, Arg.Any<CancellationToken>());
    }

    /// <summary>The octet ceiling is the message's, so a file that would take it past is refused and the walk goes on.</summary>
    [Fact]
    public async Task DeriveAsync_AttachmentsBeyondTheMessageOctetCeiling_RecordsTheCeilingAgainstThem()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "one.pdf", octets: 800);
        this.Opens(1, "application/pdf", "two.pdf", octets: 800);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText(Contract, PageCount: 1, [], [Page(1, 0)])));

        var bounds = Bounds() with { MaxInputOctetsPerEmail = 1000 };

        // Act
        var derived = await this.Deriver(bounds)
            .DeriveAsync(Awaiting(2), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Contract, derived!.Attachments[0].Text);
        Assert.Equal("MessageBudgetExhausted", derived.Attachments[1].Outcome);
    }

    /// <summary>
    /// A run out of octets has decided nothing about the message in hand, so it answers with nothing at all. Writing a
    /// partial reading here would stamp the message as read while most of its attachments never were, and no later run
    /// would come back for them.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_TheRunBudgetRefusingAnAttachment_LeavesTheWholeMessageUndecided()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "one.pdf", octets: 4096);

        // Act
        var derived = await this.Deriver()
            .DeriveAsync(Awaiting(1), new EmailAttachmentTextRunBudget(64), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(derived);
        await this.extractor.DidNotReceive()
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A run already out of octets reaches nothing at all, and reads no content to find that out.</summary>
    [Fact]
    public async Task DeriveAsync_ARunBudgetAlreadySpent_ReadsNothingAndDefersTheMessage()
    {
        // Act
        var derived = await this.Deriver()
            .DeriveAsync(Awaiting(1), new EmailAttachmentTextRunBudget(0), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(derived);
        await this.contentStore.DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Content this deployment does not hold is what the repair path exists for, so the message is told about rather
    /// than stamped: settling it would take it out of the walk before the copy it needs has been fetched again.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AMessageWhoseRawMimeIsNotStored_RequestsARepairAndLeavesTheMessageUnsettled()
    {
        // Arrange
        this.contentStore
            .FindStoredContentAsync(Message, Arg.Any<CancellationToken>())
            .Returns((StoredEmailContent?)null);

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(derived);
        Assert.Empty(derived.Attachments);
        Assert.False(derived.IsSettled);
        await this.repairRequestStore.Received(1).RecordAsync(
            Arg.Is<EmailContentRepairRequest>(request => request!.Defect == EmailContentDefect.Missing),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Stored octets the walk can no longer open are a damaged local copy, which ends the message not the run.</summary>
    [Fact]
    public async Task DeriveAsync_StoredContentTheWalkCannotOpen_RequestsARepairAndKeepsWhatItAlreadyRead()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "application/pdf", "one.pdf", octets: 16);
        this.attachmentWalk
            .OpenAsync(1, Arg.Any<CancellationToken>())
            .Returns(OpenedEmailAttachmentResult.Unreadable());
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText(Contract, PageCount: 1, [], [Page(1, 0)])));

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(2), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(derived);
        Assert.Single(derived.Attachments);
        Assert.False(derived.IsSettled);
        await this.repairRequestStore.Received(1).RecordAsync(
            Arg.Is<EmailContentRepairRequest>(request => request!.Defect == EmailContentDefect.Unreadable),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A walk position the message turns out not to have is the count disagreeing with the structure, which no repair
    /// mends and no later run resolves — so it settles the message instead of holding it back for ever.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AWalkPositionTheMessageDoesNotHave_SettlesItWithoutRequestingARepair()
    {
        // Arrange
        this.StoreHolds();

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derived!.IsSettled);
        Assert.Empty(derived.Attachments);
        await this.repairRequestStore.DidNotReceive()
            .RecordAsync(Arg.Any<EmailContentRepairRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A file's words are derived mail content exactly as a body's are, so what is stored is what the owner's scanner
    /// left behind — a bank statement's account number never reaches a row because it arrived as an attachment.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_ADocumentCarryingSomethingTheScannerFinds_StoresOnlyTheRedactedWords()
    {
        // Arrange
        using var scanning = ScanningSensitiveContentDerivation.Finding(
            "1234",
            new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        this.StoreHolds();
        this.Opens(0, "application/pdf", "statement.pdf", octets: 2048);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText("account 1234", PageCount: 1, [], [Page(1, 0)])));

        // Act
        var derived = await this.Deriver(guard: scanning.Guard)
            .DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("1234", derived!.Attachments[0].Text, StringComparison.Ordinal);
        Assert.NotNull(derived.RedactedUnder);
    }

    /// <summary>A model asked what a picture shows reads out whatever is printed on it, so a description is redacted too.</summary>
    [Fact]
    public async Task DeriveAsync_ADescriptionCarryingSomethingTheScannerFinds_StoresOnlyTheRedactedWords()
    {
        // Arrange
        using var scanning = ScanningSensitiveContentDerivation.Finding(
            "1234",
            new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        this.StoreHolds();
        this.Opens(0, "image/png", "statement.png", octets: 2048);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.FormatNotRecognized());
        this.describer
            .DescribeAsync("image/png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ImageAttachmentDescription.Described("a statement showing 1234"));

        // Act
        var derived = await this.Deriver(guard: scanning.Guard)
            .DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("1234", derived!.Attachments[0].Text, StringComparison.Ordinal);
    }

    /// <summary>A picture past the per-attachment input ceiling is refused here rather than buffered and sent.</summary>
    [Fact]
    public async Task DeriveAsync_AnImageBeyondThePerAttachmentInputCeiling_IsRefusedWithoutReachingTheProvider()
    {
        // Arrange
        this.StoreHolds();
        this.Opens(0, "image/png", "poster.png", octets: 4096);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.FormatNotRecognized());

        var extractionOptions = new AttachmentTextExtractionOptions { MaxInputOctets = 1024 };

        // Act
        var derived = await this.Deriver(extractionOptions: extractionOptions)
            .DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ImageDescriptionRefusal.ImageTooLarge.ToString(), derived!.Attachments[0].Outcome);
        await this.describer.DidNotReceive()
            .DescribeAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A provider having a bad afternoon has decided nothing, so the message is left for a later run to read.</summary>
    /// <remarks>
    /// Stamping it here would settle a picture nobody has looked at yet, and nothing would come back for it: the walk
    /// selects on the stamp, so the description that provider owes would never be asked for again.
    /// </remarks>
    [Theory]
    [InlineData(ImageDescriptionRefusal.ProviderTimedOut)]
    [InlineData(ImageDescriptionRefusal.ProviderUnavailable)]
    public async Task DeriveAsync_ADescriptionTheProviderMayAnswerLater_LeavesTheMessageUnsettled(
        ImageDescriptionRefusal refusal)
    {
        // Arrange
        this.RefusesToDescribe(refusal);

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(derived!.IsSettled);
    }

    /// <summary>
    /// Everything else a description can refuse for is settled here, because nothing about this message changes it: a
    /// switch or a ceiling an operator moves is a backfill over what is already stored, and the octets themselves are
    /// what they are.
    /// </summary>
    [Theory]
    [InlineData(ImageDescriptionRefusal.NotActivated)]
    [InlineData(ImageDescriptionRefusal.PixelGridTooLarge)]
    [InlineData(ImageDescriptionRefusal.ProviderRefused)]
    [InlineData(ImageDescriptionRefusal.FormatNotSupported)]
    public async Task DeriveAsync_ADescriptionRefusedForAnythingElse_SettlesTheMessage(ImageDescriptionRefusal refusal)
    {
        // Arrange
        this.RefusesToDescribe(refusal);

        // Act
        var derived = await this.Deriver().DeriveAsync(Awaiting(1), Budget(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derived!.IsSettled);
    }

    /// <summary>Nothing can be derived from arguments that are not there.</summary>
    [Fact]
    public async Task DeriveAsync_MissingArgument_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            this.Deriver().DeriveAsync(null!, Budget(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            this.Deriver().DeriveAsync(Awaiting(1), null!, TestContext.Current.CancellationToken));
    }

    /// <summary>Nothing can be composed from collaborators that are not there.</summary>
    [Fact]
    public void Construction_AMissingCollaborator_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new EmailAttachmentTextDeriver(
            null!,
            this.attachmentReader,
            this.extractor,
            this.describer,
            ScanningSensitiveContentDerivation.Inactive(),
            this.repairRequestStore,
            new AttachmentTextExtractionOptions(),
            Bounds()));
        Assert.Throws<ArgumentNullException>(() => new EmailAttachmentTextDeriver(
            this.contentStore,
            this.attachmentReader,
            this.extractor,
            this.describer,
            ScanningSensitiveContentDerivation.Inactive(),
            this.repairRequestStore,
            new AttachmentTextExtractionOptions(),
            null!));
    }

    private static EmailAttachmentTextBounds Bounds() => EmailAttachmentTextBounds.Disabled with { IsEnabled = true };

    private static EmailAttachmentTextRunBudget Budget() => new(1024L * 1024);

    private static AttachmentTextSegment Page(int number, int startOffset) =>
        new(AttachmentTextSegmentKind.Page, number, Label: null, startOffset);

    private static EmailAwaitingAttachmentText Awaiting(int attachmentCount) => new(
        Message,
        ScanningSensitiveContentDerivation.Owner,
        attachmentCount,
        DerivedWorkAdmission.Admitted);

    private EmailAttachmentTextDeriver Deriver(
        EmailAttachmentTextBounds? bounds = null,
        SensitiveContentDerivationGuard? guard = null,
        AttachmentTextExtractionOptions? extractionOptions = null) => new(
        this.contentStore,
        this.attachmentReader,
        this.extractor,
        this.describer,
        guard ?? ScanningSensitiveContentDerivation.Inactive(),
        this.repairRequestStore,
        extractionOptions ?? new AttachmentTextExtractionOptions(),
        bounds ?? Bounds());

    private void StoreHolds()
    {
        var content = new StoredEmailContent(
            new byte[] { 1, 2, 3 },
            RecordedByteLength: 3,
            RecordedSha256Hash: ReadOnlyMemory<byte>.Empty);

        this.contentStore.FindStoredContentAsync(Message, Arg.Any<CancellationToken>()).Returns(content);
        this.attachmentReader
            .OpenWalkAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(OpenedEmailAttachmentWalkResult.Opened(this.attachmentWalk));
        this.attachmentWalk
            .OpenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OpenedEmailAttachmentResult.NoSuchAttachment());
    }

    private void RefusesToDescribe(ImageDescriptionRefusal refusal)
    {
        this.StoreHolds();
        this.Opens(0, "image/png", "roof.png", octets: 4096);
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.FormatNotRecognized());
        this.describer
            .DescribeAsync("image/png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ImageAttachmentDescription.Refused(refusal));
    }

    private void Opens(int position, string mediaType, string fileName, long octets)
    {
        var attachment = Substitute.For<IOpenedEmailAttachment>();
        AttachmentFileName.TryNormalize(fileName, out var normalized);
        attachment.Description.Returns(new ExtractedEmailAttachment(normalized, mediaType, octets));

        this.attachmentWalk
            .OpenAsync(position, Arg.Any<CancellationToken>())
            .Returns(OpenedEmailAttachmentResult.Opened(attachment));
    }
}
