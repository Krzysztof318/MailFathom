// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Runs one operation from several callers at once, so an idempotency claim is proved against a real race.</summary>
/// <remarks>
/// <para>
/// Synchronization, object writes, and outbox processing are all required to be idempotent, and what a deployment does
/// is not call such an operation twice in sequence but call it twice at the same moment, from two workers. A sequential
/// test observes the second call finding what the first committed, which is the case that was never in doubt. The
/// window a defect lives in is the one where neither caller can see the other's uncommitted write, and the database is
/// the only thing that can close it.
/// </para>
/// <para>
/// Nothing here orders the attempts, and nothing may. The claim under test is about PostgreSQL's own concurrency, so a
/// scheduler or an injected barrier that made the interleaving repeatable would prove the opposite of what is wanted —
/// that the operation is idempotent under the one ordering the harness chose. Every attempt is started on the thread
/// pool and left to collide, which is why what a test asserts afterwards is a count of what survived rather than a
/// sequence of steps.
/// </para>
/// </remarks>
internal static class ConcurrentIdempotency
{
    /// <summary>Runs one operation from several callers at once and reports what each of them did.</summary>
    /// <typeparam name="TResult">What one attempt answers with.</typeparam>
    /// <param name="operation">The operation under test, spelled the way a failure should report it.</param>
    /// <param name="attempts">How many callers run it at once, which the calling class states as a constant of its own.</param>
    /// <param name="attempt">One caller's whole act, given its ordinal so it can vary what it writes where a test needs that.</param>
    /// <param name="cancellationToken">Cancels every attempt.</param>
    /// <returns>What the attempts produced and what they threw, for the caller to state its effect count against.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when fewer than two callers are asked for, which races nothing.</exception>
    /// <remarks>
    /// An attempt that throws is one of the outcomes rather than a failure of the run: a duplicate refused by a unique
    /// index is how several of these claims are enforced, so the exception is collected and reported beside the effect
    /// count instead of ending the test at whichever caller lost.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An attempt's failure is one of the outcomes under test and is reported beside the effect count.")]
    internal static async Task<ConcurrentAttempts<TResult>> RunAsync<TResult>(
        string operation,
        int attempts,
        Func<int, CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 2);
        ArgumentNullException.ThrowIfNull(attempt);

        var results = new ConcurrentBag<TResult>();
        var failures = new ConcurrentBag<Exception>();

        // Each attempt is queued rather than invoked, so no caller runs the synchronous half of its act while the
        // previous one is already at the database. That is what leaves them overlapping instead of staggered.
        await Task.WhenAll(Enumerable.Range(0, attempts).Select(ordinal => Task.Run(
            async () =>
            {
                try
                {
                    results.Add(await attempt(ordinal, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The run was cancelled rather than lost, so it is reported as a cancellation. Collecting it would
                    // leave every attempt "failed" and the assertion afterwards describing an effect count nobody was
                    // still writing.
                    throw;
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            },
            cancellationToken)));

        return new ConcurrentAttempts<TResult>(operation, attempts, [.. results], [.. failures]);
    }
}
