// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Reminders;

/// <summary>What kind of thing a reminder is about.</summary>
/// <remarks>
/// The one thing a due reminder carries that the pass cannot read off the lead: which of the deployment's records it
/// belongs to, and therefore which kind of notification is written, what it leads to, and what it is deduplicated
/// under. A reader schedules reminders of one kind, so the value is a constant per adapter rather than something
/// decided per row.
/// </remarks>
public enum ReminderSubject
{
    /// <summary>One event of a person's own calendar.</summary>
    CalendarEvent = 0,

    /// <summary>One task of a person's own list, reminded against its due date.</summary>
    PersonalTask = 1,
}
