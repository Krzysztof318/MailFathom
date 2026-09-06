// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Asks <c>ts_headline</c> for extracts in one form, and reads what it returns back into published ones.</summary>
/// <remarks>
/// <para>
/// One place rather than one per reader, because both halves of it are a single agreement: the option list decides the
/// markers and the delimiter, and the reading decides what those markers mean. A second copy would be a second
/// agreement, and the two would disagree the first time either marker changed — which would not fail, it would publish
/// a message's opening words as though the query had matched them.
/// </para>
/// <para>
/// Everything the option list carries comes from validated deployment configuration or from a constant in this file, so
/// the list is composed rather than parameterized. Nothing a request carries reaches it, which is what keeps composing
/// it safe.
/// </para>
/// </remarks>
internal static class SearchHeadlineText
{
    /// <summary>Marks the start of a matched run of words in what PostgreSQL returns.</summary>
    /// <remarks>
    /// A control character rather than anything printable, because this marker is not decoration: whether a fragment
    /// carries it is what tells a genuine highlight from the opening words <c>ts_headline</c> falls back to when the
    /// query matched nothing inside the text. A printable marker cannot answer that — Markdown mail carrying
    /// <c>**</c> of its own would be read as highlighted and the fallback would be published as though it had matched.
    /// The indexed text cannot contain this character: text extraction drops every control character except the tab
    /// and the newline, so the distinction holds by construction rather than by improbability.
    /// </remarks>
    internal const string HighlightStartMarker = "\u0002";

    /// <summary>Marks the end of a matched run of words in what PostgreSQL returns.</summary>
    /// <remarks>Distinct from the start marker so a fragment cut short by the character bound can be told to be unbalanced and closed.</remarks>
    internal const string HighlightEndMarker = "\u0003";

    /// <summary>Marks both ends of a matched run of words in what a caller receives.</summary>
    /// <remarks>
    /// Emphasis a client can render, rather than PostgreSQL's default <c>&lt;b&gt;</c>: an extract is text cut from
    /// untrusted mail, and handing it back wrapped in markup invites a consumer to treat the rest of it as markup too.
    /// It is substituted for the control markers after the fragment has been recognized as highlighted, so what a text
    /// happens to contain never takes part in that decision.
    /// </remarks>
    internal const string PublishedHighlightMarker = "**";

    /// <summary>Marks an extract the character bound cut short.</summary>
    internal const string TruncationMarker = "…";

    /// <summary>Separates the extracts PostgreSQL returns as one value, so they can be split back apart.</summary>
    /// <remarks>
    /// A unit separator rather than the default ellipsis, because the default is punctuation that mail also contains and
    /// splitting on it would cut an extract in half wherever somebody wrote one. It is the same control character the
    /// filter fingerprint separates its fields with, and for the same reason: no prose carries it.
    /// </remarks>
    internal const string SnippetSeparator = "\u001f";

    /// <summary>Writes the bounds as the option list <c>ts_headline</c> reads them.</summary>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <returns>The option list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snippetBounds" /> is <see langword="null" />.</exception>
    internal static string Options(EmailSearchSnippetBounds snippetBounds)
    {
        ArgumentNullException.ThrowIfNull(snippetBounds);

        return string.Format(
            CultureInfo.InvariantCulture,
            "StartSel=\"{0}\", StopSel=\"{1}\", MaxFragments={2}, MaxWords={3}, MinWords={4}, FragmentDelimiter=\"{5}\"",
            HighlightStartMarker,
            HighlightEndMarker,
            snippetBounds.SnippetsPerEmail,
            snippetBounds.WordsPerSnippet,
            MinimumWordsPerSnippet(snippetBounds),
            SnippetSeparator);
    }

    /// <summary>Splits what the server returned into the extracts a result publishes.</summary>
    /// <param name="headline">What <c>ts_headline</c> returned, or <see langword="null" /> where the row carried no text.</param>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <returns>The published extracts, which is an empty list where nothing in the text matched.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snippetBounds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A fragment carrying no highlight marker is dropped. <c>ts_headline</c> falls back to the opening words of a
    /// document when the query matched nothing inside it — which happens whenever an email matched on its subject or a
    /// participant address — and returning that would publish the start of a message while claiming it was what
    /// matched. Both bounds are applied again here rather than trusted from the option list, because they are the
    /// privacy control and a result must not depend on the server having honored them.
    /// </remarks>
    internal static IReadOnlyList<string> Extracts(string? headline, EmailSearchSnippetBounds snippetBounds)
    {
        ArgumentNullException.ThrowIfNull(snippetBounds);

        return headline is null
            ? []
            :
            [
                .. headline
                    .Split(SnippetSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static fragment => fragment.Contains(HighlightStartMarker, StringComparison.Ordinal))
                    .Take(snippetBounds.SnippetsPerEmail)
                    .Select(fragment => Published(fragment, snippetBounds)),
            ];
    }

    /// <summary>Decides the shortest extract the server may return, which has to stay below the longest.</summary>
    /// <remarks>
    /// <c>ts_headline</c> rejects an option list whose minimum is not below its maximum, so the floor is derived from
    /// the configured length rather than configured beside it: a deployment cannot then write two numbers that make the
    /// query fail. A third of the maximum leaves room for a fragment that ends early without shrinking to a bare word.
    /// </remarks>
    private static int MinimumWordsPerSnippet(EmailSearchSnippetBounds snippetBounds) =>
        Math.Max(1, snippetBounds.WordsPerSnippet / 3);

    /// <summary>Bounds one extract by characters and puts its markers into the form a caller receives.</summary>
    /// <remarks>
    /// The character bound is what makes the word bound mean something. <c>MaxWords</c> counts words, and a word is
    /// whatever lies between two spaces, so a text carrying one enormous unbroken token beside a match — a URL, a
    /// base64 blob, a hash — satisfies a limit of a few words while publishing most of itself.
    /// </remarks>
    private static string Published(string fragment, EmailSearchSnippetBounds snippetBounds)
    {
        var bounded = BoundedToSourceCharacters(fragment, snippetBounds.MaximumCharacters);

        return Closed(bounded)
            .Replace(HighlightStartMarker, PublishedHighlightMarker, StringComparison.Ordinal)
            .Replace(HighlightEndMarker, PublishedHighlightMarker, StringComparison.Ordinal);
    }

    /// <summary>Cuts an extract once it has carried as many characters of the source as the bound allows.</summary>
    /// <remarks>
    /// The markers are not counted, because the bound exists to limit how much of a message one result publishes and a
    /// marker is MailFathom's own. Counting them would also make the bound depend on how often the query matched inside
    /// the extract, so the same setting would show less of a message the better it matched — which is the opposite of
    /// what a reader wants and tells an operator nothing about what the number protects.
    /// </remarks>
    private static string BoundedToSourceCharacters(string fragment, int maximumCharacters)
    {
        var sourceCharacters = 0;
        var index = 0;

        while (index < fragment.Length && sourceCharacters < maximumCharacters)
        {
            if (!IsMarker(fragment[index]))
            {
                sourceCharacters++;
            }

            index++;
        }

        return index == fragment.Length ? fragment : string.Concat(fragment[..index], TruncationMarker);
    }

    private static bool IsMarker(char character) =>
        character == HighlightStartMarker[0] || character == HighlightEndMarker[0];

    /// <summary>Closes a highlight the character bound cut in half, so the published markers stay paired.</summary>
    /// <remarks>
    /// Truncation can land between the two control markers, and the published marker is the same string at both ends —
    /// so an unclosed run would leave a client emphasizing the rest of the extract rather than the words that matched.
    /// </remarks>
    private static string Closed(string fragment) =>
        fragment.Count(character => character == HighlightStartMarker[0])
        > fragment.Count(character => character == HighlightEndMarker[0])
            ? string.Concat(fragment, HighlightEndMarker)
            : fragment;
}
