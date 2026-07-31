// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Removes the quoted history and the signature from the end of an extracted body text.</summary>
/// <remarks>
/// <para>
/// Every rule here is conservative in one direction: it may leave quoted history in place, and it must never remove
/// something a person wrote. Mail has no marker that reliably separates a reply from what it replies to, so the rules
/// recognize only conventions that clients actually emit, only at the end of the text, and only when what remains still
/// says something. The untrimmed text is retained beside the result, so even a wrong cut costs ranking rather than
/// content.
/// </para>
/// <para>
/// Trimming the end alone is the whole of the safety argument. A reply written under the quoted message — the style
/// most mail clients default to for forwarding — keeps its own words above the block that is removed, while a reply
/// written inside a quoted block is untouched because the block does not reach the end of the text.
/// </para>
/// </remarks>
internal static partial class QuotedHistoryTrimmer
{
    /// <summary>The greatest number of lines a trailing block may hold and still be read as a signature.</summary>
    /// <remarks>
    /// RFC 3676 gives the separator no length, so the bound is what separates a signature from a second message that
    /// happens to start with two dashes. A signature longer than this is left in the text rather than guessed at.
    /// </remarks>
    private const int MaximumSignatureLines = 20;

    /// <summary>Removes the trailing quoted history and signature, keeping the text whenever removal would empty it.</summary>
    /// <param name="bodyText">The extracted body text, with line breaks already normalized to <c>\n</c>.</param>
    /// <returns>The text that remains, or the original text when nothing could be removed safely.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bodyText" /> is <see langword="null" />.</exception>
    public static string Trim(string bodyText)
    {
        ArgumentNullException.ThrowIfNull(bodyText);

        var lines = bodyText.Split('\n');

        var keptLineCount = FindFirstOriginalMessageMarkerLine(lines)
            ?? FindQuotedHistoryStartLine(lines)
            ?? lines.Length;

        keptLineCount = FindSignatureStartLine(lines, keptLineCount) ?? keptLineCount;

        var trimmed = string.Join('\n', lines.Take(keptLineCount)).TrimEnd();

        // A message that is nothing but a forwarded block or a signature would otherwise be indexed on its headers
        // alone, which is exactly the silent gap trimming must not create.
        return trimmed.Length == 0 ? bodyText : trimmed;
    }

    /// <summary>Matches the separator Outlook and several other clients write above a forwarded or replied-to message.</summary>
    [GeneratedRegex(@"^\s*-{2,}\s*(Original Message|Forwarded message|Weitergeleitete Nachricht|Oorspronkelijk bericht)\s*-{2,}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex OriginalMessageMarker();

    /// <summary>Matches the attribution line a client writes directly above the block it quotes.</summary>
    /// <remarks>
    /// The pattern is anchored on both ends and bounded in length, because an unanchored "wrote:" would match a
    /// sentence in the message body and take the paragraph after it out of the index.
    /// </remarks>
    [GeneratedRegex(@"^\s{0,8}(On|Am|Le|El|Il)\b.{0,300}\b(wrote|schrieb|a écrit|escribió|ha scritto)\s*:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex QuoteAttributionLine();

    /// <summary>Finds the first line of the forwarded-message block, or nothing when the text carries none.</summary>
    /// <remarks>
    /// The topmost marker is the outermost one, and it is the one honored: everything below it belongs to the messages
    /// being forwarded rather than to this one. Honoring the last marker instead would cut only the innermost message
    /// out of a forwarded chain and index every message above it as though its text were this message's own.
    /// </remarks>
    private static int? FindFirstOriginalMessageMarkerLine(string[] lines)
    {
        var markerLine = FirstIndexWhere(lines, line => OriginalMessageMarker().IsMatch(line));

        return markerLine >= 0 ? markerLine : null;
    }

    /// <summary>Finds where a trailing run of quoted lines begins, including the attribution line that introduces it.</summary>
    /// <remarks>
    /// Blank lines inside the run are part of it, because a quoted message's own paragraph breaks arrive unprefixed
    /// from several clients. A run that reaches the first line of the text is not treated as history: a message that is
    /// entirely quoted is a message whose whole content is the quote.
    /// </remarks>
    private static int? FindQuotedHistoryStartLine(string[] lines)
    {
        var firstQuotedLine = lines.Length;

        for (var candidate = lines.Length - 1; candidate >= 0; candidate--)
        {
            var line = lines[candidate];
            if (line.TrimStart().StartsWith('>'))
            {
                firstQuotedLine = candidate;

                continue;
            }

            if (line.Trim().Length == 0 && firstQuotedLine < lines.Length)
            {
                continue;
            }

            break;
        }

        if (firstQuotedLine >= lines.Length)
        {
            return null;
        }

        var attributionLine = firstQuotedLine;
        while (attributionLine > 0 && lines[attributionLine - 1].Trim().Length == 0)
        {
            attributionLine--;
        }

        if (attributionLine > 0 && QuoteAttributionLine().IsMatch(lines[attributionLine - 1]))
        {
            attributionLine--;
        }
        else
        {
            attributionLine = firstQuotedLine;
        }

        return attributionLine == 0 ? null : attributionLine;
    }

    /// <summary>Finds the RFC 3676 signature separator inside the kept lines, or nothing when there is no usable one.</summary>
    private static int? FindSignatureStartLine(string[] lines, int keptLineCount)
    {
        var separatorLine = LastIndexWhere(
            lines.Take(keptLineCount),
            line => string.Equals(line.TrimEnd(), "--", StringComparison.Ordinal));

        if (separatorLine <= 0 || keptLineCount - separatorLine > MaximumSignatureLines)
        {
            return null;
        }

        return separatorLine;
    }

    /// <summary>Reports the first position a predicate accepts, or <c>-1</c> when it accepts none.</summary>
    private static int FirstIndexWhere(IEnumerable<string> lines, Func<string, bool> predicate) =>
        lines
            .Select((line, position) => (Line: line, Position: position))
            .Where(candidate => predicate(candidate.Line))
            .Select(candidate => candidate.Position)
            .DefaultIfEmpty(-1)
            .First();

    /// <summary>Reports the last position a predicate accepts, or <c>-1</c> when it accepts none.</summary>
    private static int LastIndexWhere(IEnumerable<string> lines, Func<string, bool> predicate) =>
        lines
            .Select((line, position) => (Line: line, Position: position))
            .Where(candidate => predicate(candidate.Line))
            .Select(candidate => candidate.Position)
            .DefaultIfEmpty(-1)
            .Last();
}
