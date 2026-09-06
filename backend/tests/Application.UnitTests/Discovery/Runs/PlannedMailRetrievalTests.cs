// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Runs;

/// <summary>Covers what running a plan reads, what bounds it, and when it stops.</summary>
public sealed class PlannedMailRetrievalTests
{
    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    private static readonly MailboxScope WholeMailbox = MailboxScope.Create(
        SyntheticMailOwner.Deployment,
        [MailAccountId.Create("primary")],
        []);

    [Fact]
    public async Task RetrieveAsync_APlanOfSeveralLookups_RunsThemInTheOrderThePlanNames()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .Returning("invoice", ScriptedEmailKnowledgeSearch.Passage("the invoice"))
            .Returning("faktura", ScriptedEmailKnowledgeSearch.Passage("the faktura"));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 10, "invoice", "faktura"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["invoice", "faktura"], search.Lookups.Select(lookup => lookup.QueryText));
        Assert.Equal(["the invoice", "the faktura"], evidence.Passages.Select(passage => passage.Text));
        Assert.Equal(2, evidence.LookupsRun);
    }

    /// <summary>The scope is what bounds the reading, so it travels into the lookup rather than filtering its result.</summary>
    [Fact]
    public async Task RetrieveAsync_AQuestionAboutSelectedMessages_BoundsEveryLookupByThatSelection()
    {
        // Arrange
        var selected = StoredEmailId.Create(new Guid("77777777-7777-7777-7777-777777777777"));
        var scope = WholeMailbox.NarrowedToEmails([selected]);
        var search = new ScriptedEmailKnowledgeSearch();

        // Act
        await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(scope),
            PlanOf(sufficientPassages: 10, "invoice"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([selected], search.LastScope?.SelectedEmails);
    }

    /// <summary>Enough is a ceiling on what one question reads, so the lookups behind it never run.</summary>
    [Fact]
    public async Task RetrieveAsync_EnoughFoundByTheFirstLookup_LeavesTheRestOfThePlanUnrun()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .Returning(
                "invoice",
                ScriptedEmailKnowledgeSearch.Passage("first"),
                ScriptedEmailKnowledgeSearch.Passage("second"))
            .Returning("faktura", ScriptedEmailKnowledgeSearch.Passage("third"));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 2, "invoice", "faktura"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["invoice"], search.Lookups.Select(lookup => lookup.QueryText));
        Assert.Equal(1, evidence.LookupsRun);
        Assert.Equal(2, evidence.Passages.Count);
    }

    /// <summary>Two wordings that reach the same extract found it once, which is what makes enough a count of evidence.</summary>
    [Fact]
    public async Task RetrieveAsync_TwoLookupsReachingOneExtract_CountItOnce()
    {
        // Arrange
        var storedEmailId = new Guid("88888888-8888-8888-8888-888888888888");
        var search = new ScriptedEmailKnowledgeSearch()
            .Returning("invoice", ScriptedEmailKnowledgeSearch.Passage("the same words", storedEmailId))
            .Returning("faktura", ScriptedEmailKnowledgeSearch.Passage("the same words", storedEmailId));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 10, "invoice", "faktura"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(evidence.Passages);
        Assert.Equal(2, evidence.LookupsRun);
    }

    /// <summary>One wording the deployment refuses does not make a question unanswerable.</summary>
    [Fact]
    public async Task RetrieveAsync_OneRefusedLookupAmongSeveral_AnswersFromTheOthersAndSaysSo()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .Refusing("invoice")
            .Returning("faktura", ScriptedEmailKnowledgeSearch.Passage("the faktura"));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 10, "invoice", "faktura"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(evidence.Passages);
        Assert.Equal(1, evidence.LookupsRun);
        Assert.Equal(1, evidence.LookupsRefused);
    }

    /// <summary>A plan nothing ran is told apart from a mailbox that held nothing, by the refusal that names the filter.</summary>
    [Fact]
    public async Task RetrieveAsync_EveryLookupRefused_RaisesTheRefusalRatherThanAnsweringEmpty()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .Refusing("invoice")
            .Refusing("faktura");
        var retrieval = new PlannedMailRetrieval(search);

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => retrieval.RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 10, "invoice", "faktura"),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("sender address", refusal.FilterName);
    }

    /// <summary>How this deployment ranks is its own configuration, republished rather than decided by the plan.</summary>
    [Fact]
    public async Task RetrieveAsync_ADeploymentThatRanksLexically_ReportsThatMode()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .RankingBy(EmailSearchRetrievalMode.Lexical)
            .Returning("invoice", ScriptedEmailKnowledgeSearch.Passage("the invoice"));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 10, "invoice"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, evidence.RetrievalMode);
    }

    /// <summary>A lookup returning more than the plan asked for is still bounded by what enough means.</summary>
    [Fact]
    public async Task RetrieveAsync_ALookupReturningMoreThanEnough_HandsOverOnlyWhatThePlanAskedFor()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch().Returning(
            "invoice",
            ScriptedEmailKnowledgeSearch.Passage("first"),
            ScriptedEmailKnowledgeSearch.Passage("second"),
            ScriptedEmailKnowledgeSearch.Passage("third"));

        // Act
        var evidence = await new PlannedMailRetrieval(search).RetrieveAsync(
            Question(WholeMailbox),
            PlanOf(sufficientPassages: 2, "invoice"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["first", "second"], evidence.Passages.Select(passage => passage.Text));
    }

    private static MailQuestion Question(MailboxScope scope) =>
        new(MailQuestionText.Create("was the invoice attached"), scope);

    private static RetrievalPlan PlanOf(int sufficientPassages, params string[] queries) => RetrievalPlan.Create(
        Bounds,
        [.. queries.Select(EmailKnowledgeQuery.ForText)],
        sufficientPassages);
}
