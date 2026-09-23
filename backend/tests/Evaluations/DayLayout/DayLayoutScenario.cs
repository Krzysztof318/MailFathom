// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.DayLayout;
using MailFathom.AI.Orchestration;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.DayLayout;

/// <summary>Puts one day to the day-layout agent and holds the arrangement to what the day allows.</summary>
/// <remarks>
/// The agent is composed by the composition a deployment uses, with the instruction, the turn, and the reading a
/// deployment uses, so a verdict here is about the prompt and the model rather than about a copy of either. What it
/// leaves out is what decides whether a day is arranged at all rather than how it is arranged: the spend ledger, the
/// egress guard, and the fallback chain. An arrangement is a structure and every case asks what that structure
/// settles, so no case is judged; every answer is also held to carrying out nothing a task's own line asked of it,
/// which only the hostile case invites.
/// </remarks>
internal static class DayLayoutScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "DayLayout";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Arrangement as expected";

    /// <summary>Describes one day as the case the shared scenario runs.</summary>
    /// <param name="scenario">The day.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(DayLayoutCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var question = scenario.Question;
        var instruction = DayLayoutInstructions.Text;

        // The lines pass an inactive guard, an egress guard being what a deployment puts between the store and this turn
        // rather than part of what the agent is measured on.
        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            instruction,
            (plan, cancellationToken) => DayLayoutAgent.ComposeTurnAsync(
                question,
                SensitiveContentEgressGuards.Inactive(),
                plan,
                cancellationToken),
            (model, plan) => DayLayoutAgentComposition.Compose(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => HostileMail.Obeyed(answer, instruction) ?? (StructuredAnswerScenario.IsReadableObject(answer)
                ? scenario.Expectation(question, DayLayoutReading.Read(answer, question))
                : "the answer holds no JSON object, so the person would be shown no arrangement at all."),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }
}
