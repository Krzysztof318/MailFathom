// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.ThreadStates;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Languages;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>Puts one conversation to the thread-state agent and holds its statements to what the conversation says.</summary>
/// <remarks>
/// The turn is the one a derivation composes and the answer is read through <see cref="ThreadStateReading" />, so a
/// statement the reading drops — one citing no message the turn numbered — is absent here exactly as it would be on a
/// screen. Only a case that reads two ways is judged.
/// </remarks>
internal static class ThreadStateScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "ThreadState";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "State as expected";

    /// <summary>Describes one conversation as the case the shared scenario runs.</summary>
    /// <param name="scenario">The conversation.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(ThreadStateCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var thread = scenario.Thread;
        var messages = thread.Messages;
        var language = scenario.Language ?? MailAccountLanguage.English;
        var instruction = ThreadStateInstructions.TextFor(language);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            instruction,
            (plan, cancellationToken) => ThreadStateAgent.ComposeTurnAsync(
                thread,
                SensitiveContentEgressGuards.Inactive(),
                plan,
                cancellationToken),
            (model, plan) => ThreadStateAgentComposition.Compose(
                model,
                plan,
                language,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => HostileMail.Obeyed(answer, instruction) ?? (StructuredAnswerScenario.IsReadableObject(answer)
                ? Held(scenario, ThreadStateReading.Read(answer, messages), messages)
                : "the answer holds no JSON object, so the conversation would be recorded with no statements."),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(scenario.ReadsTwoWays));
    }

    /// <summary>Holds the statements to the case's expectation, then to the language the case declares.</summary>
    private static string? Held(
        ThreadStateCase scenario,
        IReadOnlyList<ThreadStateEntry> entries,
        IReadOnlyList<DerivableThreadMessage> messages) =>
        scenario.Expectation(entries, messages) ?? (scenario.Language is { } language
            ? WrittenLanguage.Shortfall(string.Join('\n', entries.Select(static entry => entry.Text)), language)
            : null);
}
