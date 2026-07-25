// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

/// <summary>Retries a safe local write after optimistic concurrency conflicts.</summary>
internal sealed class OptimisticConcurrencyRetryPolicy
{
    private readonly IPersistenceSessionFactory sessionFactory;
    private readonly int maximumAttempts;

    /// <summary>Initializes a retry policy with a bounded total attempt count.</summary>
    public OptimisticConcurrencyRetryPolicy(
        IPersistenceSessionFactory sessionFactory,
        int maximumAttempts)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        this.sessionFactory = sessionFactory;
        this.maximumAttempts = maximumAttempts;
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
        }

        return PersistenceCommitResult.ConcurrencyConflict;
    }
}
