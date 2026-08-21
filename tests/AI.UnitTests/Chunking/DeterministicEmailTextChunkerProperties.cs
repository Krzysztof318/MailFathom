// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using CsCheck;
using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Chunking;

/// <summary>States what a cut has to be true of for every text and every rule set, not for the ones written down.</summary>
/// <remarks>
/// <para>
/// A citation names a span of the extracted text and a retrieval answers out of the passages this produces, so the
/// three rules below are what makes either usable at all: a passage reads back from its own offsets, the passages
/// arrive in reading order, and nothing an author wrote falls between two of them. An example test fixes one text and
/// one rule set; these fix the rules and let the generator choose the text — including the ones a body reaches by
/// accident, where a paragraph break sits one character inside the window or a single emoji is longer than the target.
/// </para>
/// <para>
/// The overlap is drawn up to half the target rather than up to the target itself. An overlap approaching the target
/// makes the walk advance a character at a time, so a long text would cut into thousands of passages: that is a
/// question about what such a configuration costs an operator, and it is one an example states, because sampling it
/// would make every iteration here quadratic in the length of the text.
/// </para>
/// </remarks>
public sealed class DeterministicEmailTextChunkerProperties
{
    /// <summary>How many inputs each property here draws.</summary>
    private const int Iterations = 200;

    /// <summary>The bound is above every text drawn, so that these properties state the cut rather than the ceiling.</summary>
    private static readonly EmbeddingInputBound Bound = EmbeddingInputBound.Default;

    /// <summary>The separator ladder every generated rule set breaks along, which is the one the deployment uses.</summary>
    private static readonly string[] BoundarySeparators = ["\n\n", "\n", " "];

    /// <summary>
    /// Fragments a body is assembled from: words, each strength of separator, characters that span more than one
    /// UTF-16 code unit, and a run long enough that no separator inside the window can end it.
    /// </summary>
    private static readonly Gen<string> Fragments = Gen.OneOf(
        Gen.String[Gen.Char['a', 'z'], 1, 14],
        Gen.OneOfConst("\n\n", "\n", " ", "\t", "  "),
        Gen.OneOfConst("😀", "👩‍👩‍👧‍👦", "🇵🇱", "é", "中"),
        Gen.String[Gen.Char['A', 'Z'], 20, 60]);

    private static readonly Gen<string> Bodies = Fragments
        .Array[0, 200]
        .Select(fragments => string.Concat(fragments));

    private static readonly Gen<EmailChunkingRules> RuleSets =
        from target in Gen.Int[100, 1200]
        from minimum in Gen.Int[1, target - 1]
        from overlap in Gen.Int[0, target / 2]
        select EmailChunkingRules.Create(
            ruleSetVersion: 1,
            target,
            minimum,
            overlap,
            BoundarySeparators,
            EmailChunkSourceForm.TrimmedText);

    private static readonly Gen<(string Body, EmailChunkingRules Rules)> Cases =
        Gen.Select(Bodies, RuleSets, (body, rules) => (body, rules));

    /// <summary>A citation is verified by reading the span back, so the offsets have to index the text literally.</summary>
    [Fact]
    public void DeriveChunks_AnyTextAndAnyRuleSet_ReadsEveryPassageBackFromItsOwnOffsets()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var chunks = Chunk(input.Body, input.Rules);

                Assert.All(chunks, chunk => Assert.InRange(chunk.EndOffset, 0, input.Body.Length));
                Assert.Equal(
                    chunks.Select(chunk => chunk.Text),
                    chunks.Select(chunk => input.Body.Substring(chunk.StartOffset, chunk.Text.Length)));
            },
            Iterations);
    }

    /// <summary>
    /// The ordinal is the reading order a citation and a re-cut comparison both rely on, and it means that only while
    /// each passage begins after the one before it.
    /// </summary>
    [Fact]
    public void DeriveChunks_AnyTextAndAnyRuleSet_NumbersThePassagesByAStrictlyAdvancingOffset()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var starts = Chunk(input.Body, input.Rules)
                    .Select(chunk => (chunk.Ordinal, chunk.StartOffset))
                    .ToArray();

                Assert.Equal(Enumerable.Range(0, starts.Length), starts.Select(chunk => chunk.Ordinal));

                // Sorting a strictly advancing sequence and dropping its repeats returns it unchanged; any other
                // sequence comes back reordered, shorter, or both.
                Assert.Equal(
                    starts.Select(chunk => chunk.StartOffset),
                    starts.Select(chunk => chunk.StartOffset).Order().Distinct());
            },
            Iterations);
    }

    /// <summary>
    /// A word nobody embedded is a word nobody can retrieve on, so the passages cover the text between them. A window
    /// holding only blank characters carries no passage to retrieve and is the one thing they may leave out.
    /// </summary>
    [Fact]
    public void DeriveChunks_AnyTextAndAnyRuleSet_LeavesNoNonBlankCharacterOfTheTextOutsideEveryPassage()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var spans = Chunk(input.Body, input.Rules)
                    .Select(chunk => (chunk.StartOffset, chunk.EndOffset))
                    .Prepend((StartOffset: 0, EndOffset: 0))
                    .Append((StartOffset: input.Body.Length, EndOffset: input.Body.Length))
                    .ToArray();

                var dropped = spans
                    .Zip(spans.Skip(1), (covered, next) => (From: covered.EndOffset, To: next.StartOffset))
                    .Where(gap => gap.To > gap.From)
                    .Select(gap => input.Body[gap.From..gap.To])
                    .Where(gap => !string.IsNullOrWhiteSpace(gap))
                    .ToArray();

                Assert.Empty(dropped);
            },
            Iterations);
    }

    private static IReadOnlyList<EmailTextChunk> Chunk(string body, EmailChunkingRules rules) =>
        new DeterministicEmailTextChunker()
            .DeriveChunks(ExtractedEmailText.FromPlainTextBody(body, body), rules, Bound)
            .Chunks;
}
