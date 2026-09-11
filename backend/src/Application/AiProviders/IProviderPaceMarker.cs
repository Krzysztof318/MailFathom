// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Hands out the slots one paced workload's requests are sent in, across every replica of one deployment.</summary>
/// <remarks>
/// <para>
/// The marker is one instant per workload — when the next request may go out — and reserving a slot is moving it
/// forward by one interval and being told what the caller reserved. It is durable rather than a field because a rate is
/// what a provider states about the deployment: three replicas each pacing themselves send three times what their
/// operator declared, at a provider that answers a per-minute quota with a refusal.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records why a rate moves into the database while an in-flight count stays a process's.
/// </para>
/// <para>
/// A reservation is spent whether or not the caller waits for it. That is what a slot reservation means and it is the
/// same promise the in-process marker made: a caller that gave up leaves the next one no worse off than one slot, and
/// nothing has to be given back for the rate to keep holding.
/// </para>
/// <para>
/// What comes back is how long to wait rather than when the slot falls, because both instants are the database's and
/// only their difference is meaningful to a replica whose own clock may sit anywhere. Subtracting them where they were
/// measured is what keeps a drifting clock from turning a rate into a burst.
/// </para>
/// </remarks>
public interface IProviderPaceMarker
{
    /// <summary>Takes the paced workload's next slot and reports how long its caller has to wait for it.</summary>
    /// <param name="workload">Which paced workload's marker to move, as it is recorded.</param>
    /// <param name="interval">How far apart this workload's requests are spaced.</param>
    /// <param name="cancellationToken">Cancels the reservation.</param>
    /// <returns>How long to wait before sending, which is zero where the slot has already arrived.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workload" /> names nothing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval" /> is not positive.</exception>
    Task<TimeSpan> ReserveNextSlotAsync(string workload, TimeSpan interval, CancellationToken cancellationToken);
}
