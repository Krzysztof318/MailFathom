// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;

namespace MailMcp.Application.Persistence;

/// <summary>Retries a safe local write after optimistic concurrency conflicts.</summary>
internal sealed class OptimisticConcurrencyRetryPolicy
{
    private readonly IPersistenceSessionFactory sessionFactory;
    private readonly int maximumAttempts;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a retry policy with a bounded total attempt count.</summary>
    public OptimisticConcurrencyRetryPolicy(
        IPersistenceSessionFactory sessionFactory,
        int maximumAttempts)
        : this(
            sessionFactory,
            maximumAttempts,
            TimeProvider.System)
    {
    }

    /// <summary>Initializes a retry policy with a bounded total attempt count and testable time source.</summary>
    public OptimisticConcurrencyRetryPolicy(
        IPersistenceSessionFactory sessionFactory,
        int maximumAttempts,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        this.sessionFactory = sessionFactory;
        this.maximumAttempts = maximumAttempts;
        this.timeProvider = timeProvider;
    }

    /// <summary>Stages and commits a complete local write in a fresh session for every attempt.</summary>
    /// <param name="stageChangesAsync">Stages the complete idempotent local write in the supplied session.</param>
    /// <param name="cancellationToken">Cancels session creation, staging, commit, or a subsequent retry.</param>
    /// <returns>The successful commit result, or a concurrency conflict after all attempts are exhausted.</returns>
    public async Task<PersistenceCommitResult> CommitAsync(
        Func<IPersistenceSession, CancellationToken, Task> stageChangesAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stageChangesAsync);

        for (var attemptNumber = 1; attemptNumber <= this.maximumAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var session = await this.sessionFactory.BeginSessionAsync(cancellationToken);
            await stageChangesAsync(session, cancellationToken);

            var result = await session.CommitAsync(cancellationToken);
            if (result == PersistenceCommitResult.Committed)
            {
                return result;
            }

            if (attemptNumber < this.maximumAttempts)
            {
                await Task.Delay(
                    CreateJitteredRetryDelay(attemptNumber),
                    this.timeProvider,
                    cancellationToken);
            }
        }

        return PersistenceCommitResult.ConcurrencyConflict;
    }

    private static TimeSpan CreateJitteredRetryDelay(int completedAttemptCount)
    {
        var exponentialCeilingMilliseconds = Math.Min(
            1000,
            50 * (1 << Math.Min(completedAttemptCount - 1, 5)));
        var minimumMilliseconds = exponentialCeilingMilliseconds / 2;
        var jitteredMilliseconds = RandomNumberGenerator.GetInt32(
            minimumMilliseconds,
            exponentialCeilingMilliseconds + 1);

        return TimeSpan.FromMilliseconds(jitteredMilliseconds);
    }
}
