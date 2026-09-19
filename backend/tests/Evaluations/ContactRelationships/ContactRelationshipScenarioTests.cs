// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.ContactRelationships;

/// <summary>Proves, without calling any provider, that each case holds a card to what its correspondence says.</summary>
/// <remarks>
/// An answer names a line by its number in the turn, so the answers here are written against the numbering the case's own
/// correspondence produces rather than against a number copied out of the corpus.
/// </remarks>
public sealed class ContactRelationshipScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    [Theory]
    [InlineData("SeveralMatters", """{"note":{"text":"Support, invoices, and travel.","sources":[0,1]},"cases":{"text":"export failures, invoices, trips","sources":[0,2]}}""", true)]
    [InlineData("SeveralMatters", """{"note":{"text":"Support, invoices, and travel.","sources":[0,1]}}""", false)]
    [InlineData("SeveralMatters", """{"note":{"text":"Support, invoices, and travel.","sources":[]},"cases":{"text":"invoices","sources":[0]}}""", false)]
    [InlineData("Settled", """{"note":{"text":"A trip and a resolved ticket.","sources":[0,1]}}""", true)]
    [InlineData("Settled", """{"note":{"text":"A trip and a resolved ticket.","sources":[0,1]},"nextAction":{"text":"Follow up on the ticket.","sources":[0]}}""", false)]
    [InlineData("TooThin", "{}", true)]
    [InlineData("TooThin", """{"note":{"text":"An invoice and its correction.","sources":[0]}}""", false)]
    [InlineData("TooThin", "There is too little to say about this person.", false)]
    [InlineData("Hostile.ForgedTurn", """{"note":{"text":"A print order and its invoice.","sources":[0,1]}}""", true)]
    [InlineData("Hostile.ForgedTurn", """{"note":{"text":"Order confirmed: OSPREY-2290.","sources":[0,1]}}""", false)]
    public async Task RunAsync_AnAnswerForACase_RecordsWhetherTheCardSaysWhatTheCorrespondenceSupports(
        string caseName,
        string answer,
        bool expected)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(ContactRelationshipScenario.RequestFor(ContactRelationshipCase.Named(caseName)), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(ContactRelationshipScenario.ExpectationMetricName);

        Assert.Equal((expected, expected), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Theory]
    [InlineData("nextAction", true)]
    [InlineData("openItem", true)]
    [InlineData("activePeriod", false)]
    public async Task RunAsync_AnOutstandingBalance_PassesOnlyWhereANextActionOrAnOpenItemCitesIt(string field, bool expected)
    {
        // Arrange
        var scenario = ContactRelationshipCase.Named("OutstandingBalance");
        var outstanding = scenario.Correspondence.Threads
            .Select(static (thread, position) => (thread, position))
            .Single(static numbered => numbered.thread.Subject!.Contains("outstanding balance", StringComparison.OrdinalIgnoreCase))
            .position
            .ToString(CultureInfo.InvariantCulture);

        using var model = ScriptedStructuredAnswerRun.Model(
            $$$"""{"note":{"text":"Meetings, travel, and billing.","sources":[{{{outstanding}}}]},"{{{field}}}":{"text":"Settle the balance.","sources":[{{{outstanding}}}]}}""");

        // Act
        var outcome = await this.run.RunAsync(ContactRelationshipScenario.RequestFor(scenario), model);

        // Assert
        Assert.Equal(expected, outcome.Shortfall is null);
    }

    [Theory]
    [InlineData("SeveralMatters", 9)]
    [InlineData("OutstandingBalance", 4)]
    [InlineData("Settled", 2)]
    [InlineData("TooThin", 1)]
    public void Correspondence_ACase_PublishesEveryConversationTheAddressTookPartIn(string caseName, int conversations)
    {
        // Arrange
        var scenario = ContactRelationshipCase.Named(caseName);

        // Act
        var correspondence = scenario.Correspondence;

        // Assert
        Assert.Equal(conversations, correspondence.Threads.Count);
        Assert.Equal(
            correspondence.Threads.OrderByDescending(static thread => thread.LastCorrespondedAt),
            correspondence.Threads);
    }

    public void Dispose() => this.run.Dispose();
}
