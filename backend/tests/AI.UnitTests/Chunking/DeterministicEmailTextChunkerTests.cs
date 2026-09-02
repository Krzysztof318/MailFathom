// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using Xunit;

namespace MailFathom.AI.UnitTests.Chunking;

/// <summary>Covers the boundary rules chunks are cut to, and the determinism every stored hash depends on.</summary>
public sealed class DeterministicEmailTextChunkerTests
{
    private readonly DeterministicEmailTextChunker chunker = new();

    /// <summary>Nothing hangs on a passage of a message that carried no words, so nothing is cut for one.</summary>
    [Theory]
    [MemberData(nameof(TextsWithoutABody))]
    public void DeriveChunks_TextWithoutABody_YieldsNoChunks(ExtractedEmailText text)
    {
        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.Empty(chunks);
    }

    /// <summary>A message shorter than the target is one passage, so retrieval cites it whole.</summary>
    [Fact]
    public void DeriveChunks_TextShorterThanTheTarget_YieldsOneChunkHoldingAllOfIt()
    {
        // Arrange
        const string body = "The roof above the west stairwell is leaking again after Tuesday's storm.";
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Ordinal);
        Assert.Equal(0, chunk.StartOffset);
        Assert.Equal(body, chunk.Text);
        Assert.Equal(body.Length, chunk.EndOffset);
    }

    /// <summary>
    /// The offsets are what lets a later citation name the exact span rather than the whole message, so they have to
    /// index the extracted text literally: reading the span back returns the passage and nothing else.
    /// </summary>
    [Fact]
    public void DeriveChunks_LongText_GivesEveryChunkOffsetsThatReadItsOwnTextBack()
    {
        // Arrange
        var body = Paragraphs(count: 12);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.Equal(
            chunks.Select(chunk => chunk.Text),
            chunks.Select(chunk => body.Substring(chunk.StartOffset, chunk.Text.Length)));
    }

    /// <summary>The ordinals are the reading order, which is what a citation and a re-cut comparison both rely on.</summary>
    [Fact]
    public void DeriveChunks_LongText_NumbersChunksFromZeroInReadingOrder()
    {
        // Arrange
        var body = Paragraphs(count: 12);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.True(chunks.Count > 1, "The sample has to be long enough to be cut into several passages.");
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Ordinal));
        Assert.Equal(
            chunks.Select(chunk => chunk.StartOffset).Order(),
            chunks.Select(chunk => chunk.StartOffset));
    }

    /// <summary>No passage may exceed the target, or a provider would refuse the chunk rather than the mailbox.</summary>
    [Fact]
    public void DeriveChunks_LongText_KeepsEveryChunkWithinTheTarget()
    {
        // Arrange
        var body = Paragraphs(count: 12);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.All(chunks, chunk => Assert.InRange(chunk.Text.Length, 1, EmailChunkingRules.Current.TargetCharacterCount));
    }

    /// <summary>A statement straddling a boundary is whole in one of the two chunks that share it, which is the overlap.</summary>
    [Fact]
    public void DeriveChunks_LongText_StartsEachChunkInsideTheOneBefore()
    {
        // Arrange
        var body = Paragraphs(count: 12);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.All(
            chunks.Zip(chunks.Skip(1)),
            pair => Assert.InRange(pair.Second.StartOffset, pair.First.StartOffset + 1, pair.First.EndOffset - 1));
    }

    /// <summary>A paragraph break is the strongest boundary, so a text offering one is cut there rather than mid-word.</summary>
    [Fact]
    public void DeriveChunks_TextWithParagraphBreaks_EndsChunksAfterTheLastBreakThatFits()
    {
        // Arrange
        var paragraph = new string('a', 400);
        var body = string.Join("\n\n", Enumerable.Repeat(paragraph, 4));
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.EndsWith("\n\n", chunks[0].Text, StringComparison.Ordinal);
        Assert.Equal(2 * (paragraph.Length + 2), chunks[0].EndOffset);
    }

    /// <summary>
    /// A separator near the start of a window would cut a passage too short to retrieve on, so the minimum refuses it
    /// and the window is cut at a later boundary instead.
    /// </summary>
    [Fact]
    public void DeriveChunks_SeparatorBeforeTheMinimum_IsNotWhereTheChunkEnds()
    {
        // Arrange
        var body = "Short line.\n\n" + new string('a', 2000);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.True(
            chunks[0].Text.Length >= EmailChunkingRules.Current.MinimumCharacterCount,
            "The first passage must not be cut at the paragraph break that precedes the minimum.");
    }

    /// <summary>A window offering no separator at all is cut at its own end rather than left uncut.</summary>
    [Fact]
    public void DeriveChunks_TextWithoutAnySeparator_CutsAtTheTarget()
    {
        // Arrange
        var body = new string('a', 2500);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.Equal(EmailChunkingRules.Current.TargetCharacterCount, chunks[0].Text.Length);
        Assert.True(chunks.Count > 1, "A text longer than the target has to be cut into several passages.");
    }

    /// <summary>
    /// A cut through a surrogate pair or a combining sequence leaves text PostgreSQL rejects and gives a retrieved
    /// passage a character its author never wrote, so every boundary falls between text elements.
    /// </summary>
    [Fact]
    public void DeriveChunks_TextOfMultiCharacterElements_CutsOnlyBetweenThem()
    {
        // Arrange
        const string element = "👨‍👩‍👧‍👦";
        var body = string.Concat(Enumerable.Repeat(element, 300));
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.All(chunks, chunk => Assert.Equal(0, chunk.Text.Length % element.Length));
        Assert.All(chunks, chunk => Assert.Equal(0, chunk.StartOffset % element.Length));
    }

    /// <summary>
    /// A single text element longer than the whole window is the one case the target cannot bound. Taking it whole
    /// overruns by a few characters; refusing it would leave the walk with nowhere to go.
    /// </summary>
    [Fact]
    public void DeriveChunks_TextElementLongerThanTheWindow_TakesItWholeRatherThanSplittingIt()
    {
        // Arrange
        var rules = EmailChunkingRules.Create(
            ruleSetVersion: 1,
            targetCharacterCount: 4,
            minimumCharacterCount: 2,
            overlapCharacterCount: 1,
            ["\n"],
            EmailChunkSourceForm.TrimmedText);
        const string element = "👨‍👩‍👧‍👦";
        var body = element + element;
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, rules);

        // Assert
        Assert.All(chunks, chunk => Assert.Equal(0, chunk.Text.Length % element.Length));
        Assert.Equal(body.Length, chunks[^1].EndOffset);
    }

    /// <summary>Determinism is what lets an unchanged message cost nothing: two runs must agree on every digest.</summary>
    [Fact]
    public void DeriveChunks_SameTextAndRules_ProducesIdenticalChunksOnEveryRun()
    {
        // Arrange
        var body = Paragraphs(count: 12);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var first = this.Chunk(text, EmailChunkingRules.Current);
        var second = new DeterministicEmailTextChunker()
            .DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default)
            .Chunks;

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>Retrieval means the text a reader wrote, so quoted history is out of the passage by default.</summary>
    [Fact]
    public void DeriveChunks_DefaultRules_CutsTheTrimmedFormRatherThanTheOriginal()
    {
        // Arrange
        var text = ExtractedEmailText.FromPlainTextBody("Quoted history.\n\nThe answer.", "The answer.");

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.Equal("The answer.", Assert.Single(chunks).Text);
    }

    /// <summary>The untrimmed reading is reachable, because an over-aggressive trim must not be the only one retrievable.</summary>
    [Fact]
    public void DeriveChunks_RulesNamingTheOriginalForm_CutsTheUntrimmedText()
    {
        // Arrange
        var rules = EmailChunkingRules.Create(
            EmailChunkingRules.Current.RuleSetVersion + 1,
            EmailChunkingRules.Current.TargetCharacterCount,
            EmailChunkingRules.Current.MinimumCharacterCount,
            EmailChunkingRules.Current.OverlapCharacterCount,
            EmailChunkingRules.Current.BoundarySeparators,
            EmailChunkSourceForm.OriginalText);
        var text = ExtractedEmailText.FromPlainTextBody("Quoted history.\n\nThe answer.", "The answer.");

        // Act
        var chunks = this.Chunk(text, rules);

        // Assert
        Assert.Equal("Quoted history.\n\nThe answer.", Assert.Single(chunks).Text);
    }

    /// <summary>A lossy reading is worth less than its length suggests, and a later ranking reads that off the chunk.</summary>
    [Fact]
    public void DeriveChunks_TextDerivedFromHtml_MarksEveryChunkAsLossy()
    {
        // Arrange
        var body = Paragraphs(count: 4);
        var text = ExtractedEmailText.DerivedFromHtmlBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.All(chunks, chunk => Assert.True(chunk.IsDerivedFromLossyHtml));
    }

    /// <summary>The version travels onto the row so a backfill can be asked how much is still cut to the old rules.</summary>
    [Fact]
    public void DeriveChunks_AnyText_CopiesTheRuleSetVersionOntoEveryChunk()
    {
        // Arrange
        var body = Paragraphs(count: 4);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, EmailChunkingRules.Current);

        // Assert
        Assert.All(chunks, chunk => Assert.Equal(EmailChunkingRules.Current.RuleSetVersion, chunk.RuleSetVersion));
    }

    /// <summary>A window of blank lines carries no passage, so it consumes no ordinal a citation could point at.</summary>
    [Fact]
    public void DeriveChunks_TextEndingInBlankLines_YieldsNoChunkForThem()
    {
        // Arrange
        var rules = EmailChunkingRules.Create(
            ruleSetVersion: 1,
            targetCharacterCount: 10,
            minimumCharacterCount: 2,
            overlapCharacterCount: 1,
            ["\n"],
            EmailChunkSourceForm.TrimmedText);
        var body = "The answer" + new string('\n', 30);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var chunks = this.Chunk(text, rules);

        // Assert
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }

    /// <summary>An unknown source form is a rule nothing can cut to, and a silent fallback would cut the wrong text.</summary>
    [Fact]
    public void DeriveChunks_UnknownSourceForm_IsRefused()
    {
        // Arrange
        var rules = EmailChunkingRules.Create(
            ruleSetVersion: 1,
            targetCharacterCount: 100,
            minimumCharacterCount: 10,
            overlapCharacterCount: 10,
            ["\n"],
            (EmailChunkSourceForm)(-1));
        var text = ExtractedEmailText.FromPlainTextBody("Body.", "Body.");

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => this.Chunk(text, rules));
    }

    /// <summary>Nothing may be cut from arguments that are not there.</summary>
    [Fact]
    public void DeriveChunks_MissingArgument_IsRefused()
    {
        // Arrange
        var text = ExtractedEmailText.FromPlainTextBody("Body.", "Body.");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => this.Chunk(null!, EmailChunkingRules.Current));
        Assert.Throws<ArgumentNullException>(() => this.Chunk(text, null!));
        Assert.Throws<ArgumentNullException>(
            () => this.chunker.DeriveChunks(text, EmailChunkingRules.Current, null!));
    }

    /// <summary>A message inside the ceiling is cut whole and its record says nothing was left out.</summary>
    [Fact]
    public void DeriveChunks_TextInsideTheInputBound_ReportsNoTruncation()
    {
        // Arrange
        var body = Paragraphs(4);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var cut = this.chunker.DeriveChunks(
            text,
            EmailChunkingRules.Current,
            EmbeddingInputBound.Create(body.Length));

        // Assert
        Assert.Null(cut.TruncatedFromCharacterCount);
        Assert.Equal(body.Length, cut.Chunks[^1].EndOffset);
    }

    /// <summary>An oversized message is bounded rather than refused, and the length it had is what the cut reports.</summary>
    [Fact]
    public void DeriveChunks_TextBeyondTheInputBound_CutsToItAndReportsTheLengthItHad()
    {
        // Arrange
        var body = Paragraphs(20);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);
        var bound = EmbeddingInputBound.Create(body.Length / 2);

        // Act
        var cut = this.chunker.DeriveChunks(text, EmailChunkingRules.Current, bound);

        // Assert
        Assert.Equal(body.Length, cut.TruncatedFromCharacterCount);
        Assert.NotEmpty(cut.Chunks);
        Assert.True(cut.Chunks[^1].EndOffset <= bound.MaximumCharacterCount);
    }

    /// <summary>
    /// A ceiling decides how many passages exist and never what one says, so every passage the bound leaves in place
    /// keeps the hash it had — which is what stops a raised or lowered ceiling from re-embedding text nothing changed.
    /// </summary>
    [Fact]
    public void DeriveChunks_TextBeyondTheInputBound_LeavesTheHashesOfTheRetainedPassagesUnchanged()
    {
        // Arrange
        var body = Paragraphs(20);
        var text = ExtractedEmailText.FromPlainTextBody(body, body);

        // Act
        var whole = this.chunker.DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default);
        var bounded = this.chunker.DeriveChunks(
            text,
            EmailChunkingRules.Current,
            EmbeddingInputBound.Create(body.Length / 2));

        // Assert
        Assert.NotEqual(whole.Chunks.Count, bounded.Chunks.Count);
        Assert.Equal(
            whole.Chunks.Take(bounded.Chunks.Count - 1).Select(chunk => chunk.ContentHash),
            bounded.Chunks.Take(bounded.Chunks.Count - 1).Select(chunk => chunk.ContentHash));
    }

    public static TheoryData<ExtractedEmailText> TextsWithoutABody() =>
    [
        ExtractedEmailText.NoTextualBody,
        ExtractedEmailText.EncryptedBody,
        ExtractedEmailText.FromPlainTextBody(string.Empty, string.Empty),
    ];

    /// <summary>Cuts through the chunker under a ceiling deliberately beyond anything these bodies reach.</summary>
    /// <remarks>
    /// Every test but the three about the ceiling itself is written about the boundary rules, and passing the default
    /// bound keeps them saying what they said before one existed: the cut is decided by the separators and the target
    /// length alone.
    /// </remarks>
    private IReadOnlyList<EmailTextChunk> Chunk(ExtractedEmailText text, EmailChunkingRules rules) =>
        this.chunker.DeriveChunks(text, rules, EmbeddingInputBound.Default).Chunks;

    /// <summary>Builds a body of numbered sentences, long enough to be cut and varied enough that no two chunks match.</summary>
    private static string Paragraphs(int count) => string.Join(
        "\n\n",
        Enumerable.Range(0, count).Select(index => string.Join(
            ' ',
            Enumerable.Range(0, 20).Select(sentence => string.Create(
                CultureInfo.InvariantCulture,
                $"Paragraph {index} sentence {sentence} reports what the west stairwell looked like that morning.")))));
}
