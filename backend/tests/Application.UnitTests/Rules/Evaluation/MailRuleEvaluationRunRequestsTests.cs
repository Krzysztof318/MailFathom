// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Covers the one thing the request path decides: whether this request is what puts a run in front of an account.</summary>
public sealed class MailRuleEvaluationRunRequestsTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 4, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>What a scheduled occasion is dispatched under, which is what the deployment's own process reaches this with.</summary>
    private static readonly AccessAuthorization ProcessItself =
        AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process);

    private readonly InMemoryMailRuleEvaluationRunStore runStore = new();
    private readonly FakeTimeProvider timeProvider = new(RequestedAt);

    [Fact]
    public async Task SubmitAsync_AccountWithNoRunOutstanding_RecordsOneAndAcceptsTheRequest()
    {
        // Act
        var request = await this.CreateRequests().SubmitAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(RequestedAt, request.Run.RequestedAt);
        Assert.True(request.Run.IsOutstanding);
        Assert.Equal(Account, this.runStore.Find(Account)?.Account);
    }

    /// <summary>Asking twice is asking once: the caller wanted the mail re-evaluated, and it is going to be.</summary>
    [Fact]
    public async Task SubmitAsync_RunAlreadyOutstanding_AnswersWithItAndRecordsNothing()
    {
        // Arrange
        var requests = this.CreateRequests();
        var first = await requests.SubmitAsync(Account, TestContext.Current.CancellationToken);

        this.timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await requests.SubmitAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(second.Accepted);
        Assert.Equal(first.Run.RequestedAt, second.Run.RequestedAt);
        Assert.Single(this.runStore.Saves);
    }

    [Fact]
    public async Task SubmitAsync_PreviousRunAlreadyEnded_AcceptsANewOne()
    {
        // Arrange
        this.runStore.Arrange(new MailRuleEvaluationRun
        {
            Account = Account,
            RequestedAt = RequestedAt.AddDays(-1),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            EndedAt = RequestedAt.AddDays(-1).AddMinutes(3),
            Ending = MailRuleEvaluationRunEnding.Completed,
        });

        // Act
        var request = await this.CreateRequests().SubmitAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(RequestedAt, this.runStore.Find(Account)?.RequestedAt);
        Assert.Null(this.runStore.Find(Account)?.Ending);
    }

    /// <summary>A schedule's occasion records a run of its own, which is what the pass reads the narrower reach from.</summary>
    [Fact]
    public async Task SubmitScheduledAsync_AccountWithNoRunOutstanding_RecordsAScheduledRun()
    {
        // Act
        var request = await this.CreateRequests(ProcessItself)
            .SubmitScheduledAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(MailRuleExecutionTrigger.ScheduledRun, request.Run.Trigger);
        Assert.Equal(MailRuleExecutionTrigger.ScheduledRun, this.runStore.Find(Account)?.Trigger);
    }

    /// <summary>One run per schedule at a time: an occasion finding the mailbox already being walked stands down.</summary>
    [Theory]
    [InlineData(MailRuleExecutionTrigger.RequestedRun)]
    [InlineData(MailRuleExecutionTrigger.ScheduledRun)]
    public async Task SubmitScheduledAsync_AnyRunAlreadyOutstanding_IsAnsweredWithItAndRecordsNothing(
        MailRuleExecutionTrigger outstanding)
    {
        // Arrange
        this.runStore.Arrange(new MailRuleEvaluationRun
        {
            Account = Account,
            RequestedAt = RequestedAt.AddMinutes(-5),
            Trigger = outstanding,
        });

        // Act
        var request = await this.CreateRequests(ProcessItself)
            .SubmitScheduledAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(request.Accepted);
        Assert.Equal(outstanding, request.Run.Trigger);
        Assert.Empty(this.runStore.Saves);
    }

    /// <summary>An operator's request reaches every rule, so it replaces a scheduled walk that reaches only some of them.</summary>
    [Fact]
    public async Task SubmitAsync_AScheduledRunOutstanding_ReplacesItRatherThanAnsweringWithIt()
    {
        // Arrange
        this.runStore.Arrange(new MailRuleEvaluationRun
        {
            Account = Account,
            RequestedAt = RequestedAt.AddMinutes(-5),
            Trigger = MailRuleExecutionTrigger.ScheduledRun,
            EvaluatedEmailCount = 40,
        });

        // Act
        var request = await this.CreateRequests().SubmitAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(MailRuleExecutionTrigger.RequestedRun, request.Run.Trigger);
        Assert.Equal(0, this.runStore.Find(Account)?.EvaluatedEmailCount);
    }

    /// <summary>The row at the moment of the write decides, so a run that arrived first is answered with, not overwritten.</summary>
    [Fact]
    public async Task SubmitScheduledAsync_ARunCommittedWhileTheOccasionWasBeingRecorded_StandsDownRatherThanReplacingIt()
    {
        // Arrange
        this.runStore.WhenAStartIsAttempted = () => this.runStore.Arrange(new MailRuleEvaluationRun
        {
            Account = Account,
            RequestedAt = RequestedAt.AddSeconds(-1),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            EvaluatedEmailCount = 12,
        });

        // Act
        var request = await this.CreateRequests(ProcessItself)
            .SubmitScheduledAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(request.Accepted);
        Assert.Equal(MailRuleExecutionTrigger.RequestedRun, request.Run.Trigger);
        Assert.Equal(12, this.runStore.Find(Account)?.EvaluatedEmailCount);
        Assert.Empty(this.runStore.Saves);
    }

    /// <summary>The same window on the operator's path: a walk already under way keeps its position rather than restarting.</summary>
    [Fact]
    public async Task SubmitAsync_ARequestedRunCommittedWhileThisOneWasBeingRecorded_StandsDownRatherThanResettingIt()
    {
        // Arrange
        this.runStore.WhenAStartIsAttempted = () => this.runStore.Arrange(new MailRuleEvaluationRun
        {
            Account = Account,
            RequestedAt = RequestedAt.AddSeconds(-1),
            Trigger = MailRuleExecutionTrigger.RequestedRun,
            EvaluatedEmailCount = 40,
        });

        // Act
        var request = await this.CreateRequests().SubmitAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(request.Accepted);
        Assert.Equal(40, request.Run.EvaluatedEmailCount);
        Assert.Equal(40, this.runStore.Find(Account)?.EvaluatedEmailCount);
        Assert.Empty(this.runStore.Saves);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task SubmitAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var requests = this.CreateRequests(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            requests.SubmitAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        Assert.Null(this.runStore.Find(Account));
    }

    /// <summary>The scheduled path is the process's own dispatch rather than a caller's request, so it asks for no grant and holds none.</summary>
    [Fact]
    public async Task SubmitScheduledAsync_TheProcessItself_RecordsARunWithoutHoldingAnyPermission()
    {
        // Arrange
        var requests = this.CreateRequests(ProcessItself);

        // Act
        var request = await requests.SubmitScheduledAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Empty(AuthorizedPrincipal.Process.Permissions);
    }

    /// <summary>Holding no grant is not what admits the scheduled path, so a caller reaching it is refused rather than starting a mailbox-wide walk.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitScheduledAsync_ACallerRatherThanTheProcess_IsRefusedWhateverItWasGranted(bool grantedEverything)
    {
        // Arrange
        MailFathomPermission[] granted = grantedEverything
            ? [.. MailFathomPermission.All.Where(permission => permission.Surface == ProtectedSurface.Administration)]
            : [];
        var requests = this.CreateRequests(AccessAuthorizations.ForCallerGranted(granted));

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            requests.SubmitScheduledAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(this.runStore.Find(Account));
        Assert.Empty(this.runStore.Saves);
    }

    /// <summary>An entrypoint that stated nothing about what admitted the work is the case the check exists to fail on.</summary>
    [Fact]
    public async Task SubmitScheduledAsync_AnEntrypointThatStatedNoPrincipal_IsRefusedRatherThanTreatedAsTheProcess()
    {
        // Arrange
        var requests = this.CreateRequests(AccessAuthorizations.ForPrincipal(principal: null));

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            requests.SubmitScheduledAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(this.runStore.Find(Account));
    }

    private MailRuleEvaluationRunRequests CreateRequests(AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleEvaluationRunRequests(
            this.runStore,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                this.timeProvider),
            this.timeProvider,
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
