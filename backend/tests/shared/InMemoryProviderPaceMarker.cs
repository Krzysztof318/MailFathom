// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;

namespace MailFathom.TestSupport;

/// <summary>Hands out each paced workload's slots from memory, on the clock a test controls.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what a pacer test asserts is that a second caller waits an interval
/// longer than the first: a substitute would answer from a script and the assertion would be about the script. It does
/// the same arithmetic the persisted marker's statement does — the marker moves to one interval past whichever is
/// later, its own value or now, and the caller is told the difference — so a burst is spaced here as it would be in a
/// deployment. One dictionary rather than one field, so two workloads pace independently exactly as their two rows do.
/// </remarks>
internal sealed class InMemoryProviderPaceMarker(TimeProvider timeProvider) : IProviderPaceMarker
{
    private readonly Dictionary<string, DateTimeOffset> nextSlotByWorkload = [];

    /// <summary>Gets how many slots were reserved, which is what says an unpaced workload reached nothing.</summary>
    public int ReservationCount { get; private set; }

    /// <inheritdoc />
    public Task<TimeSpan> ReserveNextSlotAsync(string workload, TimeSpan interval, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(workload);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        this.ReservationCount++;

        var now = timeProvider.GetUtcNow();
        var slot = this.nextSlotByWorkload.TryGetValue(workload, out var reserved) && reserved > now ? reserved : now;

        this.nextSlotByWorkload[workload] = slot + interval;

        return Task.FromResult(slot - now);
    }
}
