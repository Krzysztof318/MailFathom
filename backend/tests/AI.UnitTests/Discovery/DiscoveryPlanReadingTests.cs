// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what a model's answer is read as, and what is run when it cannot be read at all.</summary>
/// <remarks>
/// Every case here is a pure function of the text, which is what lets a derivation be asserted rather than observed:
/// the answers a provider produces once in a thousand runs are ordinary examples in this class.
/// </remarks>
public sealed class DiscoveryPlanReadingTests
{
    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    /// <summary>The instant the turn stated, which every bound a model writes is read against.</summary>
    /// <remarks>
    /// Deliberately not UTC. A bound the model writes carries no zone, so an anchor at offset zero would make a reading
    /// that reattached nothing indistinguishable from one that reattached the right thing — which is what the bound
    /// cases here exist to tell apart.
    /// </remarks>
    private static readonly DateTimeOffset AskedAt = new(2026, 7, 8, 9, 30, 0, TimeSpan.FromHours(2));

    private static readonly MailQuestionText Question =
        MailQuestionText.Create("which supplier quoted least for the racking");

    [Fact]
    public void Read_AWellFormedAnswer_ReadsTheIntentTheLookupsAndWhatIsEnough()
    {
        // Arrange
        const string answer = """
            {
              "intent": "compareTerms",
              "sufficientPassages": 8,
              "lookups": [
                { "queryText": "quotation racking", "senderAddress": "sales@example.test", "hasAttachments": true },
                { "queryText": "oferta regały" }
              ]
            }
            """;

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.True(outcome.WasRead);
        Assert.Equal(DiscoveryIntent.CompareTerms, outcome.Plan.Intent);
        Assert.Equal(
            ["quotation racking", "oferta regały"],
            outcome.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
        Assert.Equal("sales@example.test", outcome.Plan.Retrieval.Lookups[0].SenderAddress);
        Assert.True(outcome.Plan.Retrieval.Lookups[0].HasAttachments);
        Assert.Equal(8, outcome.Plan.Retrieval.SufficientPassages);
    }

    /// <summary>
    /// The planner resolved <em>this week</em> against the wall clock its turn stated, so the days it wrote back are
    /// that person's days and the reading is what puts them back on their own offset. Read any other way they are the
    /// same numbers against somebody else's day, which is the drift the anchor exists to remove.
    /// </summary>
    [Fact]
    public void Read_ALookupBoundedByDates_ReadsEachBoundAgainstTheAnchorTheTurnStated()
    {
        // Arrange
        const string answer = """
            {
              "intent": "findFact",
              "lookups": [
                {
                  "queryText": "invoice",
                  "receivedOnOrAfter": "2026-07-06T00:00",
                  "receivedBefore": "2026-07-13T00:00"
                }
              ]
            }
            """;

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        var lookup = Assert.Single(outcome.Plan.Retrieval.Lookups);

        Assert.Equal(new DateTimeOffset(2026, 7, 6, 0, 0, 0, AskedAt.Offset), lookup.ReceivedOnOrAfter);
        Assert.Equal(new DateTimeOffset(2026, 7, 13, 0, 0, 0, AskedAt.Offset), lookup.ReceivedBefore);
    }

    /// <summary>
    /// A model that wrote a zone invented one, because its turn stated none. The lookup still runs — its words are
    /// what the question was about — and it runs unbounded rather than bounded by a week somebody else was having.
    /// </summary>
    [Theory]
    [InlineData("2026-07-06T00:00Z")]
    [InlineData("2026-07-06T00:00+05:00")]
    [InlineData("the sixth of July")]
    public void Read_ALookupBoundedByAnInstantThisBuildCannotBelieve_RunsTheLookupUnbounded(string written)
    {
        // Arrange
        var answer = $$"""{"intent": "findFact", "lookups": [{"queryText": "invoice", "receivedOnOrAfter": "{{written}}"}]}""";

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        var lookup = Assert.Single(outcome.Plan.Retrieval.Lookups);

        Assert.Equal("invoice", lookup.QueryText);
        Assert.Null(lookup.ReceivedOnOrAfter);
    }

    /// <summary>A model told to answer with one object still fences it.</summary>
    [Fact]
    public void Read_AnAnswerInsideACodeFence_StillReadsThePlan()
    {
        // Arrange
        var answer = string.Join(
            Environment.NewLine,
            "```json",
            """{"intent": "findFact", "lookups": [{"queryText": "invoice"}]}""",
            "```");

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.True(outcome.WasRead);
        Assert.Equal(DiscoveryIntent.FindFact, outcome.Plan.Intent);
        Assert.Equal(["invoice"], outcome.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A model told to answer with one object still writes a sentence around it.</summary>
    [Fact]
    public void Read_AnAnswerSurroundedByProse_StillReadsThePlan()
    {
        // Arrange
        const string answer =
            """Here is the plan: {"intent": "findFact", "lookups": [{"queryText": "invoice"}]} — I hope it helps.""";

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.True(outcome.WasRead);
        Assert.Equal(["invoice"], outcome.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A question of no named kind is answered rather than refused, opening with an answer and its evidence.</summary>
    [Fact]
    public void Read_AnIntentThisCatalogueDoesNotHold_IsReadAsUnclassifiedAndStillPlans()
    {
        // Arrange
        const string answer = """{"intent": "summarise", "lookups": [{"queryText": "invoice"}]}""";

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.True(outcome.WasRead);
        Assert.Equal(DiscoveryIntent.Unclassified, outcome.Plan.Intent);
        Assert.Equal(
            [PresentationBlockType.Answer, PresentationBlockType.EvidenceList],
            outcome.Plan.Composition);
    }

    /// <summary>Nothing readable is still a run: the question's own words are what a system with no model would search for.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot help with that.")]
    [InlineData("{ not json at all ")]
    [InlineData("""{"intent": "findFact", "lookups": []}""")]
    [InlineData("""{"intent": "findFact", "lookups": [{"queryText": "   "}]}""")]
    public void Read_AnAnswerThisBuildCannotBelieve_FallsBackToTheQuestionsOwnWords(string? answer)
    {
        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.False(outcome.WasRead);
        Assert.Equal(DiscoveryIntent.Unclassified, outcome.Plan.Intent);
        Assert.Equal([Question.Value], outcome.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A lookup whose words could not be searched for is dropped, and the rest of the plan still runs.</summary>
    [Fact]
    public void Read_ALookupWhoseWordsExceedWhatCanBeSearchedFor_IsDroppedFromThePlan()
    {
        // Arrange
        var overLong = new string('a', EmailSearchQueryText.MaximumLength + 1);
        var answer = $$"""
            {"intent": "findFact", "lookups": [{"queryText": "{{overLong}}"}, {"queryText": "invoice"}]}
            """;

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.True(outcome.WasRead);
        Assert.Equal(["invoice"], outcome.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>More lookups than a plan may run is a model misjudging the question, not a plan to throw away.</summary>
    [Fact]
    public void Read_MoreLookupsThanAPlanMayRun_KeepsTheBestOnesInOrder()
    {
        // Arrange
        var wordings = Enumerable
            .Range(0, RetrievalPlan.MaximumLookups + 2)
            .Select(position => $$"""{"queryText": "wording {{position}}"}""");
        var answer = $$"""{"intent": "findFact", "lookups": [{{string.Join(",", wordings)}}]}""";

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.Equal(RetrievalPlan.MaximumLookups, outcome.Plan.Retrieval.Lookups.Count);
        Assert.Equal("wording 0", outcome.Plan.Retrieval.Lookups[0].QueryText);
    }

    /// <summary>A number outside the bound is clamped to what retrieval would have applied anyway.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    public void Read_EnoughBelowWhatIsUsable_IsClampedRatherThanRefused(int asked, int expected)
    {
        // Arrange
        var answer = $$"""{"intent": "findFact", "sufficientPassages": {{asked}}, "lookups": [{"queryText": "invoice"}]}""";

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.Equal(expected, outcome.Plan.Retrieval.SufficientPassages);
    }

    [Fact]
    public void Read_EnoughAboveWhatRetrievalReturns_IsClampedToThatBound()
    {
        // Arrange
        var answer = $$"""
            {"intent": "findFact", "sufficientPassages": {{Bounds.MaximumPassages + 40}}, "lookups": [{"queryText": "invoice"}]}
            """;

        // Act
        var outcome = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.Equal(Bounds.MaximumPassages, outcome.Plan.Retrieval.SufficientPassages);
    }

    /// <summary>Reading is a function of the text alone, which is what makes a derivation reproducible.</summary>
    [Fact]
    public void Read_TheSameAnswerTwice_ProducesTheSamePlan()
    {
        // Arrange
        const string answer = """
            {"intent": "trackChange", "sufficientPassages": 6, "lookups": [{"queryText": "delivery date"}]}
            """;

        // Act
        var first = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);
        var second = DiscoveryPlanReading.Read(answer, Question, Bounds, AskedAt);

        // Assert
        Assert.Equal(first.Plan.Intent, second.Plan.Intent);
        Assert.Equal(first.Plan.Composition, second.Plan.Composition);
        Assert.Equal(
            first.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText),
            second.Plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A question may be twice the length a query is allowed to be, so the fallback cuts it rather than failing.</summary>
    [Fact]
    public void Fallback_AQuestionLongerThanAQueryMayBe_IsCutToWhatCanBeSearchedFor()
    {
        // Arrange
        var longQuestion = MailQuestionText.Create(new string('a', MailQuestionText.MaximumLength));

        // Act
        var plan = DiscoveryPlanReading.Fallback(longQuestion, Bounds);

        // Assert
        Assert.Equal(EmailSearchQueryText.MaximumLength, plan.Retrieval.Lookups[0].QueryText.Length);
    }
}
