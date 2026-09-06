// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers what a watched run publishes, in what order, and what it publishes instead when it ends badly.</summary>
public sealed class StreamedDiscoveryRunTests
{
    private const int SufficientPassages = 5;

    private static readonly MailQuestion Question = new(
        MailQuestionText.Create("which supplier quoted least"),
        MailboxScope.Create(SyntheticMailOwner.Deployment, [MailAccountId.Create("primary")], []));

    private readonly FakeTimeProvider clock = new(DiscoveryRuns.Now);

    /// <summary>A person watching sees the run open, work, declare its sources, and only then read a block.</summary>
    [Fact]
    public async Task RunAsync_ARunThatRetrievedMail_PublishesTheOpeningTheProgressTheSourcesAndThenTheBlocks()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new ScriptedEmailKnowledgeSearch()
            .Returning("quotation", ScriptedEmailKnowledgeSearch.Passage("the quotation")));

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        var published = Published(journal);
        Assert.Equal(
            [
                DiscoveryRunStarted.Kind,
                DiscoveryRetrievalProgressed.Kind,
                DiscoveryCitationDeclared.Kind,
                DiscoveryBlockComposed.Kind,
                DiscoveryRunCompleted.Kind,
            ],
            published.Select(@event => @event.EventName).Distinct());
        var composed = PresentationPlanExample.Compose();
        Assert.Equal(composed.Citations.Count, published.OfType<DiscoveryCitationDeclared>().Count());
        Assert.Equal(composed.Blocks.Count, published.OfType<DiscoveryBlockComposed>().Count());
    }

    /// <summary>A source is declared before the block naming it, so a client renders a block the moment it arrives.</summary>
    [Fact]
    public async Task RunAsync_ARunThatRetrievedMail_DeclaresEverySourceBeforeTheBlockNamingIt()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new ScriptedEmailKnowledgeSearch()
            .Returning("quotation", ScriptedEmailKnowledgeSearch.Passage("the quotation")));

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        var published = Published(journal);
        var declared = published.OfType<DiscoveryCitationDeclared>().ToArray();
        var composed = published.OfType<DiscoveryBlockComposed>().ToArray();
        Assert.True(declared.Max(@event => @event.Sequence) < composed.Min(@event => @event.Sequence));
        Assert.Empty(
            composed.SelectMany(@event => @event.Block.ReferencedCitations)
                .Select(citation => citation.Value)
                .Except(declared.Select(@event => @event.Citation.Id.Value)));
    }

    /// <summary>Retrieval reports as each lookup settles, so a run reading a large mailbox does not look stalled.</summary>
    [Fact]
    public async Task RunAsync_APlanOfSeveralLookups_ReportsRetrievalAsEachOfThemSettles()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(
            new ScriptedEmailKnowledgeSearch()
                .Returning("quotation", ScriptedEmailKnowledgeSearch.Passage("the quotation"))
                .Returning("oferta", ScriptedEmailKnowledgeSearch.Passage("the oferta")),
            "quotation",
            "oferta");

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        var reported = Published(journal).OfType<DiscoveryRetrievalProgressed>().Select(@event => @event.Progress);
        Assert.Equal([1, 2], reported.Select(progress => progress.LookupsRun));
        Assert.Equal([1, 2], reported.Select(progress => progress.PassagesFound));
    }

    /// <summary>What made the answer narrower than the question is stated on the ending rather than left to be inferred.</summary>
    [Fact]
    public async Task RunAsync_ARunThatStoppedAtEnough_StatesThatItReadNoFurther()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(
            new ScriptedEmailKnowledgeSearch().Returning(
                "quotation",
                ScriptedEmailKnowledgeSearch.Passage("the quotation"),
                ScriptedEmailKnowledgeSearch.Passage("the revision")),
            sufficientPassages: 2,
            "quotation");

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        var completed = Assert.IsType<DiscoveryRunCompleted>(Published(journal)[^1]);
        Assert.Equal([PresentationLimitation.RetrievalTruncated], completed.Limitations);
    }

    /// <summary>A mailbox holding nothing on the subject is a run that completed, not one that failed.</summary>
    /// <remarks>
    /// The composition answers with a plan whatever retrieval found — one saying the sources do not settle the question
    /// where they do not — so a run over an empty mailbox publishes that plan rather than ending on a failure that would
    /// tell somebody to ask again.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ARunThatRetrievedNothing_StillCompletesWithWhatTheCompositionMadeOfIt()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new ScriptedEmailKnowledgeSearch());

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<DiscoveryRunCompleted>(Published(journal)[^1]);
        Assert.NotEmpty(Published(journal).OfType<DiscoveryBlockComposed>());
    }

    /// <summary>What the run read of its own accounts reaches the client too, so nothing of the plan is lost to the stream.</summary>
    /// <remarks>
    /// Coverage and the limitations beside it are statements about the whole run rather than about a block, so they
    /// arrive on the ending. A client assembling the plan out of the events holds every part of it once the run has
    /// ended, which is what makes the stream a delivery of the plan rather than a summary of one.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ARunThatRetrievedMail_EndsCarryingTheCoverageAndTheLimitationsOfItsPlan()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new ScriptedEmailKnowledgeSearch()
            .Returning("quotation", ScriptedEmailKnowledgeSearch.Passage("the quotation")));

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        var composed = PresentationPlanExample.Compose();
        var completed = Assert.IsType<DiscoveryRunCompleted>(Published(journal)[^1]);
        Assert.Equal(composed.Coverage, completed.Coverage);
        Assert.Equal(composed.Limitations, completed.Limitations);
    }

    /// <summary>A deployment that answers no question at all says so as the run's ending, since nobody is left to throw at.</summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatAnswersNoQuestions_EndsTheRunAsUnavailable()
    {
        // Arrange
        var journal = NewJournal();
        var run = new StreamedDiscoveryRun(
            DiscoveryRuns.Composing(planner: null, new ScriptedEmailKnowledgeSearch()),
            this.clock);

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryRunFailure.Unavailable, Failure(journal));
    }

    /// <summary>A provider refusing right now is told apart from a deployment that answers nothing, on the stream too.</summary>
    [Fact]
    public async Task RunAsync_AChatProviderRefusingRecently_EndsTheRunAsTemporarilyUnavailable()
    {
        // Arrange
        var journal = NewJournal();
        var run = new StreamedDiscoveryRun(
            DiscoveryRuns.Composing(
                DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation"),
                new ScriptedEmailKnowledgeSearch(),
                chatState: AiProviderHealthState.Unavailable),
            this.clock);

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryRunFailure.TemporarilyUnavailable, Failure(journal));
    }

    /// <summary>A plan the deployment refused every lookup of ends as a refusal rather than as an empty answer.</summary>
    [Fact]
    public async Task RunAsync_EveryLookupRefused_EndsTheRunAsRefusedRetrieval()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new ScriptedEmailKnowledgeSearch().Refusing("quotation"));

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryRunFailure.RetrievalRefused, Failure(journal));
    }

    /// <summary>What the run published before it failed stays published, which is the whole of keeping what came before.</summary>
    [Fact]
    public async Task RunAsync_ARunThatFailedAfterReportingProgress_KeepsWhatItHadAlreadyPublished()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(
            new ScriptedEmailKnowledgeSearch().Refusing("quotation").Refusing("oferta"),
            "quotation",
            "oferta");

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [DiscoveryRunStarted.Kind, DiscoveryRetrievalProgressed.Kind, DiscoveryRetrievalProgressed.Kind, DiscoveryRunFailed.Kind],
            Published(journal).Select(@event => @event.EventName));
    }

    /// <summary>A deployment stopping mid-run says so, rather than leaving a client on a connection that closes.</summary>
    [Fact]
    public async Task RunAsync_ADeploymentStoppingMidRun_EndsTheRunAsStopped()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new StoppedEmailKnowledgeSearch());
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        // Act
        await run.RunAsync(Question, journal, stopped.Token);

        // Assert
        Assert.Equal(DiscoveryRunFailure.Stopped, Failure(journal));
    }

    /// <summary>A run that spent the longest one may take is told apart from one a stopping deployment cut short.</summary>
    [Fact]
    public async Task RunAsync_ARunSpendingTheLongestOneMayTake_EndsTheRunAsTimedOut()
    {
        // Arrange
        var journal = NewJournal();
        var run = this.RunOver(new StoppedEmailKnowledgeSearch(this.clock));

        // Act
        await run.RunAsync(Question, journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryRunFailure.TimedOut, Failure(journal));
    }

    /// <summary>Retrieval as it behaves for a run that has been stopped, whether by a shutdown or by its own bound.</summary>
    /// <remarks>
    /// A clock handed over here is the run's own, and spending the longest a run may take against it is what makes the
    /// timeout observable: the bound is applied inside the run rather than by whoever passed the token, so there is no
    /// second cancellation source for a test to trip instead.
    /// </remarks>
    private sealed class StoppedEmailKnowledgeSearch(FakeTimeProvider? clock = null) : IEmailKnowledgeSearch
    {
        public Task<EmailKnowledgeLookup> FindPassagesAsync(
            MailboxScope scope,
            EmailKnowledgeQuery query,
            CancellationToken cancellationToken)
        {
            clock?.Advance(DiscoveryRunBounds.MaximumDuration);

            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("The run was expected to have been stopped before retrieval answered.");
        }
    }

    private StreamedDiscoveryRun RunOver(IEmailKnowledgeSearch search) =>
        this.RunOver(search, SufficientPassages, "quotation");

    private StreamedDiscoveryRun RunOver(IEmailKnowledgeSearch search, params string[] queries) =>
        this.RunOver(search, SufficientPassages, queries);

    private StreamedDiscoveryRun RunOver(
        IEmailKnowledgeSearch search,
        int sufficientPassages,
        params string[] queries) =>
        new(
            DiscoveryRuns.Composing(
                DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, sufficientPassages, queries),
                search),
            this.clock);

    private static DiscoveryRunJournal NewJournal() =>
        new(DiscoveryRunId.New(), SyntheticMailOwner.Deployment);

    private static DiscoveryRunFailure Failure(DiscoveryRunJournal journal) =>
        Assert.IsType<DiscoveryRunFailed>(Published(journal)[^1]).Failure;

    private static DiscoveryRunEvent[] Published(DiscoveryRunJournal journal) =>
    [
        .. journal.ReadFromAsync(afterSequence: 0, TestContext.Current.CancellationToken)
            .ToBlockingEnumerable(TestContext.Current.CancellationToken),
    ];
}
