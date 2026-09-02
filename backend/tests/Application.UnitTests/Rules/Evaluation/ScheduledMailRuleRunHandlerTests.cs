// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>What a schedule's occasion does once the queue hands it to a worker: it asks for a walk and nothing else.</summary>
public sealed class ScheduledMailRuleRunHandlerTests
{
    private static readonly DateTimeOffset DispatchedAt = new(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private readonly InMemoryMailRuleEvaluationRunStore runStore = new();

    [Fact]
    public void JobType_IsTheTypeTheScheduleEnqueues()
    {
        // Assert
        Assert.Equal(JobType.RunScheduledMailRules, this.CreateHandler().JobType);
    }

    [Fact]
    public async Task RunAsync_APayloadNamingAnAccount_RecordsAScheduledRunForIt()
    {
        // Act
        await this.CreateHandler().RunAsync(
            RunScheduledMailRulesJobPayload.For(Account),
            TestContext.Current.CancellationToken);

        // Assert
        var run = this.runStore.Find(Account);
        Assert.Equal(MailRuleExecutionTrigger.ScheduledRun, run?.Trigger);
        Assert.Equal(DispatchedAt, run?.RequestedAt);
    }

    /// <summary>The queue may hand one job to a worker twice, so the second delivery has to change nothing.</summary>
    [Fact]
    public async Task RunAsync_TheSameOccasionDeliveredTwice_RecordsOneRun()
    {
        // Arrange
        var handler = this.CreateHandler();
        var payload = RunScheduledMailRulesJobPayload.For(Account);

        // Act
        await handler.RunAsync(payload, TestContext.Current.CancellationToken);
        await handler.RunAsync(payload, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(this.runStore.Saves);
    }

    /// <summary>A payload of another shape is a defect in what enqueued it, and is reported rather than walked past.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherShape_IsRefused()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => this.CreateHandler().RunAsync(
            new ClassifyEmailSpamJobPayload
            {
                OwnerId = SyntheticMailOwner.Deployment.Value,
                AccountId = Account.Id.Value,
                FolderAlias = "inbox",
                FolderResolutionGeneration = 1,
                UidValidity = 42,
                Uid = 7,
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("payload", refusal.ParamName);
    }

    private ScheduledMailRuleRunHandler CreateHandler()
    {
        var timeProvider = new FakeTimeProvider(DispatchedAt);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new ScheduledMailRuleRunHandler(new MailRuleEvaluationRunRequests(
            this.runStore,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
            timeProvider,
            // The occasion runs under the process rather than a caller, which is the arrangement the scheduled path is
            // written for and requires: it asks for no permission and for the process itself, which is what the host
            // reports outside a request.
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process)));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
