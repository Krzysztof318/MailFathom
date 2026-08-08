// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Application.Persistence;

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
    public Task CommitAsync(
        Func<IPersistenceSession, CancellationToken, Task> stageChangesAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stageChangesAsync);

        return this.CommitAsync<object?>(
            async (session, attemptCancellationToken) =>
            {
                await stageChangesAsync(session, attemptCancellationToken);

                return null;
            },
            cancellationToken);
    }

    /// <summary>Stages and commits a complete local write that answers with what it wrote.</summary>
    /// <typeparam name="TResult">What the write answers with.</typeparam>
    /// <param name="stageChangesAsync">Stages the complete idempotent local write in the supplied session and answers with its result.</param>
    /// <param name="cancellationToken">Cancels session creation, staging, commit, or a subsequent retry.</param>
    /// <returns>What the attempt that committed produced.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt conflicted.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels before or between attempts.</exception>
    /// <remarks>
    /// The result of an attempt that did not commit is discarded, which is the whole reason this exists rather than a
    /// caller assigning to a captured local: a variable written by the losing attempt keeps its value, so code reading
    /// it after a conflict would be reading a row that was rolled back.
    /// </remarks>
    public async Task<TResult> CommitAsync<TResult>(
        Func<IPersistenceSession, CancellationToken, Task<TResult>> stageChangesAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stageChangesAsync);

        for (var attemptNumber = 1; attemptNumber <= this.maximumAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var session = await this.sessionFactory.BeginSessionAsync(cancellationToken);
            var result = await stageChangesAsync(session, cancellationToken);

            if (await session.CommitAsync(cancellationToken) == PersistenceCommitResult.Committed)
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
