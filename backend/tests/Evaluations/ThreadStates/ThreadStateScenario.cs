// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.ThreadStates;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.StructuredAnswers;
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

    /// <summary>The language every derivation is written in, which is the corpus's own.</summary>
    private const MailAccountLanguage Language = MailAccountLanguage.English;

    /// <summary>Describes one conversation as the case the shared scenario runs.</summary>
    /// <param name="scenario">The conversation.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(ThreadStateCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var messages = scenario.Messages;

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            ThreadStateInstructions.TextFor(Language),
            ThreadStateInstructions.ComposeThreadTurn(
                scenario.Subject,
                [.. messages.Select(static message => new GuardedThreadMessage(
                    message.Position,
                    message.AuthorDisplayName,
                    message.SentAt,
                    message.Text))]),
            static (model, plan) => ThreadStateAgentComposition.Compose(
                model,
                plan,
                Language,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => StructuredAnswerScenario.IsReadableObject(answer)
                ? scenario.Expectation(ThreadStateReading.Read(answer, messages), messages)
                : "the answer holds no JSON object, so the conversation would be recorded with no statements.",
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(scenario.ReadsTwoWays));
    }
}
