// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;

namespace MailFathom.Application.Persistence;

/// <summary>Replays a safe local write after an optimistic concurrency conflict or a transient database failure.</summary>
/// <remarks>
/// <para>
/// This policy is the only place where a conflict is an expected control-flow branch rather than a failure. Callers
/// opt in by supplying a write that is idempotent and safe to repeat from a fresh read; once the configured attempts
/// are exhausted the conflict leaves the policy as <see cref="PersistenceConcurrencyConflictException" /> so no
/// intermediate use-case code has to restate it.
/// </para>
/// <para>
/// A database failure that can clear on its own is replayed by the same loop, because the unit of work is the only
/// thing that can be repeated: a dropped connection takes its transaction with it, so nothing below this can retry a
/// statement and nothing above this holds the staging body. It is the same attempt bound and the same backoff, for
/// the same reason a caller already accepted — that this body may run more than once. The failure of the last allowed
/// attempt leaves the policy as <see cref="PersistenceTransientFailureException" />.
/// </para>
/// <para>
/// This is the one layer around a local write, which is what keeps the single layer of retry
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/outbound-resilience.md">outbound resilience</see>
/// requires checkable: no database call runs under a resilience pipeline, and EF Core's own retrying execution
/// strategy stays off.
/// </para>
/// </remarks>
public sealed class OptimisticConcurrencyRetryPolicy
{
    /// <summary>The delay the first retry of a conflicted commit is drawn around.</summary>
    /// <remarks>
    /// A conflict is resolved by reading and writing again in the same process, so the whole curve is measured in
    /// milliseconds rather than in the minutes a scheduler's backoff spans.
    /// </remarks>
    private static readonly TimeSpan ConflictRetryBaseDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>The ceiling a grown commit-retry delay never exceeds.</summary>
    private static readonly TimeSpan ConflictRetryMaxDelay = TimeSpan.FromMilliseconds(1000);

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
    /// <exception cref="PersistenceTransientFailureException">Thrown when the last allowed attempt met a database failure that can clear on its own.</exception>
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
    /// <exception cref="PersistenceTransientFailureException">Thrown when the last allowed attempt met a database failure that can clear on its own.</exception>
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

            try
            {
                await using var session = await this.sessionFactory.BeginSessionAsync(cancellationToken);
                var result = await stageChangesAsync(session, cancellationToken);

                if (await session.CommitAsync(cancellationToken) == PersistenceCommitResult.Committed)
                {
                    return result;
                }
            }
            catch (PersistenceTransientFailureException) when (attemptNumber < this.maximumAttempts)
            {
                // Replayed rather than repeated: the connection that carried the attempt is gone and took its
                // transaction with it, so the next attempt stages the same work again from a fresh read on a
                // connection the pool opens anew. The last attempt's failure is left to leave this policy, because a
                // caller that cannot be told the write did not happen would go on as though it had.
            }

            if (attemptNumber < this.maximumAttempts)
            {
                await Task.Delay(
                    JitteredRetryBackoff.DelayBeforeNextAttempt(
                        ConflictRetryBaseDelay,
                        ConflictRetryMaxDelay,
                        minimumDelay: TimeSpan.Zero,
                        attemptNumber),
                    this.timeProvider,
                    cancellationToken);
            }
        }

        throw new PersistenceConcurrencyConflictException(
            $"A local write did not commit within the configured {this.maximumAttempts} optimistic concurrency attempts.");
    }
}
