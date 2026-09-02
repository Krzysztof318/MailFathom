// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>Hands out the recurring dispatches one part of this instance currently declares.</summary>
/// <remarks>
/// <para>
/// Read once per pass rather than held, because what a source declares changes underneath it. A configured schedule an
/// edit removed stops being dispatched at the next pass and one it added is dispatched from the next pass onwards, and
/// a stored declaration behaves the same way the moment somebody makes or stops one; neither needs the process
/// restarted and neither reaches a pass that has already begun.
/// </para>
/// <para>
/// There is more than one source, and that is what this contract is for rather than an accident of registration. The
/// rules a deployment configures and the messages an owner asked to repeat are declared by different parts of the
/// system, out of different places, and both want the one mechanism underneath: the same occasion arithmetic, the same
/// one-run-at-a-time guarantee, the same capacity bounds, and the same worker.
/// </para>
/// <para>
/// The read is asynchronous because a source may hold its declarations in the database. Nothing about the contract
/// assumes either — a source over configuration answers without waiting for anything, and one over stored state reads
/// a bounded query — and a pass takes what each one returns without asking where it came from.
/// </para>
/// <para>
/// An implementation returns what it declares and decides nothing about time. Whether an occasion has passed, whether
/// one was missed, and whether the previous run is still going all belong to <see cref="JobSchedulePass" />, so a
/// consumer contributes what it wants repeated and gets the mechanism's guarantees without restating them.
/// </para>
/// </remarks>
public interface IScheduledJobSource
{
    /// <summary>Reads the schedules this source declares as they stand now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The schedules, empty when this source declares none.</returns>
    Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken);
}
