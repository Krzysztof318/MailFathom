// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Covers the one thing the request path decides: whether this request is what puts a run in front of an account.</summary>
public sealed class MailRuleEvaluationRunRequestsTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 4, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");

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
        Assert.Equal(Account, this.runStore.Find(Account)?.AccountId);
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
            AccountId = Account,
            RequestedAt = RequestedAt.AddDays(-1),
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

    private MailRuleEvaluationRunRequests CreateRequests()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleEvaluationRunRequests(
            this.runStore,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                this.timeProvider),
            this.timeProvider);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
