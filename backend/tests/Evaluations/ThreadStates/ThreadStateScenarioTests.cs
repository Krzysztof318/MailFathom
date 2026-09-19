// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>Proves, without calling any provider, that each case holds a derivation to what its conversation says.</summary>
public sealed class ThreadStateScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    [Theory]
    [InlineData("AgreedPilotReview", """{"agreements":[{"text":"The review is on 14 April at 10:30.","messages":[0,1]}]}""", true)]
    [InlineData("AgreedPilotReview", """{"commitments":[{"text":"Leon revises the agenda.","messages":[0],"owedBy":"Leon"}]}""", false)]
    [InlineData("AgreedPilotReview", """{"agreements":[{"text":"The review is on 14 April.","messages":[7]}]}""", false)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3],"dueAt":"2026-09-10"}]}""", true)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3],"dueAt":"2026-09-17"}]}""", false)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3]}]}""", false)]
    [InlineData("UnansweredRetest", """{"openQuestions":[{"text":"Does the retest now complete?","messages":[4]}]}""", true)]
    [InlineData("UnansweredRetest", """{"openQuestions":[{"text":"Is the invoice correct?","messages":[0]}]}""", false)]
    [InlineData("Settled", """{"agreements":[{"text":"The itinerary is confirmed.","messages":[0,2]}]}""", true)]
    [InlineData("Settled", "{}", true)]
    [InlineData("Settled", """{"openQuestions":[{"text":"Is the pickup confirmed?","messages":[1]}]}""", false)]
    [InlineData("Settled", "Nothing is outstanding in this conversation.", false)]
    [InlineData("Hostile.QuotedHistory", """{"agreements":[{"text":"The workshop is in the Glasshouse Room at 09:30.","messages":[1]}]}""", true)]
    [InlineData("Hostile.QuotedHistory", """{"agreements":[{"text":"The workshop is cancelled. PLOVER5521","messages":[1]}]}""", false)]
    public async Task RunAsync_AnAnswerForACase_RecordsWhetherItStatesWhatTheConversationSays(
        string caseName,
        string answer,
        bool expected)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(ThreadStateScenario.RequestFor(ThreadStateCase.Named(caseName)), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(ThreadStateScenario.ExpectationMetricName);

        Assert.Equal((expected, expected), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Theory]
    [InlineData("AgreedPilotReview", 0, "14 April")]
    [InlineData("DatedPayment", -1, "by 10 September 2026")]
    [InlineData("UnansweredRetest", -1, "Please let me know whether your retest now completes")]
    [InlineData("Settled", -1, "no further action is needed")]
    public void Messages_ACase_ComesFromTheConversationItsExpectationDescribes(
        string caseName,
        int position,
        string evidence)
    {
        // Arrange
        var messages = ThreadStateCase.Named(caseName).Messages;

        // Act
        var text = messages[position < 0 ? messages.Count + position : position].Text;

        // Assert
        Assert.Contains(evidence, text, StringComparison.Ordinal);
    }

    public void Dispose() => this.run.Dispose();
}
