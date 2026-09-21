// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar.Import;

/// <summary>One entry of an iCalendar file, read far enough to be an event and no further.</summary>
/// <remarks>
/// <para>
/// This is what the reader answers with instead of the parsed entry it read: the file format's own types stay inside
/// the adapter that owns parsing, and what crosses the port is the five facts a MailFathom event is made of. Nothing
/// an entry additionally carries — its organizer, its participants, its location, its description, the files it
/// attaches, the addresses it names — is read at all, so an import cannot become a way to put somebody else's text,
/// or a link to somebody else's server, into this deployment.
/// </para>
/// <para>
/// The instants are resolved by the time the entry reaches here, because resolving one is what the file's zone
/// declarations are for and the reader is the only thing that has them. What is left for the import to decide is
/// whether the calendar already holds the entry.
/// </para>
/// </remarks>
/// <param name="Uid">The identifier the entry named itself by, which is what recognizes a second import of the file.</param>
/// <param name="Title">What the entry calls itself, read from its summary.</param>
/// <param name="Start">The instant it begins, resolved from whatever the file stated it in.</param>
/// <param name="End">The instant it ends, or <see langword="null" /> where the entry stated none.</param>
/// <param name="IsAllDay">Whether the entry names a day rather than a clock time.</param>
public sealed record CalendarFileEntry(
    ImportedCalendarEventUid Uid,
    CalendarEventTitle Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay);
