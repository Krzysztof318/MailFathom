// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval;

/// <summary>What one lookup found, and what it considered before it got there.</summary>
/// <param name="Passages">The passages the lookup hands over, most relevant first.</param>
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
/// An implementation that judges nothing reports a candidate count equal to what it hands over and no fallback, which
/// <see cref="Unfiltered" /> is what states — so the pair reads the same way whether or not a deployment turned a second
/// pass on, instead of a caller having to know which shape of retrieval it is holding.
/// </para>
/// </remarks>
public sealed record EmailKnowledgeLookup(
    IReadOnlyList<EmailKnowledgePassage> Passages,
    int CandidateCount,
    bool RelevanceFilterFellBack)
{
    /// <summary>Reports a lookup that judged nothing, so everything it ranked is everything it hands over.</summary>
    /// <param name="passages">The passages the ranking produced.</param>
    /// <returns>The lookup.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A factory rather than a defaulted constructor because it is what keeps the counts honest: an unfiltered lookup
    /// whose candidate count disagreed with its passage count would report a filter that does not exist as having
    /// dropped mail.
    /// </remarks>
    public static EmailKnowledgeLookup Unfiltered(IReadOnlyList<EmailKnowledgePassage> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        return new EmailKnowledgeLookup(passages, passages.Count, RelevanceFilterFellBack: false);
    }
}
