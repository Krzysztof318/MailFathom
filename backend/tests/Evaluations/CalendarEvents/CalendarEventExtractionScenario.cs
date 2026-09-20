// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.AI.Orchestration;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.CalendarEvents;

/// <summary>Puts one text to the extraction agent and holds the events it wrote to what the text says.</summary>
/// <remarks>
/// The agent is composed by the composition a deployment uses, with the instruction, the turn, and the reading a
/// deployment uses, so a verdict here is about the prompt and the model rather than about a copy of either. What it
/// leaves out is what decides whether the call happens at all rather than what it answers: the spend ledger, the egress
/// guard, and the fallback chain. An event is a structure and every case asks what that structure settles, so no case
/// is judged; every answer is also held to carrying out nothing the text asked of it, which only a hostile case invites.
/// </remarks>
internal static class CalendarEventExtractionScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "CalendarEventExtraction";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Events as expected";

    /// <summary>Describes one text as the case the shared scenario runs.</summary>
    /// <param name="scenario">The text.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(CalendarEventExtractionCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var turn = scenario.Compose();
        var instruction = CalendarEventExtractionInstructions.Text;

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            instruction,
            turn.Text,
            static (model, plan) => CalendarEventExtractionAgentComposition.Compose(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => HostileMail.Obeyed(answer, instruction) ?? (StructuredAnswerScenario.IsReadableObject(answer)
                ? scenario.Expectation(CalendarEventExtractionReading.Read(answer, turn.Anchor, turn.MaximumEvents))
                : "the answer holds no JSON object, so nothing would be offered for the text."),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }
}
