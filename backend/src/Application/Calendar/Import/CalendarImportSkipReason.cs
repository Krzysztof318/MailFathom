// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>Why one entry of an imported file became no event.</summary>
/// <remarks>
/// <para>
/// A skip is not a failure. The file is somebody else's work — a conference programme, an export from another
/// calendar — so it carries entries this deployment has no shape for, and refusing the whole file over one of them
/// would make a schedule unusable because of a single line in it. What the import owes instead is a count per reason,
/// which is what lets a person see that ten of eighty entries were recurring before they confirm the other seventy.
/// </para>
/// <para>
/// None of these names what the entry said. A title, a date, and an identifier are personal data whichever file they
/// arrived in, so a reason is a kind rather than a value, and the report carries how many of each rather than which.
/// </para>
/// </remarks>
public enum CalendarImportSkipReason
{
    /// <summary>The entry states a recurrence rule, and this deployment holds single events only.</summary>
    /// <remarks>
    /// Expanding one into a series would write events the file does not contain, and nothing here could later tell
    /// them apart from events somebody typed. So it is skipped and counted, which is a statement the person reads
    /// rather than a silence.
    /// </remarks>
    Recurring = 0,

    /// <summary>The entry names no start, so there is no day to put it on.</summary>
    NoStart = 1,

    /// <summary>The entry's summary is blank, too long, or carries a character that renders as nothing.</summary>
    UnreadableTitle = 2,

    /// <summary>The entry names no identifier, or one this deployment will not hold.</summary>
    /// <remarks>
    /// RFC 5545 requires a <c>UID</c> of every entry, and it is what recognizes a second import of the same file. An
    /// entry without one could only be written as an event nothing would ever match again, so it is skipped instead.
    /// </remarks>
    UnreadableIdentifier = 3,

    /// <summary>The entry names a time zone this deployment does not know.</summary>
    UnknownTimeZone = 4,

    /// <summary>The entry states an end that is not after its start.</summary>
    EndNotAfterStart = 5,

    /// <summary>This calendar already holds the entry, under the identifier it names itself by.</summary>
    AlreadyOnTheCalendar = 6,

    /// <summary>The file names the same identifier more than once, and only the first of them is read.</summary>
    /// <remarks>
    /// Kept apart from <see cref="AlreadyOnTheCalendar" /> because the two are different facts about the person's
    /// situation: one says the import has been done before, the other says the file repeats itself.
    /// </remarks>
    RepeatedInTheFile = 7,
}
