// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.AI.Chunking;

/// <summary>Cuts extracted text along the strongest separator each window offers, with a fixed overlap.</summary>
/// <remarks>
/// <para>
/// The cut walks the text once. Each window reaches at most the target length; inside it the strongest separator that
/// leaves a chunk of at least the minimum length ends the chunk, and a window offering none is cut at its own end. The
/// next window starts a fixed overlap back into the one before, so a sentence straddling a boundary survives whole in
/// one of the two chunks that share it.
/// </para>
/// <para>
/// Nothing here consults a clock, a random source, a culture, or a provider, which is what makes two runs over one text
/// produce identical chunks. Every comparison is ordinal for the same reason: a culture-sensitive search would make the
/// boundaries depend on the machine's locale, and a stored hash would stop matching the text it was computed from.
/// </para>
/// <para>
/// Every boundary falls between text elements, so no chunk begins or ends inside a surrogate pair or a combining
/// sequence. A cut through one would leave text PostgreSQL rejects and a JSON writer mangles, and it would give a
/// retrieved passage a character its author never wrote.
/// </para>
/// </remarks>
internal sealed class DeterministicEmailTextChunker : IEmailTextChunker
{
    /// <inheritdoc />
    public EmailChunkingResult DeriveChunks(
        ExtractedEmailText text,
        EmailChunkingRules rules,
        EmbeddingInputBound bound)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(bound);

        var selected = SelectSourceForm(text, rules);
        if (string.IsNullOrEmpty(selected))
        {
            return EmailChunkingResult.NoText;
        }

        var (source, truncatedFrom) = CutToBound(selected, bound);

        var chunks = new List<EmailTextChunk>();
        var start = 0;

        while (start < source.Length)
        {
            var end = FindChunkEnd(source, start, rules);
            var passage = source[start..end];

            // A window holding nothing but blank lines carries no passage to retrieve, so it consumes no ordinal
            // either: the numbering stays the reading order of the chunks that exist rather than of the windows walked.
            if (!string.IsNullOrWhiteSpace(passage))
            {
                chunks.Add(new EmailTextChunk(
                    chunks.Count,
                    start,
                    passage,
                    EmailChunkContentHash.Compute(rules, text.IsDerivedFromHtml, passage),
                    rules.RuleSetVersion,
                    text.IsDerivedFromHtml));
            }

            if (end >= source.Length)
            {
                break;
            }

            start = FindNextStart(source, start, end, rules.OverlapCharacterCount);
        }

        return new EmailChunkingResult(chunks, truncatedFrom);
    }

    /// <summary>Cuts the text down to what one message may cost, and reports the length it had when that happened.</summary>
    /// <remarks>
    /// The cut lands on a text-element boundary for the reason every boundary here does: a ceiling that fell inside a
    /// surrogate pair or a combining sequence would hand the last chunk a character its author never wrote, and
    /// PostgreSQL would refuse the write rather than store it.
    /// </remarks>
    private static (string Source, int? TruncatedFrom) CutToBound(string source, EmbeddingInputBound bound)
    {
        if (source.Length <= bound.MaximumCharacterCount)
        {
            return (source, null);
        }

        return (source[..BoundaryAtOrBefore(source, knownBoundary: 0, bound.MaximumCharacterCount)], source.Length);
    }

    private static string? SelectSourceForm(ExtractedEmailText text, EmailChunkingRules rules) => rules.SourceForm switch
    {
        EmailChunkSourceForm.TrimmedText => text.TrimmedText,
        EmailChunkSourceForm.OriginalText => text.OriginalText,
        _ => throw new ArgumentOutOfRangeException(nameof(rules), rules.SourceForm, "Unknown chunk source form."),
    };

    /// <summary>Finds where the chunk beginning at a position ends.</summary>
    private static int FindChunkEnd(string source, int start, EmailChunkingRules rules)
    {
        if (source.Length - start <= rules.TargetCharacterCount)
        {
            return source.Length;
        }

        var windowEnd = BoundaryAtOrBefore(source, start, start + rules.TargetCharacterCount);

        // A single text element longer than the whole window is the one case the bound cannot honour. Taking it whole
        // overruns the target by a few characters; refusing it would leave the walk with nowhere to go.
        if (windowEnd <= start)
        {
            return start + NextTextElementLength(source, start);
        }

        // Moved onto a boundary of its own, because the separator search walks text elements forward from it and a walk
        // starting inside one would read the rest of that element as though it were a character in its own right.
        var earliestAcceptableEnd = Math.Min(
            BoundaryAtOrBefore(source, start, start + rules.MinimumCharacterCount),
            windowEnd);

        return rules.BoundarySeparators
            .Select(separator => FindSeparatorEnd(source, earliestAcceptableEnd, windowEnd, separator))
            .FirstOrDefault(separatorEnd => separatorEnd > start, windowEnd);
    }

    /// <summary>Finds the end of the last occurrence of a separator inside a window, or zero when it holds none.</summary>
    /// <remarks>
    /// The last rather than the first, because a chunk should reach as far as the window allows: the earlier
    /// occurrences of a paragraph break inside one window are boundaries the next windows will reach anyway.
    /// </remarks>
    private static int FindSeparatorEnd(string source, int searchStart, int searchEnd, string separator)
    {
        var window = source.AsSpan(searchStart, searchEnd - searchStart);
        var offset = window.LastIndexOf(separator, StringComparison.Ordinal);
        if (offset < 0)
        {
            return 0;
        }

        // The separator itself belongs to the chunk it ends: keeping it makes the offsets name a contiguous span of the
        // extracted text, so a citation can be verified by reading that span back rather than by re-deriving the cut.
        return BoundaryAtOrBefore(source, searchStart, searchStart + offset + separator.Length);
    }

    /// <summary>Finds where the window after a chunk starts, reaching back into it by the configured overlap.</summary>
    private static int FindNextStart(string source, int start, int end, int overlapCharacterCount)
    {
        var overlapStart = end - overlapCharacterCount;

        // An overlap reaching past the start of the chunk it follows would walk the same window forever. Rules that
        // could produce one are refused, so this only guards a chunk the window bound had to cut short.
        return overlapStart <= start
            ? end
            : BoundaryAtOrAfter(source, start, overlapStart);
    }

    /// <summary>Finds the greatest text-element boundary at or before a position, walking from an earlier one.</summary>
    /// <param name="source">The text being cut.</param>
    /// <param name="knownBoundary">A position already known to fall between text elements.</param>
    /// <param name="position">The position to move back to a boundary.</param>
    /// <returns>The boundary, which is <paramref name="knownBoundary" /> when the position falls inside its element.</returns>
    private static int BoundaryAtOrBefore(string source, int knownBoundary, int position)
    {
        var boundary = knownBoundary;

        while (boundary < position)
        {
            var next = boundary + NextTextElementLength(source, boundary);
            if (next > position)
            {
                break;
            }

            boundary = next;
        }

        return boundary;
    }

    /// <summary>Finds the least text-element boundary at or after a position, walking from an earlier one.</summary>
    private static int BoundaryAtOrAfter(string source, int knownBoundary, int position)
    {
        var boundary = knownBoundary;

        while (boundary < position)
        {
            boundary += NextTextElementLength(source, boundary);
        }

        return boundary;
    }

    private static int NextTextElementLength(string source, int boundary) =>
        StringInfo.GetNextTextElementLength(source.AsSpan(boundary));
}
