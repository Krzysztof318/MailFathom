// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Both Discover agents over one model under test, as the two halves a run is joined through.</summary>
/// <remarks>
/// Each half composes its turn, its agent, and its reading the way a deployment's does, so the run they are joined into
/// follows the plan a deployment would follow and returns the result a deployment would return. What each leaves out is
/// what decides whether its call happens rather than what it answers — the ledgers, the egress guard, and the fallback
/// chain — which is the same line every other scenario here draws.
/// </remarks>
/// <param name="model">The model under test, already behind the run's response cache.</param>
/// <param name="generation">The generation plan both agents are composed with, which is what keeps them on one model.</param>
internal sealed class DiscoveryAgentsUnderTest(IChatClient model, ChatGenerationPlan generation)
    : IDiscoveryRunPlanner, IDiscoveryResultComposer
{
    /// <summary>Gets whether the planning answer was read as a plan, or <see langword="null" /> before the plan was asked for.</summary>
    /// <remarks>A plan that was not read is the question's own words, which is still a plan a run follows.</remarks>
    public bool? PlanWasRead { get; private set; }

    /// <summary>Gets what the composing agent wrote, or <see langword="null" /> where it wrote nothing or was not asked.</summary>
    public string? Composition { get; private set; }

    /// <inheritdoc />
    public async Task<DiscoveryRunPlan> DerivePlanAsync(MailQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var agent = DiscoveryPlanningAgentComposition.Compose(model, generation, new EmptyAgentInstructionEnvelope(), NullLoggerFactory.Instance);
        var answer = await agent.RunAsync(
            DiscoveryPlanningInstructions.ComposePlanningTurn(question.Text.Value, question.Scope, EmailKnowledgeBounds.Default),
            session: null,
            options: null,
            cancellationToken);

        var reading = DiscoveryPlanReading.Read(answer.Text, question.Text, EmailKnowledgeBounds.Default);
        this.PlanWasRead = reading.WasRead;

        return reading.Plan;
    }

    /// <inheritdoc />
    public async Task<PresentationPlan> ComposeAsync(
        MailQuestion question,
        DiscoveryRunPlan plan,
        DiscoveryEvidence evidence,
        IReadOnlyList<AccountCoverage> coverage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);

        var sources = DiscoveryComposedSources.Declare(evidence.Passages);
        var turn = DiscoveryCompositionInstructions.ComposeCompositionTurn(
            question.Text.Value,
            plan.Intent,
            [.. sources.Select(static source => new DiscoveryTurnSource(source.Citation.Id.Value, source.Citation.Label.Value, source.Extract))]);

        var agent = DiscoveryCompositionAgentComposition.Compose(model, generation, new EmptyAgentInstructionEnvelope(), NullLoggerFactory.Instance);
        var answer = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        // A blank answer reaches the reading as no answer, as it does from the deployment's composing agent.
        this.Composition = string.IsNullOrWhiteSpace(answer.Text) ? null : answer.Text;

        return DiscoveryCompositionReading.Read(this.Composition, plan, sources, evidence, coverage);
    }
}
