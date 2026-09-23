// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Agent.Answering;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Writes a compaction's summary over the model under test, the way the deployment's summariser writes one over its own client.</summary>
/// <remarks>
/// The deployment's summariser opens a provider client of its own rather than taking one, so this is the one piece a
/// scenario stands in for. What it runs is the summariser's own: its turn, its request bound, its composition, and its
/// reading of the answer, under a guard that is inactive and would hand the turn back unchanged.
/// </remarks>
/// <param name="model">The model under test's client.</param>
/// <param name="plan">The plan the model is measured with.</param>
internal sealed class ModelAgentConversationSummarizer(IChatClient model, ChatGenerationPlan plan) : IAgentConversationSummarizer
{
    /// <summary>Gets whether a compaction asked for a summary at all.</summary>
    public bool WasAsked { get; private set; }

    /// <summary>Gets the summary the compaction kept, or <see langword="null" /> where none was asked for or the model wrote none.</summary>
    public string? Summary { get; private set; }

    /// <inheritdoc />
    public async Task<string?> SummarizeAsync(
        string? previousSummary,
        IReadOnlyList<AgentHistoryTurn> turns,
        CancellationToken cancellationToken)
    {
        this.WasAsked = true;

        var turn = AgentConversationSummaryInstructions.ComposeTurn(previousSummary, turns);

        ModelsUnderTest.RequireOneTurn(turn, plan);

        var agent = AgentConversationSummaryComposition.Compose(model, plan, new EmptyAgentInstructionEnvelope(), NullLoggerFactory.Instance);
        var response = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        this.Summary = AgentConversationSummarizer.SummaryOf(response.Text);

        return this.Summary;
    }
}
