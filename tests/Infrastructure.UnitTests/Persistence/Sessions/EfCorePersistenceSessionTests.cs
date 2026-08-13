// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Sessions;

public sealed class EfCorePersistenceSessionTests
{
    private const string CommitsInstrumentName = "mailfathom.persistence.commits";

    private const string OutcomeTagName = "mailfathom.persistence.commit.outcome";

    [Fact]
    public async Task CommitAsync_DbUpdateConcurrencyException_RollsBackAndReturnsConflict()
    {
        // Arrange
        var (resources, persistenceSession) = CreateSession(new DbUpdateConcurrencyException());
        await using var session = persistenceSession;

        // Act
        var result = await session.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, result);
        Assert.Equal(1, resources.RollbackCount);
        Assert.Equal(0, resources.CommitCount);
    }

    [Fact]
    public async Task CommitAsync_ClassifiedDbUpdateException_RollsBackAndReturnsConflict()
    {
        // Arrange
        var (resources, persistenceSession) = CreateSession(
            new DbUpdateException("checkpoint creation conflict"),
            classifiesSaveChangesExceptionAsConcurrencyConflict: true);
        await using var session = persistenceSession;

        // Act
        var result = await session.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, result);
        Assert.Equal(1, resources.RollbackCount);
        Assert.Equal(0, resources.CommitCount);
    }

    [Fact]
    public async Task CommitAsync_SaveSucceeds_CommitsAndReturnsCommitted()
    {
        // Arrange
        var (resources, persistenceSession) = CreateSession();
        await using var session = persistenceSession;

        // Act
        var result = await session.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, result);
        Assert.Equal(1, resources.SaveChangesCount);
        Assert.Equal(1, resources.CommitCount);
        Assert.Equal(0, resources.RollbackCount);
    }

    [Fact]
    public async Task CommitAsync_NonConcurrencyFailure_PropagatesWithoutCompletingTransaction()
    {
        // Arrange
        var expected = new InvalidOperationException("save failed");
        var (resources, persistenceSession) = CreateSession(expected);
        await using var session = persistenceSession;

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.CommitAsync(CancellationToken.None));

        // Assert
        Assert.Same(expected, thrown);
        Assert.Equal(0, resources.CommitCount);
        Assert.Equal(0, resources.RollbackCount);
    }

    [Fact]
    public async Task DisposeAsync_UncommittedSession_RollsBackDisposesResourcesAndClearsTrackedState()
    {
        // Arrange
        var (resources, session) = CreateSession();

        // Act
        await session.DisposeAsync();

        // Assert
        Assert.Equal(1, resources.RollbackCount);
        Assert.Equal(1, resources.DisposeCount);
        Assert.Equal(1, resources.ClearTrackedStateCount);
    }

    /// <summary>
    /// A conflict rate is only real if the session that observed one says so, and a conflict resolved by a retry leaves
    /// no other trace at all — so this is the wiring that decides whether the counter measures anything.
    /// </summary>
    [Fact]
    public async Task CommitAsync_EitherEnding_CountsItUnderTheOutcomeThatHappened()
    {
        // Arrange
        var (_, committingSession) = CreateSession();
        var (_, conflictingSession) = CreateSession(new DbUpdateConcurrencyException());
        using var measurements = new RecordedMailFathomMeasurements(CommitsInstrumentName);

        // Act
        await using (committingSession)
        {
            _ = await committingSession.CommitAsync(CancellationToken.None);
        }

        await using (conflictingSession)
        {
            _ = await conflictingSession.CommitAsync(CancellationToken.None);
        }

        // Assert
        var outcomes = measurements.DimensionOf(CommitsInstrumentName, OutcomeTagName);

        Assert.Contains("committed", outcomes);
        Assert.Contains("concurrency_conflict", outcomes);
    }

    /// <summary>
    /// Work staged here is work the deployment kept only if this session committed, and an optimistic-concurrency
    /// retry stages the same body again in a fresh session — so a measurement published where the work happens would
    /// count the attempt that lost the race. The ending each held measurement is told is the whole of that fix.
    /// </summary>
    [Fact]
    public async Task MeasureOnEnding_EitherEnding_PublishesTheHeldMeasurementUnderItExactlyOnce()
    {
        // Arrange
        var (_, committingSession) = CreateSession();
        var (_, conflictingSession) = CreateSession(new DbUpdateConcurrencyException());
        var afterCommit = new HeldMeasurement();
        var afterConflict = new HeldMeasurement();

        // Act
        await using (committingSession)
        {
            committingSession.MeasureOnEnding(afterCommit);

            _ = await committingSession.CommitAsync(CancellationToken.None);
        }

        await using (conflictingSession)
        {
            conflictingSession.MeasureOnEnding(afterConflict);

            _ = await conflictingSession.CommitAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal([true], afterCommit.Endings);
        Assert.Equal([false], afterConflict.Endings);
    }

    /// <summary>A session nobody committed is one whose staged work was rolled back, whatever ended it.</summary>
    [Fact]
    public async Task MeasureOnEnding_ASessionDisposedWithoutACommit_PublishesItAsNotCommitted()
    {
        // Arrange
        var (_, persistenceSession) = CreateSession();
        var afterDisposal = new HeldMeasurement();

        // Act
        await using (persistenceSession)
        {
            persistenceSession.MeasureOnEnding(afterDisposal);
        }

        // Assert
        Assert.Equal([false], afterDisposal.Endings);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned session owns and disposes the returned test resources.")]
    private static (
        TestPersistenceSessionResources Resources,
        EfCorePersistenceSession Session) CreateSession(
        Exception? saveChangesException = null,
        bool classifiesSaveChangesExceptionAsConcurrencyConflict = false)
    {
        var resources = new TestPersistenceSessionResources
        {
            SaveChangesException = saveChangesException,
            ClassifiesSaveChangesExceptionAsConcurrencyConflict =
                classifiesSaveChangesExceptionAsConcurrencyConflict,
        };

        return (resources, new EfCorePersistenceSession(resources, new PersistenceCommitTelemetry()));
    }

    private sealed class TestPersistenceSessionResources : IEfCorePersistenceSessionResources
    {
        public MailFathomDbContext DbContext => throw new NotSupportedException();

        public Exception? SaveChangesException { get; init; }

        public bool ClassifiesSaveChangesExceptionAsConcurrencyConflict { get; init; }

        public int SaveChangesCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int ClearTrackedStateCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            this.SaveChangesCount++;

            return this.SaveChangesException is null
                ? Task.CompletedTask
                : Task.FromException(this.SaveChangesException);
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken)
        {
            this.CommitCount++;

            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken)
        {
            this.RollbackCount++;

            return Task.CompletedTask;
        }

        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            this.ClassifiesSaveChangesExceptionAsConcurrencyConflict;

        public void ClearTrackedState()
        {
            this.ClearTrackedStateCount++;
        }

        public ValueTask DisposeAsync()
        {
            this.DisposeCount++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for a measurement of staged work, and remembers every ending it was told about.</summary>
    private sealed class HeldMeasurement : ISessionScopedMeasurement
    {
        private readonly List<bool> endings = [];

        public IReadOnlyList<bool> Endings => this.endings;

        public void PublishAfterSession(bool sessionCommitted) => this.endings.Add(sessionCommitted);
    }
}
