// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Answering.Audit;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What one run's retrieval reached across every lookup it made.</summary>
/// <param name="Passages">The passages that reached the model, in the order the run retrieved them.</param>
/// <param name="CandidateCount">How many candidates the run's lookups ranked, before any relevance filtering narrowed them.</param>
/// <param name="RelevantCandidateCount">How many of those candidates survived being judged, which equals <paramref name="CandidateCount" /> where nothing judged them.</param>
/// <param name="Degradation">The ways the run read less of the mailbox than an undegraded run of the same question would.</param>
/// <remarks>
/// <para>
/// Three counts rather than one, because they narrow for three different reasons and only the pairs between them say
/// anything. The candidates are what the queries resembled; the relevant ones are what a deployment's second pass
/// decided actually answered; the passages are what the run's own ceiling on retrieved mail then allowed to leave the
/// process. A dashboard showing only the last of them cannot tell a question that found little from one that was stopped
/// from sending much.
/// </para>
/// <para>
/// It is summed over the run rather than reported per lookup, because a run is what a person asked and what an operator
/// or an audit asks about afterwards. A model decides how many lookups to make, so a per-lookup figure describes a
/// decision nobody took.
/// </para>
/// </remarks>
public sealed record MailAnsweringRetrievalReport(
    IReadOnlyList<EmailKnowledgePassage> Passages,
    int CandidateCount,
    int RelevantCandidateCount,
    MailAnsweringRunDegradation Degradation)
{
    /// <summary>Gets the report of a run that made no lookup at all.</summary>
    /// <remarks>
    /// The starting value and the honest ending for a question that needed no mail — "what can you do" costs one
    /// provider call and reads no mailbox — as well as for a run that failed before its first lookup. Nothing here
    /// distinguishes the two, because the outcome recorded beside it already does.
    /// </remarks>
    public static MailAnsweringRetrievalReport Empty { get; } =
        new([], CandidateCount: 0, RelevantCandidateCount: 0, MailAnsweringRunDegradation.None);
}
