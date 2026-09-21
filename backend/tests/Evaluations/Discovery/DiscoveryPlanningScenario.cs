// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Discovery;

/// <summary>Puts one question to the Discover planning agent and holds the plan it is read into to what the question said.</summary>
/// <remarks>
/// The turn is the one a Discover run composes and the plan is read through <see cref="DiscoveryPlanReading" />, so a
/// plan here is the plan a run would follow. A plan the answer could not be read into is a shortfall of its own, because a
/// run falls back to the question's own words then and nothing the model wrote is followed.
/// </remarks>
internal static class DiscoveryPlanningScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "DiscoveryPlanning";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Plan as expected";

    /// <summary>The scope every question is asked within: the whole of one mail account.</summary>
    /// <remarks>It reaches the model only as a count, so which account it names does not matter.</remarks>
    private static readonly MailboxScope WholeMailbox = MailboxScope.Create([MailAccountId.Create("primary")], []);

    /// <summary>The instant every question is asked at, which is the anchor the turn states.</summary>
    /// <remarks>Stated by the scenario rather than read from a clock, for the reason every evaluation input is fixed: a case resolving <em>this week</em> against today would score differently every day it is run.</remarks>
    private static readonly DateTimeOffset AskedAt = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Describes one question as the case the shared scenario runs.</summary>
    /// <param name="scenario">The question.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(DiscoveryPlanningCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            DiscoveryPlanningInstructions.Text,
            DiscoveryPlanningInstructions.ComposePlanningTurn(scenario.Question, WholeMailbox, AskedAt, EmailKnowledgeBounds.Default),
            static (model, plan) => DiscoveryPlanningAgentComposition.Compose(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => ShortfallOf(answer, scenario),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(scenario.IsAmbiguous));
    }

    private static string? ShortfallOf(string answer, DiscoveryPlanningCase scenario)
    {
        var reading = DiscoveryPlanReading.Read(
            answer,
            MailQuestionText.Create(scenario.Question),
            EmailKnowledgeBounds.Default,
            AskedAt);

        return reading.WasRead
            ? scenario.Expectation(reading.Plan)
            : "the answer could not be read as a plan, so the run fell back to the question's own words.";
    }
}
