// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.SensitiveContent.Redaction;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText;

/// <summary>Covers what one attachment's reading records, and which of the two indexes the words it produced belong in.</summary>
public sealed class DerivedAttachmentTextTests
{
    private const string Contract = "The tenant pays for the roof above the west stairwell.";

    /// <summary>A document that was read carries its words, its pagination, and the word the extractor answered with.</summary>
    [Fact]
    public void FromExtraction_ADocumentThatWasRead_CarriesItsWordsPagesAndOutcome()
    {
        // Arrange
        var extracted = new ExtractedAttachmentText(
            Contract,
            PageCount: 2,
            PagesWithoutText: [2],
            Segments: [Page(1, 0), Page(2, 20)]);

        // Act
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Extracted(extracted));

        // Assert
        Assert.Equal(AttachmentTextKind.Document, derived.Kind);
        Assert.Equal(Contract, derived.Text);
        Assert.Equal(2, derived.PageCount);
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted.ToString(), derived.Outcome);
        Assert.Equal(2, derived.Segments.Count);
    }

    /// <summary>
    /// A user asking why their contract was never searched is owed the reason, so a refusal is a row rather than an
    /// absence — and a row carrying no words reaches neither index.
    /// </summary>
    [Fact]
    public void FromExtraction_AnExtractionThatReportedAReason_CarriesTheReasonAndNoWords()
    {
        // Act
        var derived = DerivedAttachmentText.FromExtraction(
            1,
            "application/pdf",
            "scan.pdf",
            AttachmentTextExtractionResult.Encrypted());

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted.ToString(), derived.Outcome);
        Assert.Null(derived.Text);
        Assert.Equal(0, derived.PageCount);
        Assert.Empty(derived.Segments);
        Assert.False(derived.HasText);
        Assert.False(derived.BelongsInLexicalIndex);
    }

    /// <summary>
    /// The one place ADR 0030's exclusion is decided. A word a model chose is not a word anybody wrote, so a description
    /// reaches the vector index and never the lexical one, however ordinary its text looks beside a document's.
    /// </summary>
    [Fact]
    public void FromDescription_AnImageThatWasDescribed_CarriesItsWordsAndStaysOutOfTheLexicalIndex()
    {
        // Act
        var derived = DerivedAttachmentText.FromDescription(
            2,
            "image/png",
            "roof.png",
            ImageAttachmentDescription.Described("A tiled roof with a tarpaulin over one corner."));

        // Assert
        Assert.Equal(AttachmentTextKind.ImageDescription, derived.Kind);
        Assert.True(derived.HasText);
        Assert.False(derived.BelongsInLexicalIndex);
    }

    /// <summary>
    /// A description paginates nowhere, so it carries one place covering the whole of it — without which a passage cut
    /// from a long description would resolve to no coordinate and a citation would have to special-case the kind.
    /// </summary>
    [Fact]
    public void FromDescription_AnImageThatWasDescribed_CarriesOnePlaceCoveringTheWholeDescription()
    {
        // Act
        var derived = DerivedAttachmentText.FromDescription(
            0,
            "image/png",
            fileName: null,
            ImageAttachmentDescription.Described("A tiled roof."));

        // Assert
        var segment = Assert.Single(derived.Segments);
        Assert.Equal(AttachmentTextSegmentKind.Page, segment.Kind);
        Assert.Equal(1, segment.Number);
        Assert.Equal(0, segment.StartOffset);
        Assert.Equal(1, derived.PageCount);
    }

    /// <summary>A picture nothing described says why, and carries no place either.</summary>
    [Fact]
    public void FromDescription_AnImageThatWasRefused_CarriesTheRefusalAndNoPlace()
    {
        // Act
        var derived = DerivedAttachmentText.FromDescription(
            0,
            "image/png",
            "roof.png",
            ImageAttachmentDescription.Refused(ImageDescriptionRefusal.ImageTooLarge));

        // Assert
        Assert.Equal(ImageDescriptionRefusal.ImageTooLarge.ToString(), derived.Outcome);
        Assert.Null(derived.Text);
        Assert.Empty(derived.Segments);
        Assert.False(derived.HasText);
    }

    /// <summary>The words a document yielded are what a lexical match names the file by, so those do join the index.</summary>
    [Fact]
    public void BelongsInLexicalIndex_ADocumentThatYieldedWords_IsTrue()
    {
        // Arrange
        var extracted = new ExtractedAttachmentText(Contract, PageCount: 1, [], [Page(1, 0)]);

        // Act
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Extracted(extracted));

        // Assert
        Assert.True(derived.BelongsInLexicalIndex);
    }

    /// <summary>A ceiling that stopped a file is an answer rather than silence, and it opened nothing to read.</summary>
    [Fact]
    public void PastMessageBudget_AnAttachmentTheCeilingStopped_RecordsTheCeilingAndNothingElse()
    {
        // Act
        var derived = DerivedAttachmentText.PastMessageBudget(21, "application/pdf", "appendix.pdf");

        // Assert
        Assert.Equal(21, derived.Position);
        Assert.Equal("MessageBudgetExhausted", derived.Outcome);
        Assert.Null(derived.Text);
        Assert.Equal(0, derived.PageCount);
        Assert.Empty(derived.Segments);
        Assert.False(derived.BelongsInLexicalIndex);
    }

    /// <summary>A placeholder the length of what it replaced moves nothing, so every coordinate reads as it did.</summary>
    [Fact]
    public void WithRedactedText_APlaceholderTheLengthOfWhatItReplaced_KeepsThePlacesItWasReadFrom()
    {
        // Arrange
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText("account 1234", PageCount: 1, [], [Page(1, 0)])));

        // Act
        var redacted = derived.WithRedactedText(
            RedactedText.Create("account ####", [], omittedCharacterCount: 0, [new RedactedPlacement(8, 4, 4)]));

        // Assert
        Assert.Equal("account ####", redacted.Text);
        Assert.Equal([0], redacted.Segments.Select(segment => segment.StartOffset));
    }

    /// <summary>
    /// A placeholder is rarely the length of what it replaced, so every boundary after it has moved. Carrying each one
    /// across is what keeps a two-hundred-page contract citable after a signature block held one email address.
    /// </summary>
    [Fact]
    public void WithRedactedText_APlaceholderLongerThanWhatItReplaced_MovesEveryLaterBoundaryWithIt()
    {
        // Arrange
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText("a@b.co page one page two", PageCount: 2, [], [Page(1, 0), Page(2, 16)])));

        // Act
        var redacted = derived.WithRedactedText(RedactedText.Create(
            "[redacted:email] page one page two",
            [],
            omittedCharacterCount: 0,
            [new RedactedPlacement(0, 6, 16)]));

        // Assert
        Assert.Equal("[redacted:email] page one page two", redacted.Text);
        Assert.Equal([0, 26], redacted.Segments.Select(segment => segment.StartOffset));
        Assert.Equal(2, redacted.PageCount);
    }

    /// <summary>
    /// The analyzed ceiling drops the tail rather than passing it on unscanned, so a boundary pointing into that tail
    /// points at text the result does not carry. A citation that says nothing beats one that sends a reader elsewhere.
    /// </summary>
    [Fact]
    public void WithRedactedText_ABoundaryPastWhatTheCeilingAdmitted_IsLeftOutRatherThanPublishedStale()
    {
        // Arrange
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Extracted(
                new ExtractedAttachmentText("page one page two", PageCount: 2, [], [Page(1, 0), Page(2, 9)])));

        // Act
        var redacted = derived.WithRedactedText(
            RedactedText.Create("page one", [], omittedCharacterCount: 9));

        // Assert
        Assert.Equal("page one", redacted.Text);
        Assert.Equal([0], redacted.Segments.Select(segment => segment.StartOffset));
        Assert.Equal(2, redacted.PageCount);
    }

    /// <summary>There is nothing to redact in an attachment that yielded no words, so the record is left as it was.</summary>
    [Fact]
    public void WithRedactedText_ARowCarryingNoWords_IsLeftExactlyAsItWas()
    {
        // Arrange
        var derived = DerivedAttachmentText.PastMessageBudget(0, "application/pdf", "appendix.pdf");

        // Act
        var redacted = derived.WithRedactedText(
            RedactedText.Create("anything", [], omittedCharacterCount: 0));

        // Assert
        Assert.Same(derived, redacted);
    }

    /// <summary>Nothing can be recorded from arguments that are not there.</summary>
    [Fact]
    public void Factories_AMissingArgument_AreRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            DerivedAttachmentText.PastMessageBudget(0, null!, "appendix.pdf"));
        Assert.Throws<ArgumentNullException>(() =>
            DerivedAttachmentText.FromExtraction(0, null!, null, AttachmentTextExtractionResult.Encrypted()));
        Assert.Throws<ArgumentNullException>(() =>
            DerivedAttachmentText.FromExtraction(0, "application/pdf", null, null!));
        Assert.Throws<ArgumentNullException>(() => DerivedAttachmentText.FromDescription(
            0,
            null!,
            null,
            ImageAttachmentDescription.Described("A tiled roof.")));
        Assert.Throws<ArgumentNullException>(() =>
            DerivedAttachmentText.FromDescription(0, "image/png", null, null!));
        Assert.Throws<ArgumentNullException>(() =>
            DerivedAttachmentText.PastMessageBudget(0, "application/pdf", null).WithRedactedText(null!));
    }

    /// <summary>A media type is the sender's header and nothing upstream bounds it, so this record does.</summary>
    /// <remarks>
    /// Left unbounded it reaches a column narrower than it and fails the insert, which is not a conflict the commit
    /// policy retries — so the message would never be stamped and every later run would meet it first and fail again.
    /// </remarks>
    [Fact]
    public void PastMessageBudget_AMediaTypeLongerThanItIsStoredAt_HoldsItToTheCeiling()
    {
        // Arrange
        var declared = new string('a', DerivedAttachmentText.MaximumDeclaredMediaTypeLength + 40) + "/plain";

        // Act
        var derived = DerivedAttachmentText.PastMessageBudget(0, declared, "appendix.pdf");

        // Assert
        Assert.Equal(DerivedAttachmentText.MaximumDeclaredMediaTypeLength, derived.DeclaredMediaType.Length);
        Assert.StartsWith("aaaa", derived.DeclaredMediaType, StringComparison.Ordinal);
    }

    /// <summary>An ordinary media type is recorded exactly as the part declared it.</summary>
    [Fact]
    public void FromExtraction_AMediaTypeWithinTheCeiling_RecordsItUnchanged()
    {
        // Act
        var derived = DerivedAttachmentText.FromExtraction(
            0,
            "application/pdf",
            "lease.pdf",
            AttachmentTextExtractionResult.Encrypted());

        // Assert
        Assert.Equal("application/pdf", derived.DeclaredMediaType);
    }

    /// <summary>Cutting a media type must not leave half a character behind, which no encoder could then write.</summary>
    /// <remarks>
    /// A lone surrogate reaching the same insert fails it exactly as an over-long value would, which would reproduce
    /// the defect this ceiling exists to remove.
    /// </remarks>
    [Fact]
    public void PastMessageBudget_AMediaTypeCutThroughASurrogatePair_DropsThePairRatherThanHalfOfIt()
    {
        // Arrange
        var declared = new string('a', DerivedAttachmentText.MaximumDeclaredMediaTypeLength - 1)
            + "\U0001F600"
            + "extra";

        // Act
        var derived = DerivedAttachmentText.PastMessageBudget(0, declared, fileName: null);

        // Assert
        Assert.Equal(DerivedAttachmentText.MaximumDeclaredMediaTypeLength - 1, derived.DeclaredMediaType.Length);
        Assert.DoesNotContain(derived.DeclaredMediaType, character => char.IsSurrogate(character));
    }

    private static AttachmentTextSegment Page(int number, int startOffset) =>
        new(AttachmentTextSegmentKind.Page, number, Label: null, startOffset);
}
