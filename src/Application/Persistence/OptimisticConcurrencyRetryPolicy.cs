// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;

namespace MailMcp.Application.Persistence;

/// <summary>Retries a safe local write after optimistic concurrency conflicts.</summary>
/// <remarks>
/// This policy is the only place where a conflict is an expected control-flow branch rather than a failure. Callers
/// opt in by supplying a write that is idempotent and safe to repeat from a fresh read; once the configured attempts
/// are exhausted the conflict leaves the policy as <see cref="PersistenceConcurrencyConflictException" /> so no
/// intermediate use-case code has to restate it.
/// </remarks>
public sealed class OptimisticConcurrencyRetryPolicy
{
    private readonly IPersistenceSessionFactory sessionFactory;
    private readonly TimeProvider timeProvider;
    private readonly int maximumAttempts;

    /// <summary>Initializes a retry policy from the deployment-wide concurrency bound.</summary>
    /// <param name="sessionFactory">Creates a fresh persistence session for every attempt.</param>
    /// <param name="options">Supplies the maximum attempt count.</param>
    /// <param name="timeProvider">Measures the backoff between attempts.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured attempt count is below one.</exception>
    public OptimisticConcurrencyRetryPolicy(
        IPersistenceSessionFactory sessionFactory,
        PersistenceConcurrencyOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumCommitAttempts, 1, nameof(options));

        this.sessionFactory = sessionFactory;
        this.timeProvider = timeProvider;
        this.maximumAttempts = options.MaximumCommitAttempts;
    }

    /// <summary>Stages and commits a complete local write in a fresh session for every attempt.</summary>
    /// <param name="stageChangesAsync">Stages the complete idempotent local write in the supplied session.</param>
    /// <param name="cancellationToken">Cancels session creation, staging, commit, or a subsequent retry.</param>
    /// <returns>A task that completes once one attempt has committed.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt conflicted.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels before or between attempts.</exception>
    public async Task CommitAsync(
        Func<IPersistenceSession, CancellationToken, Task> stageChangesAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stageChangesAsync);

        for (var attemptNumber = 1; attemptNumber <= this.maximumAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var session = await this.sessionFactory.BeginSessionAsync(cancellationToken);
            await stageChangesAsync(session, cancellationToken);

            if (await session.CommitAsync(cancellationToken) == PersistenceCommitResult.Committed)
            {
                return;
            }

            if (attemptNumber < this.maximumAttempts)
            {
                await Task.Delay(
                    CreateJitteredRetryDelay(attemptNumber),
                    this.timeProvider,
                    cancellationToken);
            }
        }

        throw new PersistenceConcurrencyConflictException(
            $"A local write did not commit within the configured {this.maximumAttempts} optimistic concurrency attempts.");
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
