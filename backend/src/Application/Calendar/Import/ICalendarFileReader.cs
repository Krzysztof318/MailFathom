// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>Reads the entries of an iCalendar file somebody offered to their calendar.</summary>
/// <remarks>
/// <para>
/// The file is untrusted input in the strongest sense this system has: a person is handed it by somebody else and
/// chooses it from disk, so nothing in it has been through a mail server, a rule, or any other reading this deployment
/// performs. What an implementation owes is therefore stated as much by what it must not do as by what it answers
/// with. <b>It reaches no network.</b> An entry naming a URL, attaching a file, or declaring a zone by reference is
/// read as the octets it is; nothing in a file causes a request of any kind, so an import cannot be made to fetch an
/// address on the operator's network or to announce that the file was opened.
/// </para>
/// <para>
/// It is synchronous and takes the whole file in memory, which the bound above it is what makes safe: the caller has
/// already refused anything larger than <see cref="CalendarFileImport.MaximumFileBytes" />, so there is no streaming
/// parse to write and no partial reading to reason about.
/// </para>
/// <para>
/// It answers rather than raising. A file that is not iCalendar, or is iCalendar the reader cannot finish, is one of
/// this port's stated outcomes, because a person choosing the wrong file is an ordinary thing to do and not a fault in
/// the deployment.
/// </para>
/// </remarks>
public interface ICalendarFileReader
{
    /// <summary>Reads what a file offers, resolving every instant it names.</summary>
    /// <param name="file">The octets the person chose, which the caller has already bounded.</param>
    /// <param name="zoneForUnzonedTimes">The zone an entry stating neither a zone nor a UTC instant is read in, and the one an all-day entry's day opens in.</param>
    /// <returns>The entries that could become events and one reason per entry that could not, or why the whole file was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A floating time is one the file states with no zone at all, which RFC 5545 defines as the local time of
    /// whoever reads it — so it is the reader that has to be told which local that is, rather than the file. An
    /// all-day entry is the same question asked about a date: the day it names opens at a different instant in every
    /// zone, and midnight in whichever zone the server happens to run in is the one answer that is certainly wrong.
    /// </remarks>
    CalendarFileReading Read(Stream file, TimeZoneInfo zoneForUnzonedTimes);
}
