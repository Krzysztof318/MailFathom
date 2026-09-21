// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>What became of a file somebody offered to their calendar.</summary>
/// <remarks>
/// Each value but the first is a refusal of the whole file rather than of an entry in it, which is the distinction
/// this type carries: an entry this deployment has no shape for is skipped and counted, while a file that is not
/// iCalendar at all, or is larger than one may be, leaves the calendar untouched and is reported as such.
/// </remarks>
public enum CalendarImportOutcome
{
    /// <summary>The file was read, and the report says what it would create and what it skipped.</summary>
    Read = 0,

    /// <summary>The octets are not iCalendar, or are iCalendar this reader could not finish.</summary>
    NotCalendarData = 1,

    /// <summary>The file names more entries than one import writes.</summary>
    TooManyEntries = 2,

    /// <summary>The request names a time zone this deployment does not know.</summary>
    /// <remarks>
    /// The whole file rather than an entry, because the zone the request states is what every entry naming no zone of
    /// its own is read in: carrying on under a zone nobody asked for would put a person's whole day on the wrong side
    /// of a date boundary.
    /// </remarks>
    UnknownTimeZone = 3,
}
