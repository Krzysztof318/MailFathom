// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Spaces requests to one AI provider out so a deployment never sends faster than it declared it would.</summary>
/// <remarks>
/// <para>
/// A rate ceiling and the spend ceiling beside it bound different things and neither substitutes for the other: the
/// budget decides how much a period may cost, this decides how quickly that cost is allowed to accumulate. It exists
/// because a provider quota is stated per minute rather than per month, and because being refused with a rate-limit
/// response costs an attempt, a retry, and a place in a circuit-breaker window that other work is measured in.
/// </para>
/// <para>
/// One instance paces one workload rather than one provider. Embedding a mailbox's passages and describing its pictures
/// are two bulk workloads against two declared endpoints with two quotas, so each takes a pacer of its own built from
/// its own rate and named by its own workload; sharing one would make either workload's burst spend the other's slots.
/// </para>
/// <para>
/// It is also not the concurrency limit. How many calls may be in flight at once is the resilience pipeline's
/// <c>AiProviderInvocation</c> budget, which is the one mechanism that owns that question and is deliberately still a
/// process's; a second limiter counting in-flight calls here would make two settings answer for one behaviour.
/// </para>
/// <para>
/// The pacing is a slot reservation rather than a token bucket that refills on a timer: a caller takes the next free
/// slot, moves the marker forward by one interval, and waits for its own slot to arrive. The marker lives in the
/// database rather than in this process, which is what makes the declared rate the deployment's instead of each
/// replica's, and it is the only part of the wait that reaches one — nothing polls, nothing spins, and the wait itself
/// is a single cancellable delay measured on the injected <see cref="TimeProvider" />, which is what lets a test prove
/// that the ceiling binds and then releases rather than proving it against a wall clock.
/// </para>
/// </remarks>
public sealed class ProviderRequestPacer
{
    private readonly string workload;
    private readonly TimeSpan interval;
    private readonly IProviderPaceMarker marker;
    private readonly TimeProvider timeProvider;

    private ProviderRequestPacer(
        string workload,
        TimeSpan interval,
        IProviderPaceMarker marker,
        TimeProvider timeProvider)
    {
        this.workload = workload;
        this.interval = interval;
        this.marker = marker;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether this pacer delays nothing, which is what a rate of zero asked for.</summary>
    public bool IsUnpaced => this.interval == TimeSpan.Zero;

    /// <summary>Builds a pacer for one workload from the rate a deployment declared.</summary>
    /// <param name="workload">Which paced workload this is, as its marker is recorded.</param>
    /// <param name="maxRequestsPerMinute">The requests one minute may carry, or zero to pace nothing.</param>
    /// <param name="marker">Hands out this workload's slots to every replica.</param>
    /// <param name="timeProvider">Measures the waits.</param>
    /// <returns>The pacer.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workload" /> names nothing.</exception>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the rate is negative.</exception>
    public static ProviderRequestPacer Create(
        string workload,
        int maxRequestsPerMinute,
        IProviderPaceMarker marker,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(workload);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRequestsPerMinute);

        return new ProviderRequestPacer(
            workload,
            maxRequestsPerMinute == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(1) / maxRequestsPerMinute,
            marker,
            timeProvider);
    }

    /// <summary>Waits until this deployment is allowed to send the paced workload's next request.</summary>
    /// <param name="cancellationToken">Abandons the wait when the caller stops or the host shuts down.</param>
    /// <returns>A task that completes when the slot has arrived, immediately where nothing is paced.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the wait is cancelled, which leaves the reservation spent and the next caller no worse off than one slot.</exception>
    /// <remarks>
    /// The slot is reserved in one statement and waited for afterwards, so a caller that has to wait a second does not
    /// hold every other caller behind it for that second — they take the slots after it and wait for their own. A
    /// deployment that paces nothing reaches no database, which is what keeps an unpaced workload as free as it was.
    /// </remarks>
    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        if (this.IsUnpaced)
        {
            return;
        }

        var wait = await this.marker.ReserveNextSlotAsync(this.workload, this.interval, cancellationToken);

        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(wait, this.timeProvider, cancellationToken);
    }
}
