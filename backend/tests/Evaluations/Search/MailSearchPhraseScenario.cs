// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.Search;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Search;

/// <summary>Puts one sentence to the search-phrase agent and holds the reading to what the sentence stated.</summary>
/// <remarks>
/// The turn is the one a search composes and the answer is read through <see cref="MailSearchPhraseDocumentReading" />, so
/// a filter the reading refuses — an address that is not one, a period that runs backwards — is absent here exactly as it
/// would be from the search. An answer nothing survives is the reading a search falls back to the sentence's own words
/// from, which is a shortfall of its own.
/// </remarks>
internal static class MailSearchPhraseScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "MailSearchPhrase";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Reading as expected";

    /// <summary>Describes one sentence as the case the shared scenario runs.</summary>
    /// <param name="scenario">The sentence.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(MailSearchPhraseCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            MailSearchPhraseInstructions.Text,
            (_, cancellationToken) => MailSearchPhraseAgent.ComposeTurnAsync(
                new MailSearchPhrase(EmailSearchQueryText.Create(scenario.Sentence), MailSearchPhraseCase.AskedAt),
                SensitiveContentEgressGuards.Inactive(),
                cancellationToken),
            static (model, plan) => MailSearchPhraseAgentComposition.Compose(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => MailSearchPhraseDocumentReading.Read(answer) is { WasRead: true } reading
                ? scenario.Expectation(reading)
                : "nothing could be read from the answer, so the search would fall back to the sentence's own words.",
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(scenario.ReadsTwoWays));
    }
}
