// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.UnitTests.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.AgentConversations;

/// <summary>Proves, without calling any provider, that each case holds a summary to its facts and refuses one that obeyed quoted mail.</summary>
public sealed class AgentConversationSummaryScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    public static TheoryData<string> Cases { get; } = new(AgentConversationSummaryCase.All.Select(static scenario => scenario.Name));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_ASummaryCarryingEveryFact_RecordsItAsKept(string caseName)
    {
        // Arrange
        var scenario = AgentConversationSummaryCase.Named(caseName);
        using var model = ScriptedStructuredAnswerRun.Model(string.Join("; ", scenario.Facts));

        // Act
        var outcome = await this.run.RunAsync(AgentConversationSummaryScenario.RequestFor(scenario), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(AgentConversationSummaryScenario.ExpectationMetricName);

        Assert.Equal((true, true), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunAsync_ASummaryDroppingAFact_NamesTheFactItDropped(string caseName)
    {
        // Arrange
        var scenario = AgentConversationSummaryCase.Named(caseName);
        using var model = ScriptedStructuredAnswerRun.Model(string.Join("; ", scenario.Facts.Skip(1)));

        // Act
        var outcome = await this.run.RunAsync(AgentConversationSummaryScenario.RequestFor(scenario), model);

        // Assert
        Assert.Contains(scenario.Facts[0], outcome.Shortfall, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ASummaryThatIsTheCodeQuotedMailAskedFor_RecordsThatItObeyed()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(HostileMail.Codes[0]);

        // Act
        var outcome = await this.run.RunAsync(
            AgentConversationSummaryScenario.RequestFor(AgentConversationSummaryCase.Named("QuotedMailAskingToBeObeyed")),
            model);

        // Assert
        Assert.Contains("obeyed", outcome.Shortfall, StringComparison.Ordinal);
    }

    public void Dispose() => this.run.Dispose();
}
