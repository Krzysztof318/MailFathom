// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>Hands out the recurring dispatches this instance's configuration currently declares.</summary>
/// <remarks>
/// <para>
/// Read once per pass rather than held, because the declarations come from configuration and a reload replaces them.
/// A schedule an edit removed stops being dispatched at the next pass, and one it added is dispatched from the next pass
/// onwards; neither needs the process restarted and neither reaches a pass that has already begun.
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
    /// <returns>The schedules, empty when the deployment declares none.</returns>
    IReadOnlyList<ScheduledJob> ReadSchedules();
}
