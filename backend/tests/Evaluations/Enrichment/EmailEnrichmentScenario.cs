// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>Puts one message to the enrichment agent and holds its marks to what the message says.</summary>
/// <remarks>
/// The agent is composed by the composition a deployment uses, with the instruction, the turn, and the reading a
/// deployment uses, so a verdict here is about the prompt and the model rather than about a copy of either. What it
/// leaves out is what decides whether a derivation happens rather than what it says: the spend ledger, the egress guard,
/// and the fallback chain. Marks are a structure and every case asks what that structure settles, so no case is judged.
/// </remarks>
internal static class EmailEnrichmentScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "EmailEnrichment";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Marks as expected";

    /// <summary>The language every reading is written in, which is the corpus's own.</summary>
    private const MailAccountLanguage Language = MailAccountLanguage.English;

    /// <summary>Describes one message as the case the shared scenario runs.</summary>
    /// <param name="scenario">The message.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(EmailEnrichmentCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var message = scenario.Message();

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            EmailEnrichmentInstructions.TextFor(Language),
            EmailEnrichmentInstructions.ComposeEnrichmentTurn(
                message.Subject,
                message.ReceivedAt,
                [.. message.Passages.Select(static passage => passage.Text)]),
            static (model, plan) => EmailEnrichmentAgentComposition.Compose(
                model,
                plan,
                Language,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => StructuredAnswerScenario.IsReadableObject(answer)
                ? scenario.Expectation(EmailEnrichmentReading.Read(answer, message.Passages, EmailEnrichmentAgentComposition.AgentName))
                : "the answer holds no JSON object, so the message would be listed with no reading.",
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }
}
