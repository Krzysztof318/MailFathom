// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the rule routes answer, what they refuse, and the one thing none of them can be asked to do.</summary>
/// <remarks>
/// The absent operation is as much the contract as the present ones: configuration is where a rule is authored, so a
/// route that wrote one would be the path around the review a configuration diff gives. What is asserted here is the
/// three readings an operator performs, the answer that a run was already under way, and every refusal being a
/// <c>400</c> that names what to change without echoing anything the mailbox supplied.
/// </remarks>
public sealed class MailRuleEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The account a stored run names, which is the owner and the identifier together.</summary>
    private static readonly MailAccountIdentity AccountIdentity =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Account);
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private readonly IMailRuleEvaluationRunStore runs = Substitute.For<IMailRuleEvaluationRunStore>();
    private readonly IMailRuleExecutionStore history = Substitute.For<IMailRuleExecutionStore>();
    private readonly FakeTimeProvider timeProvider = new(Now);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every rule command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Routes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/rules", MailRuleEndpoints.RulesRoute);
        Assert.Equal("/rules/runs", MailRuleEndpoints.RunsRoute);
        Assert.Equal("/rules/history", MailRuleEndpoints.HistoryRoute);
    }

    /// <summary>The bound on the body, which the route carries as metadata the routing pipeline reads.</summary>
    [Fact]
    public void MapMailRules_TheRunRoute_CarriesTheRequestBodyBound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapMailRules();

        // Assert
        var runRoute = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(endpoint => endpoint.RoutePattern.RawText == MailRuleEndpoints.RunsRoute
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            MailRuleEndpoints.MaxRunRequestBytes,
            runRoute.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>The order is the answer as much as the rules are, and the authored condition is never part of it.</summary>
    [Fact]
    public async Task ReadRules_ALoadedSet_ServesItInEvaluationOrderWithoutTheAuthoredCondition()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            MailRule.Create(
                "file-invoices",
                ConditionReading(MailRuleFact.SenderDomain),
                MailRuleActionSet.Create([MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))]),
                stopWhenMatched: true),
            MailRule.Create("mark-newsletters", ConditionReading(MailRuleFact.Subject)));
        await using var settings = CreateSettings();

        // Act
        var result = MailRuleEndpoints.ReadRules(SourceOf(ruleSet), settings);

        // Assert
        Assert.Equal(["file-invoices", "mark-newsletters"], result.Value!.Rules.Select(rule => rule.Name));
        Assert.Equal(ruleSet.Revision.Value, result.Value.Revision);
        Assert.Equal(["senderDomain"], result.Value.Rules[0].ReadableFacts);
        Assert.True(result.Value.Rules[0].StopWhenMatched);

        var action = Assert.Single(result.Value.Rules[0].Actions);
        Assert.Equal(
            (0, MailboxMutation.Relocate.Name, Archive.Value),
            (action.Position, action.Mutation, action.Destination));
    }

    /// <summary>
    /// What runs a rule is part of what the rule is, and a rule nothing fires by itself is the one an operator asks
    /// about, so the answer distinguishes it rather than leaving both looking like a rule that never matched.
    /// </summary>
    [Fact]
    public async Task ReadRules_ARuleOnlyARequestedRunApplies_ReportsItAsTakingPartInNoTrigger()
    {
        // Arrange
        var ruleSet = RuleSetOf(
            MailRule.Create(
                "file-invoices",
                ConditionReading(MailRuleFact.SenderDomain),
                triggers: [MailRuleTrigger.Arrival]),
            MailRule.Create(
                "retire-old-newsletters",
                ConditionReading(MailRuleFact.Subject),
                triggers: []));
        await using var settings = CreateSettings();

        // Act
        var result = MailRuleEndpoints.ReadRules(SourceOf(ruleSet), settings);

        // Assert
        Assert.Equal(["Arrival"], result.Value!.Rules[0].Triggers);
        Assert.Empty(result.Value.Rules[1].Triggers);
        Assert.All(result.Value.Rules, rule => Assert.Null(rule.Schedule));
    }

    /// <summary>The trigger says a schedule runs the rule and only the schedule itself says when, so both are served.</summary>
    [Fact]
    public async Task ReadRules_AScheduledRule_ReportsTheOccasionsItDeclares()
    {
        // Arrange
        Assert.True(JobRecurrence.TryParse("Daily at 03:00 Europe/Warsaw", out var recurrence, out _));
        var ruleSet = RuleSetOf(MailRule.Create(
            "archive-old-newsletters",
            ConditionReading(MailRuleFact.Subject),
            triggers: [MailRuleTrigger.Schedule],
            schedule: recurrence));
        await using var settings = CreateSettings();

        // Act
        var result = MailRuleEndpoints.ReadRules(SourceOf(ruleSet), settings);

        // Assert
        Assert.Equal(["Schedule"], result.Value!.Rules[0].Triggers);
        Assert.Equal("daily:03:00:Europe/Warsaw", result.Value.Rules[0].Schedule);
    }

    /// <summary>A deployment nobody has edited says its configuration is the one the running set was read from.</summary>
    [Fact]
    public async Task ReadRules_ConfigurationNothingHasRefused_ReportsTheSetAsCurrent()
    {
        // Arrange
        await using var settings = CreateSettings();

        // Act
        var result = MailRuleEndpoints.ReadRules(SourceOf(RuleSetOf()), settings);

        // Assert
        Assert.True(result.Value!.ConfigurationAccepted);
        Assert.Equal(0, result.Value.RefusedSettingCount);
    }

    [Fact]
    public async Task StartRunAsync_AnAccountWithNoRunOutstanding_ReportsThatThisRequestStartedIt()
    {
        // Arrange
        this.runs.TryStartAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailRuleEvaluationRun>(),
                Arg.Any<CancellationToken>())
            .Returns((MailRuleEvaluationRun?)null);

        // Act
        var result = await this.StartRunAsync(new MailRuleRunRequest(Account.Value));

        // Assert
        var started = Assert.IsType<Ok<MailRuleRunStartResponse>>(result.Result);
        Assert.True(started.Value!.Started);
        Assert.Equal(Now, started.Value.Run.RequestedAt);
        Assert.Null(started.Value.Run.EndedAt);
    }

    /// <summary>Asking twice is asking once, so the second request is answered with the run already under way.</summary>
    [Fact]
    public async Task StartRunAsync_ARunAlreadyOutstanding_AnswersWithItRatherThanStartingASecondPass()
    {
        // Arrange
        var outstanding = new MailRuleEvaluationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddMinutes(-5),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            EvaluatedEmailCount = 120,
        };
        this.runs.TryStartAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailRuleEvaluationRun>(),
                Arg.Any<CancellationToken>())
            .Returns(outstanding);

        // Act
        var result = await this.StartRunAsync(new MailRuleRunRequest(Account.Value));

        // Assert
        var started = Assert.IsType<Ok<MailRuleRunStartResponse>>(result.Result);
        Assert.False(started.Value!.Started);
        Assert.Equal(outstanding.RequestedAt, started.Value.Run.RequestedAt);
        Assert.Equal(nameof(MailRuleExecutionTrigger.RequestedRun), started.Value.Run.Trigger);
        Assert.Equal(120, started.Value.Run.EvaluatedEmailCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nowhere")]
    public async Task StartRunAsync_ARequestNamingNoAccountThisDeploymentServes_IsRefused(string? account)
    {
        // Act
        var result = await this.StartRunAsync(new MailRuleRunRequest(account));

        // Assert
        AssertRefused(result.Result);
        await this.runs.DidNotReceive().TryStartAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailRuleEvaluationRun>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A request with no body at all is the same mistake as one naming no account, and is answered as one.</summary>
    [Fact]
    public async Task StartRunAsync_ARequestWithNoBody_IsRefusedWithoutStartingAnything()
    {
        // Act
        var result = await this.StartRunAsync(request: null);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>An account nobody has asked for a run is an outcome this deployment can state, not a missing resource.</summary>
    [Fact]
    public async Task ReadRunAsync_AnAccountNobodyHasAskedForARun_AnswersWithNoRunRatherThanARefusal()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((MailRuleEvaluationRun?)null);

        // Act
        var result = await MailRuleEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new MailRuleEvaluationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<MailRuleRunStateResponse>>(result.Result);
        Assert.Equal(Account.Value, state.Value!.Account);
        Assert.Null(state.Value.Run);
    }

    /// <summary>How the last run ended is what an operator comes back for, so a finished run is still reported.</summary>
    [Fact]
    public async Task ReadRunAsync_ARunThatHasEnded_ReportsItsProgressAndHowItEnded()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns(new MailRuleEvaluationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddHours(-1),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            EvaluatedEmailCount = 400,
            MatchedEmailCount = 12,
            SkippedEmailCount = 3,
            EndedAt = Now.AddMinutes(-50),
            Ending = MailRuleEvaluationRunEnding.Completed,
        });

        // Act
        var result = await MailRuleEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new MailRuleEvaluationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        var run = Assert.IsType<Ok<MailRuleRunStateResponse>>(result.Result).Value!.Run;
        Assert.Equal((400, 12, 3), (run!.EvaluatedEmailCount, run.MatchedEmailCount, run.SkippedEmailCount));
        Assert.Equal(nameof(MailRuleEvaluationRunEnding.Completed), run.Ending);
    }

    [Fact]
    public async Task ReadRunAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Act
        var result = await MailRuleEndpoints.ReadRunAsync(
            "nowhere",
            CatalogServing(Account),
            new MailRuleEvaluationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>The facts reach the wire by name, which is the invariant the whole record is bounded by.</summary>
    [Fact]
    public async Task ReadHistoryAsync_ARecordedExecution_ServesItsFactsByNameAndItsActionsWithTheirOutcomes()
    {
        // Arrange
        var recordId = Guid.CreateVersion7();
        this.ArrangePage(new MailRuleExecution
        {
            Id = MailRuleExecutionId.New(),
            Account = AccountIdentity,
            StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            RuleName = "file-invoices",
            Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6"),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            Outcome = MailRuleOutcome.Matched,
            ReadFacts = [MailRuleFact.SenderDomain],
            Actions =
            [
                new MailRuleExecutedAction(
                    0,
                    MailboxMutation.Relocate,
                    MailRuleExecutedActionOutcome.Requested,
                    Archive.Value,
                    MutationRecordId: MailboxMutationRecordId.Create(recordId)),
            ],
            EvaluatedAt = Now,
            Duration = TimeSpan.FromMilliseconds(4),
        });

        // Act
        var result = await this.ReadHistoryAsync();

        // Assert
        var execution = Assert.Single(Assert.IsType<Ok<MailRuleHistoryPageResponse>>(result.Result).Value!.Executions);
        Assert.Equal(["senderDomain"], execution.ReadFacts);
        Assert.Equal(nameof(MailRuleExecutionTrigger.RequestedRun), execution.Trigger);

        var action = Assert.Single(execution.Actions);
        Assert.Equal(nameof(MailRuleExecutedActionOutcome.Requested), action.Outcome);
        Assert.Equal(recordId, action.MutationRecord);
        Assert.Null(action.FailureReason);
    }

    /// <summary>An execution that produced no answer names why, which is what tells it from one that answered no.</summary>
    [Fact]
    public async Task ReadHistoryAsync_AnExecutionThatProducedNoAnswer_CarriesTheReasonBesideTheOutcome()
    {
        // Arrange
        this.ArrangePage(ExecutionThatFailed());

        // Act
        var result = await this.ReadHistoryAsync();

        // Assert
        var execution = Assert.Single(Assert.IsType<Ok<MailRuleHistoryPageResponse>>(result.Result).Value!.Executions);
        Assert.Equal(nameof(MailRuleOutcome.Failed), execution.Outcome);
        Assert.Equal(nameof(MailRuleConditionFailure.EvaluationTimedOut), execution.ConditionFailure);
    }

    [Fact]
    public async Task ReadHistoryAsync_APageWithMoreBehindIt_ServesTheCursorTheNextPageIsAskedWith()
    {
        // Arrange
        var executionId = MailRuleExecutionId.New();
        this.history
            .ReadPageAsync(Arg.Any<MailRuleExecutionQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => new MailRuleExecutionPage(
                [ExecutionThatFailed()],
                MailRuleExecutionCursor.After(
                    Now,
                    executionId,
                    call.Arg<MailRuleExecutionQuery>()!.FilterFingerprint)));

        // Act
        var result = await this.ReadHistoryAsync();

        // Assert
        var page = Assert.IsType<Ok<MailRuleHistoryPageResponse>>(result.Result).Value!;
        Assert.True(MailRuleExecutionCursor.TryDecode(page.NextCursor, out var cursor));
        Assert.NotNull(cursor);
        Assert.Equal(executionId, cursor.Value.ExecutionId);
    }

    /// <summary>A cursor this deployment did not issue is refused before anything is read with it.</summary>
    [Fact]
    public async Task ReadHistoryAsync_ACursorThisDeploymentDidNotIssue_IsRefusedWithoutReading()
    {
        // Act
        var result = await this.ReadHistoryAsync(cursor: "not-a-cursor-this-issued!!");

        // Assert
        AssertRefused(result.Result);
        await this.history.DidNotReceive().ReadPageAsync(
            Arg.Any<MailRuleExecutionQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadHistoryAsync_APageSizeTheHistoryDoesNotServe_IsRefused()
    {
        // Act
        var result = await this.ReadHistoryAsync(pageSize: MailRuleExecutionQuery.MaximumPageSize + 1);

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadHistoryAsync_ARuleFilterHoldingNoName_IsRefused()
    {
        // Act
        var result = await this.ReadHistoryAsync(rule: "   ");

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadHistoryAsync_ATimeRangeThatNamesNoExecutions_IsRefused()
    {
        // Act
        var result = await this.ReadHistoryAsync(from: Now, before: Now.AddHours(-1));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadHistoryAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedWithoutNamingWhatItHolds()
    {
        // Act
        var result = await this.ReadHistoryAsync(account: "nowhere");

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.DoesNotContain("work", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>The filters a caller wrote are narrowed onto the account, so no request reads another mailbox's history.</summary>
    [Fact]
    public async Task ReadHistoryAsync_TheFiltersARequestCarried_ReadsThemUnderTheNamedAccount()
    {
        // Arrange
        var email = Guid.CreateVersion7();
        this.ArrangePage();

        // Act
        await this.ReadHistoryAsync(rule: "file-invoices", email: email, pageSize: 10);

        // Assert
        await this.history.Received(1).ReadPageAsync(
            Arg.Is<MailRuleExecutionQuery>(query =>
                query!.AccountId == Account
                && query.RuleName == "file-invoices"
                && query.StoredEmailId!.Value.Value == email
                && query.PageSize == 10),
            Arg.Any<CancellationToken>());
    }

    private static ProblemHttpResult AssertRefused(IResult result)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        return refusal;
    }

    private static MailRuleExecution ExecutionThatFailed() => new()
    {
        Id = MailRuleExecutionId.New(),
        Account = AccountIdentity,
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        RuleName = "file-invoices",
        Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6"),
        Trigger = MailRuleExecutionTrigger.Arrival,
        Outcome = MailRuleOutcome.Failed,
        ConditionFailure = MailRuleConditionFailure.EvaluationTimedOut,
        ReadFacts = [],
        Actions = [],
        EvaluatedAt = Now,
        Duration = TimeSpan.FromSeconds(1),
    };

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                SyntheticMailOwner.Deployment,
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }

    private static MailRuleSetReader SourceOf(MailRuleSet ruleSet)
    {
        var source = Substitute.For<IMailRuleSetSource>();
        source.Current.Returns(ruleSet);

        return new MailRuleSetReader(source, AdministrativeGrant.WholeSurface);
    }

    private static MailRuleSet RuleSetOf(params MailRule[] rules) => MailRuleSet.Create(
        rules,
        MailRuleSetRevision.Create(
        [
            .. rules.Select(rule => new MailRuleDeclaration(
                rule.Name,
                "isSeen",
                [.. rule.Actions.Actions],
                rule.StopWhenMatched,
                [.. rule.Accounts],
                [.. rule.Triggers])),
        ]),
        MailRuleConditionBounds.Default);

    private static IMailRuleCondition ConditionReading(params MailRuleFact[] facts)
    {
        var condition = Substitute.For<IMailRuleCondition>();
        condition.ReferencedFacts.Returns(facts);

        return condition;
    }

    private static ValidatedSettingsSnapshot<MailRulesOptions> CreateSettings() => new(
        new TestOptionsMonitor<MailRulesOptions>(new MailRulesOptions()),
        (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
        "MailRules",
        new RecordingLogger<ValidatedSettingsSnapshot<MailRulesOptions>>());

    private void ArrangePage(params MailRuleExecution[] executions) =>
        this.history
            .ReadPageAsync(Arg.Any<MailRuleExecutionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new MailRuleExecutionPage(executions, NextCursor: null));

    private Task<Results<Ok<MailRuleRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        MailRuleRunRequest? request)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return MailRuleEndpoints.StartRunAsync(
            request,
            CatalogServing(Account),
            new MailRuleEvaluationRunRequests(
                this.runs,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    this.timeProvider),
                this.timeProvider,
                AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);
    }

    private Task<Results<Ok<MailRuleHistoryPageResponse>, ProblemHttpResult>> ReadHistoryAsync(
        string? account = null,
        string? rule = null,
        Guid? email = null,
        DateTimeOffset? from = null,
        DateTimeOffset? before = null,
        int? pageSize = null,
        string? cursor = null) =>
        MailRuleEndpoints.ReadHistoryAsync(
            account ?? Account.Value,
            rule,
            email,
            from,
            before,
            pageSize,
            cursor,
            CatalogServing(Account),
            new MailRuleHistory(this.history, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
