// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Persistence;
using Microsoft.Extensions.Time.Testing;
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
        var policy = CreatePolicy(sessionFactory);

        // Act
        await policy.CommitAsync(
            (session, _) =>
            {
                stagedSessions.Add(session);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Assert
        Assert.Equal([firstSession, secondSession], stagedSessions);
        await firstSession.Received(1).DisposeAsync();
        await secondSession.Received(1).DisposeAsync();
        await sessionFactory.Received(2).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CommitAsync_DefaultOptions_StopsAfterTwoConflictingAttempts()
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
        var policy = CreatePolicy(sessionFactory);

        // Act
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(() => policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        // Assert
        Assert.Equal(2, stagedAttemptCount);
        await sessionFactory.Received(2).BeginSessionAsync(CancellationToken.None);
        await sessions[0].Received(1).DisposeAsync();
        await sessions[1].Received(1).DisposeAsync();
        await sessions[2].DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_AllAttemptsConflict_ThrowsAfterConfiguredMaximum()
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
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3);

        // Act
        var thrown = await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(() => policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        // Assert
        Assert.Contains("3", thrown.Message, StringComparison.Ordinal);
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
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.CommitAsync(
            static (_, _) => Task.CompletedTask,
            cancellation.Token));

        // Assert
        await sessionFactory.Received(1).BeginSessionAsync(cancellation.Token);
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task CommitAsync_ConflictBeforeAnotherAttempt_WaitsForJitteredBackoffWithinBounds()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var firstCommitObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstSession = Substitute.For<IPersistenceSession>();
        var secondSession = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(firstSession, secondSession);
        firstSession.CommitAsync(CancellationToken.None).Returns(_ =>
        {
            firstCommitObserved.SetResult();
            return PersistenceCommitResult.ConcurrencyConflict;
        });
        secondSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        var policy = CreatePolicy(sessionFactory, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            static (_, _) => Task.CompletedTask,
            CancellationToken.None);
        await firstCommitObserved.Task;

        // Assert
        Assert.False(commitTask.IsCompleted);
        await sessionFactory.Received(1).BeginSessionAsync(CancellationToken.None);

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(24));
        Assert.False(commitTask.IsCompleted);
        await sessionFactory.Received(1).BeginSessionAsync(CancellationToken.None);

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(26));
        await commitTask;

        // Assert
        await sessionFactory.Received(2).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CommitAsync_StagingFails_PropagatesFailureWithoutCommitOrRetry()
    {
        // Arrange
        var expected = new InvalidOperationException("staging failed");
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var session = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(session);
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3);

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
            () => CreatePolicy(sessionFactory, maximumCommitAttempts: 0));

        // Assert
        Assert.Equal("options", thrown.ParamName);
    }

    private static OptimisticConcurrencyRetryPolicy CreatePolicy(
        IPersistenceSessionFactory sessionFactory,
        int? maximumCommitAttempts = null,
        TimeProvider? timeProvider = null)
    {
        var options = new PersistenceConcurrencyOptions();
        if (maximumCommitAttempts is { } configuredAttempts)
        {
            options.MaximumCommitAttempts = configuredAttempts;
        }

        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            options,
            timeProvider ?? TimeProvider.System);
    }
}
