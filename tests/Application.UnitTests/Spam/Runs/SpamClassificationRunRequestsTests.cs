// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Runs;

/// <summary>Covers the one thing the request path decides: whether this request is what puts a run in front of an account.</summary>
public sealed class SpamClassificationRunRequestsTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private readonly InMemorySpamClassificationRunStore runStore = new();

    private readonly FakeTimeProvider timeProvider = new(RequestedAt);

    [Fact]
    public async Task SubmitAsync_AccountWithNoRunOutstanding_RecordsOneAndAcceptsTheRequest()
    {
        // Arrange, Act
        var request = await this.CreateRequests().SubmitAsync(
            Account,
            TermsOf(SpamActionPosture.DryRun),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(RequestedAt, request.Run.RequestedAt);
        Assert.True(request.Run.IsOutstanding);
        Assert.Equal(SpamActionPosture.DryRun, this.runStore.Find(Account)?.Terms.Posture);
    }

    /// <summary>Terms cannot be changed under a walk that is half done, so the second request is answered rather than applied.</summary>
    [Fact]
    public async Task SubmitAsync_RunAlreadyOutstanding_AnswersWithItAndLeavesItsTermsAlone()
    {
        // Arrange
        var requests = this.CreateRequests();
        await requests.SubmitAsync(
            Account,
            TermsOf(SpamActionPosture.DryRun),
            TestContext.Current.CancellationToken);

        this.timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await requests.SubmitAsync(
            Account,
            TermsOf(SpamActionPosture.Acting),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(second.Accepted);
        Assert.Equal(RequestedAt, second.Run.RequestedAt);
        Assert.Equal(SpamActionPosture.DryRun, second.Run.Terms.Posture);
        Assert.Single(this.runStore.Saves);
    }

    [Fact]
    public async Task SubmitAsync_PreviousRunAlreadyEnded_AcceptsANewOne()
    {
        // Arrange
        this.runStore.Arrange(new SpamClassificationRun
        {
            AccountId = Account,
            RequestedAt = RequestedAt.AddDays(-1),
            Terms = TermsOf(SpamActionPosture.DryRun),
            EndedAt = RequestedAt.AddDays(-1).AddMinutes(3),
            Ending = SpamClassificationRunEnding.Completed,
        });

        // Act
        var request = await this.CreateRequests().SubmitAsync(
            Account,
            TermsOf(SpamActionPosture.Acting),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(request.Accepted);
        Assert.Equal(RequestedAt, this.runStore.Find(Account)?.RequestedAt);
        Assert.Null(this.runStore.Find(Account)?.Ending);
    }

    private static SpamClassificationRunTerms TermsOf(SpamActionPosture posture) =>
        SpamClassificationRunTerms.Create([Inbox], posture, rescores: false);

    private SpamClassificationRunRequests CreateRequests()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new SpamClassificationRunRequests(
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
