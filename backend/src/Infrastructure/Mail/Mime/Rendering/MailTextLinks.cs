// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Finds the addresses a message wrote as words and turns each of them into a link a reader may follow.</summary>
/// <remarks>
/// <para>
/// Most mail is machine-written and writes its addresses as text rather than as anchors, so a reduction that carried a
/// link only for an <c>a</c> element left the ordinary notification with its addresses as words nobody could act on.
/// The judgement is made here, in the service, for the reason <see cref="MailLinkReader" /> gives about being made
/// once: a client finding addresses for itself would be a second implementation of what a link is, and the quieter of
/// the two would be the one a reader was unlucky enough to have.
/// </para>
/// <para>
/// Every rule below is a rule over the characters in front of it. Nothing infers intent, nothing resolves an address
/// over the network, and nothing consults a model — what a reader is offered is exactly what the message wrote, with a
/// scheme supplied only in the one case a bare host leaves none.
/// </para>
/// <para>
/// A run already inside an anchor is left alone. The sender said where those words go, and finding a second address
/// inside them would replace their answer with this one's.
/// </para>
/// </remarks>
internal static partial class MailTextLinks
{
    /// <summary>The longest run this reads at all, past which the text is a payload rather than a sentence.</summary>
    /// <remarks>
    /// A run is already bounded by <see cref="MailDocumentBounds.MaximumCharactersPerRun" />, so this is the second
    /// bound rather than the only one: it keeps the scan linear in something this file states rather than in a number
    /// another type owns.
    /// </remarks>
    private const int MaximumScannedLength = 8192;

    /// <summary>What is dropped from the end of a match, because a sentence's punctuation is not part of an address.</summary>
    private const string TrailingPunctuation = ".,;:!?\"'”’»)]}>";

    /// <summary>Expands the runs that carry no link into the addresses their words name.</summary>
    /// <param name="runs">The runs of one block, already merged.</param>
    /// <param name="maximumRuns">How many runs the block may hold, which splitting is held to like anything else.</param>
    /// <param name="truncated">Reports that the bound stopped the expansion rather than the text ending.</param>
    /// <returns>The runs a reader is shown, with each written address carried as a link.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runs" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<MailInlineRun> Expanded(
        IReadOnlyList<MailInlineRun> runs,
        int maximumRuns,
        out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(runs);

        truncated = false;

        if (!runs.Any(Scannable))
        {
            return runs;
        }

        var expanded = new List<MailInlineRun>(runs.Count);

        foreach (var run in runs)
        {
            foreach (var piece in Split(run))
            {
                if (expanded.Count >= maximumRuns)
                {
                    truncated = true;

                    return expanded;
                }

                expanded.Add(piece);
            }
        }

        return expanded;
    }

    /// <summary>Answers whether a run is one an address could be found in.</summary>
    private static bool Scannable(MailInlineRun run) =>
        run.Link is null && run.Text.Length is > 0 and <= MaximumScannedLength;

    /// <summary>Splits one run at every address its words name, keeping how the words are drawn.</summary>
    /// <remarks>
    /// The matches are collected before any run is built, because the enumerator the source generator produces is a
    /// reference struct and cannot cross the boundary an iterator would put in the middle of this walk.
    /// </remarks>
    private static List<MailInlineRun> Split(MailInlineRun run)
    {
        if (!Scannable(run))
        {
            return [run];
        }

        var pieces = new List<MailInlineRun>();
        var written = 0;

        foreach (var match in WrittenAddress().EnumerateMatches(run.Text))
        {
            var matched = Trimmed(run.Text.AsSpan(match.Index, match.Length));

            if (matched.Length == 0 || LinkTo(matched) is not { } link)
            {
                continue;
            }

            if (match.Index > written)
            {
                pieces.Add(run with { Text = run.Text[written..match.Index] });
            }

            pieces.Add(run with { Text = matched, Link = link });

            written = match.Index + matched.Length;
        }

        if (pieces.Count == 0)
        {
            return [run];
        }

        if (written < run.Text.Length)
        {
            pieces.Add(run with { Text = run.Text[written..] });
        }

        return pieces;
    }

    /// <summary>Drops the sentence punctuation a match swept up, and the closing bracket that opened outside it.</summary>
    /// <remarks>
    /// A bracket is counted rather than always dropped, because an address is as likely to carry a balanced pair — a
    /// wiki title, a generated identifier — as a sentence is to close one around it.
    /// </remarks>
    private static string Trimmed(ReadOnlySpan<char> matched)
    {
        var end = matched.Length;

        while (end > 0 && TrailingPunctuation.Contains(matched[end - 1], StringComparison.Ordinal))
        {
            if (matched[end - 1] is ')' or ']' or '}'
                && Balances(matched[..end], matched[end - 1]))
            {
                break;
            }

            end--;
        }

        return matched[..end].ToString();
    }

    /// <summary>Answers whether the bracket closing this match was opened inside it.</summary>
    private static bool Balances(ReadOnlySpan<char> matched, char closing)
    {
        var opening = closing switch
        {
            ')' => '(',
            ']' => '[',
            _ => '{',
        };

        return matched.Count(opening) >= matched.Count(closing);
    }

    /// <summary>Reads one written address as the link it names, or reports that it names none.</summary>
    /// <remarks>
    /// A bare host names no scheme, and the one supplied for it is <c>https</c> rather than <c>http</c>: the reader is
    /// the one who will follow it, and offering them the protected form is the only choice here that cannot expose the
    /// address they were reading. What the sender wrote is never rewritten — a written <c>http://</c> stays one.
    /// </remarks>
    private static MailDocumentLink? LinkTo(string matched) => MailLinkReader.Read(ReferenceFor(matched), matched);

    private static string ReferenceFor(string matched) => matched switch
    {
        _ when matched.StartsWith("http://", StringComparison.OrdinalIgnoreCase) => matched,
        _ when matched.StartsWith("https://", StringComparison.OrdinalIgnoreCase) => matched,
        _ when matched.StartsWith("www.", StringComparison.OrdinalIgnoreCase) => $"https://{matched}",
        _ => $"mailto:{matched}",
    };

    /// <summary>Matches an address a message wrote as words: a URL with its scheme, a bare host, or a mail address.</summary>
    /// <remarks>
    /// Each alternative is anchored on a literal — <c>://</c>, <c>www.</c>, or the at sign — and every repetition
    /// inside it is over a character class that excludes whitespace, so no input makes this backtrack. The delimiters
    /// are the characters mail actually wraps an address in, which is what keeps a quoted or bracketed address from
    /// swallowing the punctuation around it.
    /// </remarks>
    [GeneratedRegex(
        """https?://[^\s<>"'`\\]+|\bwww\.[^\s<>"'`\\]+|[A-Za-z0-9._%+-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WrittenAddress();
}
