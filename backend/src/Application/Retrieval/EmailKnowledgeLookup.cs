// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Retrieval;

/// <summary>What one lookup found, how it ranked it, and what it considered before it got there.</summary>
/// <param name="Passages">The passages the lookup hands over, most relevant first.</param>
/// <param name="RetrievalMode">How the eligible mail was ranked, which is the instance's own capability rather than anything the query asked for.</param>
/// <param name="CandidateCount">How many candidates the ranking produced before any relevance filtering narrowed them.</param>
/// <param name="RelevanceFilterFellBack">Whether a relevance judgement could not be made, so candidates were handed over unjudged.</param>
/// <remarks>
/// <para>
/// The two numbers beside the passages are the only way anything above this port can say what a retrieval cost a
/// mailbox: <paramref name="CandidateCount" /> is what a query resembled, <c>Passages.Count</c> is what survived being
/// judged against it, and their difference is what a deployment's relevance filter did. Neither is observable from the
/// passages alone, and both are bounded counts rather than anything about the mail they describe.
/// </para>
/// <para>
/// The mode travels with them for the reason a search publishes it to its own caller: lexical ranking finds the words a
/// query carries and hybrid ranking also finds mail whose meaning is close, so which one answered decides how a further
/// query is worth wording. It describes the instance, so it is reported even by a lookup that found nothing.
/// </para>
/// <para>
/// An implementation that judges nothing reports a candidate count equal to what it hands over and no fallback, which
/// <see cref="Unfiltered" /> is what states — so the pair reads the same way whether or not a deployment turned a second
/// pass on, instead of a caller having to know which shape of retrieval it is holding.
/// </para>
/// </remarks>
public sealed record EmailKnowledgeLookup(
    IReadOnlyList<EmailKnowledgePassage> Passages,
    EmailSearchRetrievalMode RetrievalMode,
    int CandidateCount,
    bool RelevanceFilterFellBack)
{
    /// <summary>Reports a lookup that judged nothing, so everything it ranked is everything it hands over.</summary>
    /// <param name="passages">The passages the ranking produced.</param>
    /// <param name="retrievalMode">How the ranking that produced them ranked.</param>
    /// <returns>The lookup.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A factory rather than a defaulted constructor because it is what keeps the counts honest: an unfiltered lookup
    /// whose candidate count disagreed with its passage count would report a filter that does not exist as having
    /// dropped mail.
    /// </remarks>
    public static EmailKnowledgeLookup Unfiltered(
        IReadOnlyList<EmailKnowledgePassage> passages,
        EmailSearchRetrievalMode retrievalMode)
    {
        ArgumentNullException.ThrowIfNull(passages);

        return new EmailKnowledgeLookup(
            passages,
            retrievalMode,
            passages.Count,
            RelevanceFilterFellBack: false);
    }
}
