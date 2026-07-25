// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using NSubstitute;
using Xunit;

namespace MailMcp.Application.UnitTests;

public sealed class OptimisticConcurrencyRetryPolicyTests
{
    [Fact]
    public async Task CommitAsync_ConflictThenCommitted_UsesFreshSessionForEachAttempt()
    {
        // Arrange
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstSession = Substitute.For<IPersistenceSession>();
        var secondSession = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(firstSession, secondSession);
        firstSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.ConcurrencyConflict);
        secondSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        var stagedSessions = new List<IPersistenceSession>();
        var policy = new OptimisticConcurrencyRetryPolicy(sessionFactory, maximumAttempts: 3);

        // Act
        var result = await policy.CommitAsync(
            (session, _) =>
            {
                stagedSessions.Add(session);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, result);
        Assert.Equal([firstSession, secondSession], stagedSessions);
        await firstSession.Received(1).DisposeAsync();
        await secondSession.Received(1).DisposeAsync();
        await sessionFactory.Received(2).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CommitAsync_AllAttemptsConflict_ReturnsConflictAfterConfiguredMaximum()
    {
        // Arrange
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var sessions = Enumerable.Range(0, 3)
            .Select(_ => Substitute.For<IPersistenceSession>())
            .ToArray();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(sessions[0], sessions[1], sessions[2]);
        foreach (var session in sessions)
        {
            session.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.ConcurrencyConflict);
        }

        var stagedAttemptCount = 0;
        var policy = new OptimisticConcurrencyRetryPolicy(sessionFactory, maximumAttempts: 3);

        // Act
        var result = await policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, result);
        Assert.Equal(3, stagedAttemptCount);
        await sessionFactory.Received(3).BeginSessionAsync(CancellationToken.None);
        foreach (var session in sessions)
        {
            await session.Received(1).DisposeAsync();
        }
    }

    [Fact]
    public async Task CommitAsync_CancelledAfterConflict_StopsBeforeOpeningAnotherSession()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var session = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(cancellation.Token).Returns(session);
        session.CommitAsync(cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return PersistenceCommitResult.ConcurrencyConflict;
        });
        var policy = new OptimisticConcurrencyRetryPolicy(sessionFactory, maximumAttempts: 3);

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => policy.CommitAsync(
            static (_, _) => Task.CompletedTask,
            cancellation.Token));

        // Assert
        await sessionFactory.Received(1).BeginSessionAsync(cancellation.Token);
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task CommitAsync_StagingFails_PropagatesFailureWithoutCommitOrRetry()
    {
        // Arrange
        var expected = new InvalidOperationException("staging failed");
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var session = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(session);
        var policy = new OptimisticConcurrencyRetryPolicy(sessionFactory, maximumAttempts: 3);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => policy.CommitAsync(
            (_, _) => throw expected,
            CancellationToken.None));

        // Assert
        Assert.Same(expected, thrown);
        await session.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
        await sessionFactory.Received(1).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_NonPositiveMaximumAttempts_Throws()
    {
        // Arrange
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();

        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new OptimisticConcurrencyRetryPolicy(sessionFactory, maximumAttempts: 0));

        // Assert
        Assert.Equal("maximumAttempts", thrown.ParamName);
    }
}
