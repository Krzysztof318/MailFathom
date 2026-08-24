// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Persistence;

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
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (session, _) =>
            {
                stagedSessions.Add(session);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await AdvanceUntilCompletedAsync(clock, commitTask);

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
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => AdvanceUntilCompletedAsync(clock, commitTask));

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
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        var thrown = await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => AdvanceUntilCompletedAsync(clock, commitTask));

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

    /// <summary>
    /// A dropped connection takes its transaction with it, so nothing below this can repeat the statement and the
    /// whole unit of work is what a retry consists of. This is the layer that owns that, and this test is what says
    /// the failure reaches it as a replay rather than as an exception the caller has to understand.
    /// </summary>
    [Fact]
    public async Task CommitAsync_TransientFailureThenCommitted_StagesTheUnitOfWorkAgainInAFreshSession()
    {
        // Arrange
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstSession = Substitute.For<IPersistenceSession>();
        var secondSession = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(firstSession, secondSession);
        firstSession.CommitAsync(CancellationToken.None).Returns<PersistenceCommitResult>(
            _ => throw new PersistenceTransientFailureException(
                "the database failed in a way that can clear on its own",
                new IOException("the connection was reset")));
        secondSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        var stagedSessions = new List<IPersistenceSession>();
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (session, _) =>
            {
                stagedSessions.Add(session);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await AdvanceUntilCompletedAsync(clock, commitTask);

        // Assert
        Assert.Equal([firstSession, secondSession], stagedSessions);
        await firstSession.Received(1).DisposeAsync();
        await secondSession.Received(1).DisposeAsync();
    }

    /// <summary>
    /// A caller that is not told the write did not happen goes on as though it had, so the last attempt's failure
    /// leaves the policy exactly as the database stated it rather than as a conflict that never occurred.
    /// </summary>
    [Fact]
    public async Task CommitAsync_EveryAttemptFailedTransiently_RaisesTheLastFailureRatherThanAConflict()
    {
        // Arrange
        var droppedConnection = new IOException("the connection was reset");
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var sessions = Enumerable.Range(0, 3)
            .Select(_ => Substitute.For<IPersistenceSession>())
            .ToArray();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(sessions[0], sessions[1], sessions[2]);
        foreach (var session in sessions)
        {
            session.CommitAsync(CancellationToken.None).Returns<PersistenceCommitResult>(
                _ => throw new PersistenceTransientFailureException(
                    "the database failed in a way that can clear on its own",
                    droppedConnection));
        }

        var stagedAttemptCount = 0;
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        var thrown = await Assert.ThrowsAsync<PersistenceTransientFailureException>(
            () => AdvanceUntilCompletedAsync(clock, commitTask));

        // Assert
        Assert.Same(droppedConnection, thrown.InnerException);
        Assert.Equal(3, stagedAttemptCount);
        await sessionFactory.Received(3).BeginSessionAsync(CancellationToken.None);
    }

    /// <summary>
    /// Several of the writes a session carries issue their statement rather than staging it, so the connection can be
    /// lost before anything is committed. The attempt that met it is the same attempt, and the replay is the same one.
    /// </summary>
    [Fact]
    public async Task CommitAsync_StagingFailedTransiently_StagesTheUnitOfWorkAgainInAFreshSession()
    {
        // Arrange
        var droppedConnection = new IOException("the connection was reset");
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstSession = Substitute.For<IPersistenceSession>();
        var secondSession = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(firstSession, secondSession);
        firstSession.TryEndOnTransientFailure(droppedConnection).Returns(true);
        secondSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        var stagedSessions = new List<IPersistenceSession>();
        var clock = new FakeTimeProvider();
        var policy = CreatePolicy(sessionFactory, timeProvider: clock);

        // Act
        var commitTask = policy.CommitAsync(
            (session, _) =>
            {
                stagedSessions.Add(session);

                return ReferenceEquals(session, firstSession)
                    ? Task.FromException(droppedConnection)
                    : Task.CompletedTask;
            },
            CancellationToken.None);
        await AdvanceUntilCompletedAsync(clock, commitTask);

        // Assert
        Assert.Equal([firstSession, secondSession], stagedSessions);
        await firstSession.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A commit the server may already have applied is the one persistence failure a replay makes worse: an
    /// accumulating spend total and a blindly inserted audit row would each be written twice. It leaves this policy as
    /// it was raised, for a caller that knows whether its own write may be repeated.
    /// </summary>
    [Fact]
    public async Task CommitAsync_CommitOutcomeUnknown_PropagatesItWithoutStagingTheWriteAgain()
    {
        // Arrange
        var unanswered = new PersistenceCommitOutcomeUnknownException(
            "the connection went away while committing",
            new IOException("the connection was reset"));
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var session = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(CancellationToken.None).Returns(session);
        session.CommitAsync(CancellationToken.None).Returns<PersistenceCommitResult>(_ => throw unanswered);
        var stagedAttemptCount = 0;
        var policy = CreatePolicy(sessionFactory, maximumCommitAttempts: 3);

        // Act
        var thrown = await Assert.ThrowsAsync<PersistenceCommitOutcomeUnknownException>(() => policy.CommitAsync(
            (_, _) =>
            {
                stagedAttemptCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        // Assert
        Assert.Same(unanswered, thrown);
        Assert.Equal(1, stagedAttemptCount);
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

    /// <summary>Advances the fake clock in steps until the policy's own waiting is over, and reports what it ended as.</summary>
    /// <remarks>
    /// The wait before an attempt is registered only once the attempt before it has failed, so the advancing may not
    /// stop on a count: a loop that had spent its steps would leave the next wait pending for a clock that never moves
    /// again. A step past the policy's own ceiling therefore ends each wait whatever the jitter drew, and the loop
    /// keeps stepping until the whole commit has ended. The guard is what ends it on real time in both outcomes, so a
    /// policy that stopped making progress fails the test rather than spinning a clock nothing is waiting on.
    /// </remarks>
    private static async Task AdvanceUntilCompletedAsync(FakeTimeProvider clock, Task commitTask)
    {
        var guardedCommit = commitTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        while (!guardedCommit.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(2));

            await Task.Yield();
        }

        await guardedCommit;
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
