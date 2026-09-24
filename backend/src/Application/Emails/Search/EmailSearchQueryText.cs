// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Search;

/// <summary>The free text a caller is searching their mail for.</summary>
/// <remarks>
/// <para>
/// The text is bounded and checked for characters no document could hold, and then travels to PostgreSQL as a parameter
/// that the full-text parser turns into a query — so a caller's operators, quotes, and punctuation are data the parser
/// reads rather than syntax anything composes with. Where any word matches, the text is split into its words, quoted
/// phrases, and excluded terms first, and what travels is those terms rejoined, still as a parameter. Nothing in this
/// type or below it concatenates the value into SQL.
/// </para>
/// <para>
/// It is a type of its own rather than a <see langword="string" /> parameter because it is the one untrusted value that
/// reaches a query as text. A named type means the bound and the refusal are stated once, and a reader of the port
/// signature can see that the text arriving there is the validated one.
/// </para>
/// <para>
/// Query text is personal data of a particularly revealing kind — what somebody is looking for in their own mailbox —
/// so it is never logged and never repeated in a failure message.
/// </para>
/// </remarks>
public sealed record EmailSearchQueryText
{
    /// <summary>The greatest number of characters a search query may carry.</summary>
    /// <remarks>
    /// Generous against any phrase a person types and far below the point where the full-text parser's cost matters.
    /// Nothing about the query grammar bounds a length on its own, so without this a caller could send a megabyte of
    /// text that PostgreSQL would dutifully parse into a query no document can match.
    /// </remarks>
    public const int MaximumLength = 512;

    /// <summary>How the words of a query are matched, written for whoever writes one.</summary>
    /// <remarks>
    /// Stated once, here, because every surface a query is written through — a published tool, an agent's lookup, a
    /// plan's search — is read by a model that otherwise writes the question itself as the query. Where meaning takes
    /// part in the ranking, every word is required and the default text search configuration stems nothing, so a
    /// six-word lookup in the nominative misses the one message that carries four of the words in another case; where
    /// words alone rank, any of them matches and the common words of a sentence crowd the ranking. Distinctive words
    /// serve both.
    /// </remarks>
    public const string MatchingDescription =
        "Two or three distinctive words — a name, a number, a place, a rare term — reach the right mail better than a "
        + "sentence does: where the search also ranks by meaning every word is required, and where it ranks by words "
        + "alone any of them matches and a message carrying more of them ranks higher. Words match as they are "
        + "written, without stemming: a language that inflects its words, such as Polish, writes one word in several "
        + "forms (Warszawa, Warszawy, Warszawie), so offer the forms or the translations you expect with OR, or search "
        + "by a name or a number that does not change.";

    private EmailSearchQueryText(string value, EmailSearchWordMatching wordMatching)
    {
        this.Value = value;
        this.WordMatching = wordMatching;
    }

    /// <summary>Gets the query text as the caller wrote it, validated.</summary>
    public string Value { get; }

    /// <summary>Gets how the words of the query decide what the full-text index matches.</summary>
    public EmailSearchWordMatching WordMatching { get; }

    /// <summary>Gets the text <c>websearch_to_tsquery</c> reads for the messages a search matches.</summary>
    /// <remarks>
    /// The query as written where every word is required. Where any word matches, its words and quoted phrases joined by
    /// <c>or</c>, without the ones it excludes: <c>websearch_to_tsquery</c> binds an exclusion to its neighbour rather
    /// than to the whole query, so an exclusion cannot stay in the same text and is carried by
    /// <see cref="ExcludedText" /> instead.
    /// </remarks>
    public string MatchedText => this.WordMatching is EmailSearchWordMatching.AnyWord ? AnyWordTerms(this.Value).Matched : this.Value;

    /// <summary>Gets the text <c>websearch_to_tsquery</c> reads for the messages a search must not match, or <see langword="null" /> where it excludes nothing.</summary>
    /// <remarks>Always <see langword="null" /> where every word is required, because the query as written already carries its exclusions.</remarks>
    public string? ExcludedText => this.WordMatching is EmailSearchWordMatching.AnyWord ? AnyWordTerms(this.Value).Excluded : null;

    /// <summary>Validates and normalizes the text a request asked to search for.</summary>
    /// <param name="text">The query text a caller supplied.</param>
    /// <returns>The validated query text.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the text is blank, longer than <see cref="MaximumLength" />, or carries a control character.</exception>
    /// <remarks>
    /// Blank text is refused rather than treated as "match everything". A search with no text is a listing, which
    /// <see cref="ListEmails.MailboxTimelineReader" /> already answers in a stable order and with a cursor; answering it
    /// here would return an arbitrary relevance-ordered window of the whole mailbox instead, and every result would
    /// carry a rank of zero and a snippet of whatever each message happens to begin with.
    /// </remarks>
    public static EmailSearchQueryText Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw MailboxQueryFilterInvalidException.Blank("search query");
        }

        var trimmed = text.Trim();

        MailboxQueryFilterInvalidException.ThrowIfLengthExceeded(trimmed.Length, MaximumLength, "search query");

        // Refused for the reason a subject fragment's control characters are: PostgreSQL text cannot hold a zero byte,
        // so a query carrying one would surface as a provider exception instead of the failure this boundary publishes.
        if (trimmed.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.ContainsControlCharacter("search query");
        }

        return new EmailSearchQueryText(trimmed, EmailSearchWordMatching.EveryWord);
    }

    /// <summary>Gets this query as a search ranking the given way matches it.</summary>
    /// <param name="retrievalMode">How the search ranks.</param>
    /// <returns>The query matching any word where the lexical ranking stands alone, and every word where it is one half of a fusion.</returns>
    /// <remarks>
    /// Decided here once, so the ranking, the extracts, and the attachment passages of one search read the same words.
    /// The PostgreSQL retrieval evaluation is what chose it: over the keyword queries models write, matching any word
    /// raised the recall of a lexical ranking standing alone and lowered a fusion's, because the common words it lets
    /// match pull unrelated mail into the lexical half, where the semantic half already reaches what every word misses.
    /// </remarks>
    public EmailSearchQueryText MatchedUnder(EmailSearchRetrievalMode retrievalMode) =>
        new(this.Value, retrievalMode is EmailSearchRetrievalMode.Lexical ? EmailSearchWordMatching.AnyWord : EmailSearchWordMatching.EveryWord);

    /// <inheritdoc />
    /// <remarks>Returns the length rather than the text, because a query is mail-derived personal data and this is what a log or a debugger would show.</remarks>
    public override string ToString() => $"search query of {this.Value.Length} characters";

    /// <summary>Splits a query into the words and phrases it matches and the ones it excludes, each joined by <c>or</c>.</summary>
    /// <remarks>
    /// A quoted phrase stays one term, a leading minus marks an excluded term, and an <c>OR</c> the query already carried
    /// is dropped, since every term is joined by one anyway — while an excluded "or" is a word somebody excluded and stays. A query that excludes and matches nothing else is matched as
    /// written, which is what every word required would have done with it.
    /// </remarks>
    private static (string Matched, string? Excluded) AnyWordTerms(string text)
    {
        var terms = Terms(text)
            .Where(static term => term.Excluded || !term.Text.Equals("or", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matched = terms.Where(static term => !term.Excluded).Select(static term => term.Text).ToArray();
        var excluded = terms.Where(static term => term.Excluded).Select(static term => term.Text).ToArray();

        return matched.Length is 0
            ? (text, null)
            : (string.Join(" or ", matched), excluded.Length is 0 ? null : string.Join(" or ", excluded));
    }

    private static IEnumerable<(string Text, bool Excluded)> Terms(string text)
    {
        var position = 0;

        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            var excluded = text[position] is '-';
            var start = excluded ? position + 1 : position;

            // An unclosed quotation runs to the end of the query, as websearch_to_tsquery reads one.
            var end = start < text.Length && text[start] is '"'
                ? QuotationEnd(text, start)
                : WordEnd(text, start);

            var term = text[start..end];

            if (term.Trim('"').Length > 0)
            {
                yield return (term, excluded);
            }

            position = end;
        }
    }

    private static int QuotationEnd(string text, int opening)
    {
        var closing = text.IndexOf('"', opening + 1);

        return closing < 0 ? text.Length : closing + 1;
    }

    private static int WordEnd(string text, int start)
    {
        var end = start;

        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return end;
    }
}
