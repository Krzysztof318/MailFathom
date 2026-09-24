// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search;

/// <summary>How the words of a query decide which messages the full-text index matches.</summary>
/// <remarks>
/// Which one a search uses follows from how it ranks, and <see cref="EmailSearchQueryText.MatchedUnder" /> is where that
/// is decided. The retrieval evaluation measured both: every word required keeps the lexical half of a fusion precise,
/// and any word lets a ranking that stands alone reach mail a sentence-shaped query would otherwise miss.
/// </remarks>
public enum EmailSearchWordMatching
{
    /// <summary>Every word of the query is required, and <c>ts_rank</c> orders what matched.</summary>
    EveryWord = 0,

    /// <summary>Any word or quoted phrase of the query matches, an excluded word still excludes, and <c>ts_rank_cd</c> orders what matched.</summary>
    AnyWord = 1,
}
