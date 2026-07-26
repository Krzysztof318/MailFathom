// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using MailMcp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class EfCorePersistenceSessionTests
{
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

        return (resources, new EfCorePersistenceSession(resources));
    }

    private sealed class TestPersistenceSessionResources : IEfCorePersistenceSessionResources
    {
        public MailMcpDbContext DbContext => throw new NotSupportedException();

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
}
