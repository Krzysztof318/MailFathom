// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Covers the two triggers, what each of them records, and everything a pass is allowed to leave behind.</summary>
public sealed class MailRuleEvaluationPassTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 4, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly MailAccountId OtherAccount = MailAccountId.Create("personal");

    private readonly InMemoryMailRuleEvaluationStore store = new();
    private readonly InMemoryMailRuleEvaluationRunStore runStore = new();
    private readonly FakeTimeProvider timeProvider = new(EvaluatedAt);

    [Fact]
    public async Task RunAsync_MailNoPassHasEvaluated_EvaluatesItAndRecordsIt()
    {
        // Arrange
        var matching = ScriptedMailRuleCondition.Answering(matches: true);
        var arrived = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(MailRule.Create("file-it", matching, stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, matching.EvaluationCount);
        Assert.Equal([arrived], this.store.Evaluated);
        Assert.Equal(1, report.Arrivals.EvaluatedEmailCount);
        Assert.Equal(1, report.Arrivals.MatchedEmailCount);
        Assert.Equal(["file-it"], report.Arrivals.MatchedRuleNames);
        Assert.False(report.Arrivals.EmailsRemain);
    }

    /// <summary>The arrival queue must never become a back door to reprocessing, whatever the rule set now says.</summary>
    [Fact]
    public async Task RunAsync_MailAPassAlreadyEvaluated_IsNotEvaluatedAgain()
    {
        // Arrange
        var condition = ScriptedMailRuleCondition.Answering(matches: true);
        this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        var pass = this.CreatePass(RuleSetOf(MailRule.Create("new-rule", condition, stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, condition.EvaluationCount);
        Assert.True(report.Arrivals.IsEmpty);
    }

    /// <summary>An account with no rule of its own still leaves the queue behind it, so a later rule starts from now.</summary>
    [Fact]
    public async Task RunAsync_NoRuleReachesThisAccount_StillRecordsItsArrivalsAsEvaluated()
    {
        // Arrange
        var condition = ScriptedMailRuleCondition.Answering(matches: true);
        var arrived = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(
            RuleSetOf(MailRule.Create("other-account", condition, stopWhenMatched: false, [OtherAccount.Value])));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, condition.EvaluationCount);
        Assert.True(this.store.IsEvaluated(arrived));
    }

    [Fact]
    public async Task RunAsync_MailOfAnotherAccount_IsNotReached()
    {
        // Arrange
        var elsewhere = this.store.Add(FactsFor(OtherAccount));
        var pass = this.CreatePass(RuleSetOf(
            MailRule.Create("everywhere", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(this.store.IsEvaluated(elsewhere));
    }

    [Fact]
    public async Task RunAsync_MoreArrivalsThanTheBatchBudgetReaches_EvaluatesWhatItCanAndReportsTheRest()
    {
        // Arrange
        var arrived = Enumerable
            .Range(0, 5)
            .Select(_ => this.store.Add(FactsFor(Account)))
            .ToArray();
        var pass = this.CreatePass(
            RuleSetOf(MailRule.Create("all", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false)),
            batchSize: 1,
            maxBatchesPerPass: 2);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(arrived.Take(2), this.store.Evaluated);
        Assert.Equal(2, report.Arrivals.EvaluatedEmailCount);
        Assert.True(report.Arrivals.EmailsRemain);
    }

    [Fact]
    public async Task RunAsync_RequestedRun_EvaluatesMailAlreadyEvaluatedAndCompletes()
    {
        // Arrange
        var already = this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        this.runStore.Arrange(RequestedRun());
        var pass = this.CreatePass(RuleSetOf(
            MailRule.Create("re-run", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([already], this.store.Evaluated);
        Assert.Equal(1, report.RequestedRun?.EvaluatedEmailCount);
        Assert.Equal(MailRuleEvaluationRunEnding.Completed, report.RequestedRunEnding);
        Assert.Equal(MailRuleEvaluationRunEnding.Completed, this.runStore.Find(Account)?.Ending);
        Assert.Null(await this.runStore.FindOutstandingAsync(Account, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_RequestedRunPickedUp_BindsTheRevisionItStartedUnder()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            MailRule.Create("re-run", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false));
        this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        this.runStore.Arrange(RequestedRun());

        // Act
        await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ruleSet.Revision, this.runStore.Find(Account)?.Revision);
    }

    [Fact]
    public async Task RunAsync_RequestedRunLeftPartWayThrough_ResumesFromTheCommittedPosition()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            MailRule.Create("re-run", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false));
        var mail = Enumerable
            .Range(0, 3)
            .Select(_ => this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1)))
            .ToArray();
        this.runStore.Arrange(RequestedRun() with
        {
            Revision = ruleSet.Revision,
            Position = mail[0],
            EvaluatedEmailCount = 1,
        });

        // Act
        var report = await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(mail.Skip(1), this.store.Evaluated);
        Assert.Equal(2, report.RequestedRun?.EvaluatedEmailCount);
        Assert.Equal(3, this.runStore.Find(Account)?.EvaluatedEmailCount);
    }

    /// <summary>A run cannot finish under rules it did not start with, and nothing keeps the set it did start with.</summary>
    [Fact]
    public async Task RunAsync_RuleSetChangedWhileARunWasOutstanding_EndsItAsSupersededWithoutEvaluating()
    {
        // Arrange
        var condition = ScriptedMailRuleCondition.Answering(matches: true);
        this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        this.runStore.Arrange(RequestedRun() with
        {
            Revision = RuleSetOf(MailRule.Create(
                "the-old-one",
                ScriptedMailRuleCondition.Answering(matches: false),
                stopWhenMatched: false)).Revision,
        });
        var pass = this.CreatePass(RuleSetOf(MailRule.Create("the-new-one", condition, stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, condition.EvaluationCount);
        Assert.Empty(this.store.Evaluated);
        Assert.Equal(MailRuleEvaluationRunEnding.Superseded, report.RequestedRunEnding);
        Assert.Equal(MailRuleEvaluationRunEnding.Superseded, this.runStore.Find(Account)?.Ending);
    }

    [Fact]
    public async Task RunAsync_RuleNamingTheBodyTextAndAnEmailStillAwaitingExtraction_SkipsItAndLeavesItInTheQueue()
    {
        // Arrange
        var awaiting = this.store.Add(FactsFor(Account), awaitsExtraction: true);
        var pass = this.CreatePass(RuleSetOf(MailRule.Create(
            "reads-the-body",
            ScriptedMailRuleCondition.Answering(matches: true, MailRuleFact.BodyText),
            stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(this.store.IsEvaluated(awaiting));
        Assert.Equal(0, report.Arrivals.EvaluatedEmailCount);
        Assert.Equal(1, report.Arrivals.SkippedEmailCount);
        Assert.Empty(this.store.BodyTextReads);
    }

    [Fact]
    public async Task RunAsync_ExtractionArrivedForASkippedEmail_EvaluatesItOnTheNextPass()
    {
        // Arrange
        var awaiting = this.store.Add(FactsFor(Account), awaitsExtraction: true);
        var pass = this.CreatePass(RuleSetOf(MailRule.Create(
            "reads-the-body",
            ScriptedMailRuleCondition.Answering(matches: true, MailRuleFact.BodyText),
            stopWhenMatched: false)));

        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Act
        this.store.CompleteExtraction(awaiting, "the invoice is attached");
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(this.store.IsEvaluated(awaiting));
        Assert.Equal(1, report.Arrivals.EvaluatedEmailCount);
        Assert.Equal([awaiting], this.store.BodyTextReads);
    }

    /// <summary>Waiting for text nothing will ever extract would stall the queue behind a message that cannot become eligible.</summary>
    [Fact]
    public async Task RunAsync_EmailWhoseContentWillNeverYieldText_IsEvaluatedWithTheFactAbsent()
    {
        // Arrange
        var withoutText = this.store.Add(FactsFor(Account), awaitsExtraction: false);
        var pass = this.CreatePass(RuleSetOf(MailRule.Create(
            "reads-the-body",
            ScriptedMailRuleCondition.Answering(matches: false, MailRuleFact.BodyText),
            stopWhenMatched: false)));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(this.store.IsEvaluated(withoutText));
        Assert.Equal([withoutText], this.store.BodyTextReads);
    }

    [Fact]
    public async Task RunAsync_ConditionThatCannotAnswer_RecordsTheFailureAndEvaluatesTheRestOfTheBatch()
    {
        // Arrange
        var unlucky = this.store.Add(FactsFor(Account));
        var next = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(
            MailRule.Create(
                "raises",
                ScriptedMailRuleCondition.Raising(new InvalidOperationException("no answer")),
                stopWhenMatched: false),
            MailRule.Create("answers", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([unlucky, next], this.store.Evaluated);
        Assert.Equal(2, report.Arrivals.FailedRuleCount);
        Assert.Equal(0, report.Arrivals.TimedOutRuleCount);
        Assert.Equal(["raises"], report.Arrivals.FailedRuleNames);
        Assert.Equal(["answers"], report.Arrivals.MatchedRuleNames);
    }

    [Fact]
    public async Task RunAsync_CancelledPartWayThrough_KeepsTheCommittedBatchAndLeavesTheRestInTheQueue()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var first = this.store.Add(FactsFor(Account));
        var second = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(
            RuleSetOf(MailRule.Create("withdraws", new CancellingCondition(cancellation), stopWhenMatched: false)),
            batchSize: 1);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass.RunAsync(Account, cancellation.Token));

        // Assert
        Assert.True(this.store.IsEvaluated(first));
        Assert.False(this.store.IsEvaluated(second));
    }

    private static MailRuleEmailFacts FactsFor(MailAccountId accountId) =>
        new() { Account = accountId.Value, Folder = "inbox" };

    private static MailRuleEvaluationRun RequestedRun() => new()
    {
        AccountId = Account,
        RequestedAt = EvaluatedAt.AddMinutes(-1),
    };

    private static MailRuleSet RuleSetOf(params MailRule[] rules) => MailRuleSet.Create(
        rules,
        MailRuleSetRevision.Create(
            [.. rules.Select(rule => new MailRuleDeclaration(rule.Name, "isSeen", rule.StopWhenMatched, [.. rule.Accounts]))]),
        MailRuleConditionBounds.Default);

    private MailRuleEvaluationPass CreatePass(
        MailRuleSet ruleSet,
        int batchSize = 100,
        int maxBatchesPerPass = 5)
    {
        var ruleSetSource = Substitute.For<IMailRuleSetSource>();
        ruleSetSource.Current.Returns(ruleSet);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleEvaluationPass(
            ruleSetSource,
            new MailRuleSetEvaluator(this.timeProvider),
            this.store,
            this.runStore,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                this.timeProvider),
            new MailRuleEvaluationOptions { BatchSize = batchSize, MaxBatchesPerPass = maxBatchesPerPass },
            this.timeProvider);
    }

    /// <summary>Answers for the first email, then withdraws the pass, which is how a shutdown reaches a walk mid-batch.</summary>
    private sealed class CancellingCondition(CancellationTokenSource cancellation) : IMailRuleCondition
    {
        private int answeredCount;

        public IReadOnlyList<MailRuleFact> ReferencedFacts { get; } = [];

        public Task<bool> EvaluateAsync(MailRuleFacts facts, CancellationToken cancellationToken)
        {
            if (this.answeredCount++ > 0)
            {
                cancellation.Cancel();
            }

            return Task.FromResult(false);
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
