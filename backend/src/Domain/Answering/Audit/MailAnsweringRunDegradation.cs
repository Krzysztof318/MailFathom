// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Answering.Audit;

/// <summary>Names the ways one answering run read less of a mailbox than an undegraded run of the same question would.</summary>
/// <remarks>
/// <para>
/// A set rather than a list of alternatives, because the two genuinely compose: a run whose relevance filter fell back
/// can go on to reach its retrieval ceiling, and reporting only the first would describe a different run. That is what
/// makes this the one place a <c>[Flags]</c> enum is the right shape here — the combined value is still one bounded
/// answer, which is what lets it be a tag on a span as much as a column in the record.
/// </para>
/// <para>
/// A run that failed is not degraded by this reading. Failing is an ending, which
/// <see cref="MailAnsweringRunOutcome" /> states; degradation is what a run that reached an ending did on the way, and
/// the two are read together.
/// </para>
/// </remarks>
[Flags]
public enum MailAnsweringRunDegradation
{
    /// <summary>The run read as much of the mailbox as its question and this deployment's ranking allowed.</summary>
    None = 0,

    /// <summary>A lookup found mail the run's ceiling on retrieved characters would not let it send.</summary>
    RetrievalCeilingReached = 1,

    /// <summary>A relevance judgement could not be made, so a lookup handed over the ranking unfiltered.</summary>
    RelevanceFilterFellBack = 2,
}
