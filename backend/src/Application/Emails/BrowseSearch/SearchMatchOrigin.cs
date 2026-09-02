// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>Which ranking found one result, which is what a screen shows as why the message is in the list.</summary>
/// <remarks>
/// <para>
/// It exists because the extracts alone cannot answer the question. A snippet is cut around the words the query
/// carried, so a message ranked by meaning and carrying none of them has no extract to show — and a row appearing in a
/// list with nothing under it reads as an unexplained result rather than as a semantic one. This says which it is in
/// one word a person can act on.
/// </para>
/// <para>
/// It describes the result rather than the search. <see cref="Search.EmailSearchRetrievalMode" /> says how the whole
/// page was ranked, and every result of a lexically ranked page is <see cref="LexicalRanking" /> by construction; on a
/// hybrid page the three values are what separate the messages that carry the query's words from the ones that carry
/// its meaning, and from those the two rankings agreed on.
/// </para>
/// </remarks>
public enum SearchMatchOrigin
{
    /// <summary>The full-text ranking found it, and the semantic one did not — or the page was not ranked semantically at all.</summary>
    LexicalRanking = 0,

    /// <summary>The semantic ranking found it and the full-text one did not, so the message matched by meaning rather than by any word the query carried.</summary>
    SemanticRanking = 1,

    /// <summary>Both rankings found it, which is the strongest agreement a hybrid search reports and what places such a result near the top.</summary>
    BothRankings = 2,
}
