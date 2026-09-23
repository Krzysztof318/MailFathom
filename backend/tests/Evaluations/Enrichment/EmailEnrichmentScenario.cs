// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Languages;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>Puts one message to the enrichment agent and holds its marks to what the message says.</summary>
/// <remarks>
/// The agent is composed by the composition a deployment uses, with the instruction, the turn, and the reading a
/// deployment uses, so a verdict here is about the prompt and the model rather than about a copy of either. What it
/// leaves out is what decides whether a derivation happens rather than what it says: the spend ledger, the egress guard,
/// and the fallback chain. Marks are a structure and every case asks what that structure settles, so no case is judged;
/// every answer is also held to carrying out nothing a message asked of it, which only a hostile case invites.
/// </remarks>
internal static class EmailEnrichmentScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "EmailEnrichment";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Marks as expected";

    /// <summary>Describes one message as the case the shared scenario runs.</summary>
    /// <param name="scenario">The message.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(EmailEnrichmentCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var message = scenario.Message().Enrichable;
        var language = scenario.Language ?? MailAccountLanguage.English;
        var instruction = EmailEnrichmentInstructions.TextFor(language);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            instruction,
            (plan, cancellationToken) => EmailEnrichmentAgent.ComposeTurnAsync(
                message,
                SensitiveContentEgressGuards.Inactive(),
                plan,
                cancellationToken),
            (model, plan) => EmailEnrichmentAgentComposition.Compose(
                model,
                plan,
                language,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => HostileMail.Obeyed(answer, instruction) ?? (StructuredAnswerScenario.IsReadableObject(answer)
                ? Held(scenario, EmailEnrichmentReading.Read(answer, message.Passages, EmailEnrichmentAgentComposition.AgentName))
                : "the answer holds no JSON object, so the message would be listed with no reading."),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }

    /// <summary>Holds the marks to the case's expectation, then the tasks read beside them, then the language the case declares.</summary>
    /// <remarks>
    /// The language is held against the tasks as well as the marks, both being sentences this derivation wrote for one
    /// mailbox: a task offered in the message's language on a list kept in another reads as somebody else's row.
    /// </remarks>
    private static string? Held(EmailEnrichmentCase scenario, EmailEnrichmentDerivation derivation) =>
        scenario.Expectation(derivation.Marks)
        ?? scenario.TaskExpectation?.Invoke(derivation.Tasks)
        ?? (scenario.Language is { } language
            ? WrittenLanguage.Shortfall(
                string.Join('\n', derivation.Marks.Select(static mark => mark.Text).Concat(derivation.Tasks.Select(static task => task.Title))),
                language)
            : null);
}
