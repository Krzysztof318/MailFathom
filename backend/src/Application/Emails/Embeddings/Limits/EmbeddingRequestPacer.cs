// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Spaces embedding requests out so a deployment never sends faster than it declared it would.</summary>
/// <remarks>
/// <para>
/// A rate ceiling and the spend ceiling beside it bound different things and neither substitutes for the other: the
/// budget decides how much a period may cost, this decides how quickly that cost is allowed to accumulate. It exists
/// because a provider quota is stated per minute rather than per month, and because being refused with a rate-limit
/// response costs an attempt, a retry, and a place in a circuit-breaker window that other work is measured in.
/// </para>
/// <para>
/// It is also not the concurrency limit. How many calls may be in flight at once is the resilience pipeline's
/// <c>AiProviderInvocation</c> budget, which is the one mechanism that owns that question; a second limiter counting
/// in-flight calls here would make two settings answer for one behaviour.
/// </para>
/// <para>
/// The pacing is a slot reservation rather than a token bucket that refills on a timer: a caller takes the next free
/// slot, moves the marker forward by one interval, and waits for its own slot to arrive. Nothing polls, nothing spins,
/// and every wait is a single cancellable delay measured on the injected <see cref="TimeProvider" /> — which is what
/// lets a test prove that the ceiling binds and then releases, rather than proving it against a wall clock.
/// </para>
/// </remarks>
public sealed class EmbeddingRequestPacer
{
    private readonly Lock reservation = new();
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;

    private DateTimeOffset nextSlotAt;

    private EmbeddingRequestPacer(TimeSpan interval, TimeProvider timeProvider)
    {
        this.interval = interval;
        this.timeProvider = timeProvider;
        this.nextSlotAt = DateTimeOffset.MinValue;
    }

    /// <summary>Gets whether this pacer delays nothing, which is what a rate of zero asked for.</summary>
    public bool IsUnpaced => this.interval == TimeSpan.Zero;

    /// <summary>Builds a pacer from the rate a deployment declared.</summary>
    /// <param name="maxRequestsPerMinute">The requests one minute may carry, or zero to pace nothing.</param>
    /// <param name="timeProvider">Measures the waits and decides when a slot has arrived.</param>
    /// <returns>The pacer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the rate is negative.</exception>
    public static EmbeddingRequestPacer Create(int maxRequestsPerMinute, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRequestsPerMinute);

        return new EmbeddingRequestPacer(
            maxRequestsPerMinute == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(1) / maxRequestsPerMinute,
            timeProvider);
    }

    /// <summary>Waits until this deployment is allowed to send its next embedding request.</summary>
    /// <param name="cancellationToken">Abandons the wait when the caller stops or the host shuts down.</param>
    /// <returns>A task that completes when the slot has arrived, immediately where nothing is paced.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the wait is cancelled, which leaves the reservation spent and the next caller no worse off than one slot.</exception>
    /// <remarks>
    /// The slot is reserved under the lock and waited for outside it, so a caller that has to wait a second does not
    /// hold every other caller behind it for that second — they take the slots after it and wait for their own.
    /// </remarks>
    public Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        if (this.IsUnpaced)
        {
            return Task.CompletedTask;
        }

        TimeSpan wait;

        lock (this.reservation)
        {
            var now = this.timeProvider.GetUtcNow();
            var slot = this.nextSlotAt > now ? this.nextSlotAt : now;

            this.nextSlotAt = slot + this.interval;
            wait = slot - now;
        }

        return wait <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(wait, this.timeProvider, cancellationToken);
    }
}
