// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Retrieval;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether retrieval puts its candidates to the model before handing them over, and what that pass may spend.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, because the pass judges with the declared chat endpoint and
/// has nowhere to send a question without one. An operator who removes the chat section has removed this with it, which
/// is the honest reading of what they did.
/// </para>
/// <para>
/// Off by default, and off is not a lesser deployment. Retrieval then hands over the fused ranking exactly as hybrid
/// search produced it — cheaper, faster, and fully deterministic — which is what every instance did before this existed
/// and what an instance that never writes this block goes on doing.
/// </para>
/// <para>
/// The two numbers are a spend decision as much as a quality one. Judging costs one provider call per candidate on every
/// lookup a question makes, so an instance answering many questions over a large candidate count is buying ranking
/// quality per question at a rate only its operator can weigh.
/// </para>
/// </remarks>
internal sealed class PassageRelevanceFilterOptions
{
    /// <summary>How many turns one judgement sends: the instruction, and the candidate beside its query.</summary>
    private const int JudgementTurnCount = 2;

    /// <summary>Gets or sets whether retrieval judges its candidates before handing them over.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the greatest number of passages one retrieval puts to the model, or nothing to judge every passage it hands over.</summary>
    /// <remarks>
    /// The ceiling on what one lookup costs, in latency as much as in spend. Set below what retrieval returns it buys a
    /// weaker filter rather than a shorter result: a passage nobody judged keeps the place the fused ranking gave it.
    /// Absent rather than defaulted to a number, because the value it would default to is
    /// <c>MailAnswering:MaxPassagesPerRetrieval</c> — a setting in another section, so a literal written here would go
    /// on saying eight after an operator narrowed the retrieval it was meant to follow.
    /// </remarks>
    public int? MaxCandidates { get; set; }

    /// <summary>Gets or sets the least relevance a judged passage may carry and still be handed over.</summary>
    /// <remarks>Stated on the scale the model answers on. Half of it is a starting point rather than a recommendation: how much of an answer an extract has to hold depends on the mail an instance actually carries.</remarks>
    public int MinimumRelevance { get; set; } = 50;

    /// <summary>Reports every reason this pass could not run, by reading the declaration alone.</summary>
    /// <param name="endpointAlias">The chat endpoint the pass judges with, so a report names it.</param>
    /// <param name="maximumMessagesPerRequest">What the endpoint's own declaration allows one request to carry.</param>
    /// <returns>One result per rule this declaration breaks.</returns>
    /// <remarks>
    /// <para>
    /// The turn count is checked here rather than left to the first judgement, because a judgement is an instruction and
    /// a candidate — two turns — and an endpoint declared to carry one would refuse every one of them. That is a pair of
    /// settings nobody reads together, which is exactly the kind of contradiction startup exists to report.
    /// </para>
    /// <para>
    /// The upper bound on the candidate count is not checked here, because it is
    /// <c>MailAnswering:MaxPassagesPerRetrieval</c> and this type sees one section.
    /// <see cref="PassageRelevanceCandidateAgreement" /> reports it, from the composition root that holds both.
    /// </para>
    /// </remarks>
    public IEnumerable<ValidationResult> FindConfigurationErrors(string endpointAlias, int maximumMessagesPerRequest)
    {
        if (!this.Enabled)
        {
            yield break;
        }

        if (this.MaxCandidates is < 1)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{endpointAlias}' enables the relevance filter with a MaxCandidates below 1, so every lookup would judge nothing. Remove the key to judge every passage a retrieval hands over.",
                [nameof(this.MaxCandidates)]);
        }

        if (this.MinimumRelevance is <= PassageRelevanceFilterPlan.LeastRelevance
            or > PassageRelevanceFilterPlan.GreatestRelevance)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{endpointAlias}' enables the relevance filter with MinimumRelevance outside {PassageRelevanceFilterPlan.LeastRelevance + 1} to {PassageRelevanceFilterPlan.GreatestRelevance}. A threshold of {PassageRelevanceFilterPlan.LeastRelevance} would pay for a judgement that can drop nothing.",
                [nameof(this.MinimumRelevance)]);
        }

        if (maximumMessagesPerRequest < JudgementTurnCount)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{endpointAlias}' enables the relevance filter and declares MaxMessagesPerRequest below {JudgementTurnCount}, so every judgement would be refused before it was sent.",
                [nameof(this.Enabled)]);
        }
    }
}
