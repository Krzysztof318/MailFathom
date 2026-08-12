// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.History;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
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
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Backup = MailFolderAlias.Create("backup");

    private readonly InMemoryMailRuleEvaluationStore store = new();
    private readonly InMemoryMailRuleEvaluationRunStore runStore = new();
    private readonly InMemoryMailRuleExecutionStore history = new();
    private readonly InMemoryMailboxMutationRecordStore mutations = new();
    private readonly InMemoryMailFolderResolutionStore folders = new();
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    private readonly IMailRuleActionPermissionReader permissions =
        Substitute.For<IMailRuleActionPermissionReader>();

    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;

    private readonly FakeTimeProvider timeProvider = new(EvaluatedAt);

    public MailRuleEvaluationPassTests()
    {
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });

        // Both destinations are folders their accounts mirror, so a pass reaching one reads the binding its own run
        // recorded rather than asking the server, which is what every assertion here is about.
        foreach (var accountId in new[] { Account, OtherAccount })
        {
            foreach (var alias in new[] { Archive, Backup })
            {
                this.folderMappings.With(
                    accountId,
                    MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create($"INBOX/{alias.Value}")));
            }
        }
    }

    [Fact]
    public async Task RunAsync_MailNoPassHasEvaluated_EvaluatesItAndRecordsIt()
    {
        // Arrange
        var matching = ScriptedMailRuleCondition.Answering(matches: true);
        var arrived = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(ArrivalRule("file-it", matching, stopWhenMatched: false)));

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

    /// <summary>What a match leads to is a durable request, written in the batch's own transaction and issued by nothing here.</summary>
    [Fact]
    public async Task RunAsync_ARuleWithAnAction_WritesTheChangeDownAgainstTheMatchedOccurrence()
    {
        // Arrange
        this.folders.Bind(Account, Archive);
        var arrived = this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(FilingRule("file-it")));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.mutations.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal(arrived, request.StoredEmailId);
        Assert.Equal(MailboxMutationOrigin.Rule, request.Requester.Origin);
        Assert.Equal(1, report.Arrivals.RequestedActionCount);
    }

    /// <summary>Two rules asking for one email's fate resolve by declared order, and the withheld one is reported by name.</summary>
    [Fact]
    public async Task RunAsync_TwoRulesFilingOneEmailDifferently_AsksOnceAndNamesTheRuleItWithheld()
    {
        // Arrange
        this.folders.Bind(Account, Archive);
        this.folders.Bind(Account, Backup);
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(FilingRule("file-invoices"), FilingRule("file-everything", Backup)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.mutations.OpenedRecordCount);
        Assert.Equal(1, report.Arrivals.RequestedActionCount);
        Assert.Equal(1, report.Arrivals.WithheldActionCount);
        Assert.Equal(["file-everything"], report.Arrivals.UnappliedActionRuleNames);
    }

    /// <summary>A whole-mailbox run re-asks under the same identity, so re-running the rules performs each change once.</summary>
    [Fact]
    public async Task RunAsync_AWholeMailboxRunOverMailAlreadyFiled_OpensNoSecondRecord()
    {
        // Arrange
        this.folders.Bind(Account, Archive);
        this.store.Add(FactsFor(Account));
        var ruleSet = RuleSetOf(FilingRule("file-it"));
        await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);
        this.runStore.Arrange(RequestedRun());

        // Act
        var report = await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.mutations.OpenedRecordCount);
        Assert.Equal(1, report.RequestedRun?.RequestedActionCount);
    }

    /// <summary>A destination that has stopped resolving fails visibly rather than filing the mail somewhere unintended.</summary>
    [Fact]
    public async Task RunAsync_ADestinationNothingHasBound_RecordsNothingAndNamesTheRule()
    {
        // Arrange
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(FilingRule("file-it")));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, this.mutations.OpenedRecordCount);
        Assert.Equal(1, report.Arrivals.FailedActionCount);
        Assert.Equal(["file-it"], report.Arrivals.UnappliedActionRuleNames);
    }

    /// <summary>A rule that selects mail and changes nothing is an ordinary rule, so the mailbox is asked for nothing.</summary>
    [Fact]
    public async Task RunAsync_AMatchingRuleThatDeclaresNoAction_AsksTheMailboxForNothing()
    {
        // Arrange
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(
            RuleSetOf(ArrivalRule("select-only", ScriptedMailRuleCondition.Answering(matches: true))));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, this.mutations.OpenedRecordCount);
        Assert.Equal(0, report.Arrivals.RequestedActionCount);
    }

    /// <summary>The arrival queue must never become a back door to reprocessing, whatever the rule set now says.</summary>
    [Fact]
    public async Task RunAsync_MailAPassAlreadyEvaluated_IsNotEvaluatedAgain()
    {
        // Arrange
        var condition = ScriptedMailRuleCondition.Answering(matches: true);
        this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        var pass = this.CreatePass(RuleSetOf(ArrivalRule("new-rule", condition, stopWhenMatched: false)));

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
            RuleSetOf(ArrivalRule("other-account", condition, stopWhenMatched: false, accounts: [OtherAccount.Value])));

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
            ArrivalRule("everywhere", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

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
            RuleSetOf(ArrivalRule("all", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false)),
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
            ArrivalRule("re-run", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([already], this.store.Evaluated);
        Assert.Equal(1, report.RequestedRun?.EvaluatedEmailCount);
        Assert.Equal(MailRuleEvaluationRunEnding.Completed, report.RequestedRunEnding);
        Assert.Equal(MailRuleEvaluationRunEnding.Completed, this.runStore.Find(Account)?.Ending);
        Assert.Null(await this.runStore.FindOutstandingAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>A manual-only rule is the whole point of the key: nothing fires it, and asking for a run does.</summary>
    [Fact]
    public async Task RunAsync_ManualOnlyRule_RunsInTheRequestedWalkAndNotInTheArrivalOne()
    {
        // Arrange
        var housekeeping = ScriptedMailRuleCondition.Answering(matches: true);
        var ruleSet = RuleSetOf(MailRule.Create("housekeeping", housekeeping, triggers: []));
        this.store.Add(FactsFor(Account));

        // Act
        var arrivalOnly = await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        this.runStore.Arrange(RequestedRun());

        var requested = await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, arrivalOnly.Arrivals.EvaluatedEmailCount);
        Assert.Empty(arrivalOnly.Arrivals.MatchedRuleNames);
        Assert.Equal(["housekeeping"], requested.RequestedRun?.MatchedRuleNames);
    }

    /// <summary>The body-text question is asked of the rules a walk runs, so a manual-only rule holds nothing up.</summary>
    [Fact]
    public async Task RunAsync_ManualOnlyRuleNamingTheBodyText_LeavesArrivingMailAwaitingExtractionEvaluated()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            MailRule.Create(
                "housekeeping",
                ScriptedMailRuleCondition.Answering(matches: true, MailRuleFact.BodyText),
                triggers: []),
            ArrivalRule("on-arrival", ScriptedMailRuleCondition.Answering(matches: true)));
        var awaiting = this.store.Add(FactsFor(Account), awaitsExtraction: true);

        // Act
        var report = await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([awaiting], this.store.Evaluated);
        Assert.Equal(0, report.Arrivals.SkippedEmailCount);
        Assert.Equal(["on-arrival"], report.Arrivals.MatchedRuleNames);
    }

    [Fact]
    public async Task RunAsync_RequestedRunPickedUp_BindsTheRevisionItStartedUnder()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            ArrivalRule("re-run", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false));
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
            ArrivalRule("re-run", ScriptedMailRuleCondition.Answering(matches: false), stopWhenMatched: false));
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
            Revision = RuleSetOf(ArrivalRule(
                "the-old-one",
                ScriptedMailRuleCondition.Answering(matches: false),
                stopWhenMatched: false)).Revision,
        });
        var pass = this.CreatePass(RuleSetOf(ArrivalRule("the-new-one", condition, stopWhenMatched: false)));

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
        var pass = this.CreatePass(RuleSetOf(ArrivalRule(
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
        var pass = this.CreatePass(RuleSetOf(ArrivalRule(
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
        var pass = this.CreatePass(RuleSetOf(ArrivalRule(
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
            ArrivalRule(
                "raises",
                ScriptedMailRuleCondition.Raising(new InvalidOperationException("no answer")),
                stopWhenMatched: false),
            ArrivalRule("answers", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false)));

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
            RuleSetOf(ArrivalRule("withdraws", new CancellingCondition(cancellation), stopWhenMatched: false)),
            batchSize: 1);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass.RunAsync(Account, cancellation.Token));

        // Assert
        Assert.True(this.store.IsEvaluated(first));
        Assert.False(this.store.IsEvaluated(second));
    }

    /// <summary>What every reached rule concluded is written down beside the mail, which is what a later question reads.</summary>
    [Fact]
    public async Task RunAsync_TheArrivalWalk_RecordsWhatEachRuleItReachedConcluded()
    {
        // Arrange
        var arrived = this.store.Add(FactsFor(Account));
        var ruleSet = RuleSetOf(
            ArrivalRule("file-invoices", ScriptedMailRuleCondition.Answering(matches: false)),
            ArrivalRule(
                "mark-newsletters",
                ScriptedMailRuleCondition.Answering(matches: true, MailRuleFact.SenderDomain)));

        // Act
        await this.CreatePass(ruleSet).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("file-invoices", MailRuleOutcome.NotMatched), ("mark-newsletters", MailRuleOutcome.Matched)],
            this.history.Executions.Select(execution => (execution.RuleName, execution.Outcome)));
        Assert.All(this.history.Executions, execution => Assert.Equal(arrived, execution.StoredEmailId));
        Assert.All(this.history.Executions, execution => Assert.Equal(ruleSet.Revision, execution.Revision));
        Assert.All(
            this.history.Executions,
            execution => Assert.Equal(MailRuleExecutionTrigger.Arrival, execution.Trigger));
        Assert.Equal(
            ["senderDomain"],
            this.history.ExecutionsOf("mark-newsletters")[0].ReadFacts.Select(fact => fact.Name));
    }

    /// <summary>The two walks are told apart in the record, so an operator can see what their own run concluded.</summary>
    [Fact]
    public async Task RunAsync_ARequestedRun_RecordsItsExecutionsUnderThatTrigger()
    {
        // Arrange
        this.store.Add(FactsFor(Account), evaluatedAt: EvaluatedAt.AddDays(-1));
        this.runStore.Arrange(RequestedRun());
        var pass = this.CreatePass(RuleSetOf(
            ArrivalRule("re-run", ScriptedMailRuleCondition.Answering(matches: true))));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var execution = Assert.Single(this.history.Executions);
        Assert.Equal(MailRuleExecutionTrigger.RequestedRun, execution.Trigger);
        Assert.Equal("re-run", execution.RuleName);
    }

    /// <summary>A rule nobody asked leaves nothing, which is what keeps it apart from a rule that answered no.</summary>
    [Fact]
    public async Task RunAsync_ARuleThatEndedThePass_LeavesNoRecordForTheRulesBelowIt()
    {
        // Arrange
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(
            ArrivalRule("ends-it", ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: true),
            ArrivalRule("never-reached", ScriptedMailRuleCondition.Answering(matches: true))));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("ends-it", Assert.Single(this.history.Executions).RuleName);
        Assert.Empty(this.history.ExecutionsOf("never-reached"));
    }

    /// <summary>An expression that could not be evaluated is recorded as that, with the reason, rather than as a no.</summary>
    [Fact]
    public async Task RunAsync_AConditionThatCouldNotAnswer_RecordsTheFailureRatherThanANonMatch()
    {
        // Arrange
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(ArrivalRule(
            "raises",
            ScriptedMailRuleCondition.Raising(new InvalidOperationException("no answer")))));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var execution = Assert.Single(this.history.Executions);
        Assert.Equal(MailRuleOutcome.Failed, execution.Outcome);
        Assert.Equal(MailRuleConditionFailure.EvaluationFaulted, execution.ConditionFailure);
    }

    /// <summary>The record points at the mutation it asked for instead of restating what became of it.</summary>
    [Fact]
    public async Task RunAsync_ARuleWithAnAction_PointsTheRecordAtTheMutationItOpened()
    {
        // Arrange
        this.folders.Bind(Account, Archive);
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(FilingRule("file-it")));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var action = Assert.Single(Assert.Single(this.history.Executions).Actions);
        Assert.Equal(MailRuleExecutedActionOutcome.Requested, action.Outcome);
        Assert.Equal(Archive.Value, action.Destination);
        Assert.Equal(this.mutations.OpenedRequests[0].Mutation, action.Mutation);
        Assert.NotNull(action.MutationRecordId);
    }

    /// <summary>An action a destination stopped resolving for is visible as refused, with the classification.</summary>
    [Fact]
    public async Task RunAsync_AnActionNothingCouldBeRecordedFor_RecordsItAsRefusedWithTheReason()
    {
        // Arrange
        this.store.Add(FactsFor(Account));
        var pass = this.CreatePass(RuleSetOf(FilingRule("file-it")));

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var action = Assert.Single(Assert.Single(this.history.Executions).Actions);
        Assert.Equal(MailRuleExecutedActionOutcome.Refused, action.Outcome);
        Assert.Equal(MailRuleActionFailureReason.DestinationFolderUnresolved, action.FailureReason);
        Assert.Null(action.MutationRecordId);
    }

    private static MailRuleEmailFacts FactsFor(MailAccountId accountId) =>
        new() { Account = accountId.Value, Folder = "inbox" };

    /// <summary>A rule that matches everything and files it, which is the shape every action assertion here needs.</summary>
    /// <summary>A rule as an operator writes one for arriving mail, which is the walk most of these tests take.</summary>
    /// <remarks>
    /// A rule takes part in the occasions it names and in no others, so a rule meant to reach an arrival says so. The
    /// tests about a rule nothing fires by itself declare their triggers where they stand, because that is their subject.
    /// </remarks>
    private static MailRule ArrivalRule(
        string name,
        IMailRuleCondition condition,
        MailRuleActionSet? actions = null,
        bool stopWhenMatched = false,
        IReadOnlyList<string>? accounts = null) =>
        MailRule.Create(name, condition, actions, stopWhenMatched, accounts, [MailRuleTrigger.Arrival]);

    private static MailRule FilingRule(string name, MailFolderAlias? destination = null) => ArrivalRule(
        name,
        ScriptedMailRuleCondition.Answering(matches: true),
        MailRuleActionSet.Create([MailRuleAction.Relocate(MailFolderReference.ToAlias(destination ?? Archive))]));

    private static MailRuleEvaluationRun RequestedRun() => new()
    {
        AccountId = Account,
        RequestedAt = EvaluatedAt.AddMinutes(-1),
    };

    private static MailRuleSet RuleSetOf(params MailRule[] rules) => MailRuleSet.Create(
        rules,
        MailRuleSetRevision.Create(
            [.. rules.Select(rule => new MailRuleDeclaration(rule.Name, "isSeen", [.. rule.Actions.Actions], rule.StopWhenMatched, [.. rule.Accounts], [.. rule.Triggers]))]),
        MailRuleConditionBounds.Default);

    /// <summary>Resolves destinations over a server advertising nothing, so only a recorded binding ever answers.</summary>
    private MailboxDestinationResolver CreateDestinationResolver()
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>([]));

        return new MailboxDestinationResolver(
            this.folderMappings.Resolver,
            this.folders,
            new MailFolderResolver(
                remoteFolderCatalog,
                Substitute.For<IRemoteFolderCreator>(),
                this.folders,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                Substitute.For<IPersistenceSessionFactory>(),
                this.timeProvider),
            Substitute.For<IMailTransportSecurityPolicyReader>());
    }

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
            new MailRuleActionRecorder(this.mutations, this.deleteDispositions, this.permissions),
            this.CreateDestinationResolver(),
            this.history,
            this.folderMappings,
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
