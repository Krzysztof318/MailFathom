// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using Ical.Net;
using Ical.Net.DataTypes;
using MailFathom.Application.Calendar.Import;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Scheduling;
using MailFathom.Infrastructure.Documents;

namespace MailFathom.Infrastructure.Calendar;

/// <summary>Reads an offered iCalendar file with Ical.Net, and answers in MailFathom's own terms.</summary>
/// <remarks>
/// <para>
/// This is the whole of the repository's contact with RFC 5545. Every Ical.Net type stays inside it — the parsed
/// calendar, its entries, and the <see cref="CalDateTime" /> a date is stated as — so what crosses the port is
/// <see cref="CalendarFileEntry" /> and a reason per entry that could not become one.
/// </para>
/// <para>
/// <b>Six properties of an entry are read and nothing else is.</b> Its identifier, its summary, its start, its end or
/// duration, whether it recurs, and whether it names an occurrence of a series. An entry's organizer, participants,
/// location, description, attachments, and URLs are never touched, which is what makes it true that nothing in a file
/// somebody else wrote reaches this deployment's records or causes a request of any kind.
/// </para>
/// <para>
/// <b>The instant is resolved here because this is where the zone declarations are.</b> A UTC value is already one; a
/// value naming a zone is resolved in that zone; a value naming neither is a floating time, which RFC 5545 defines as
/// the local time of whoever reads it, and an all-day entry states a date whose day opens at a different instant in
/// every zone. The last two are read in the zone the caller stated, and every resolution runs through
/// <see cref="ZonedInstant" /> so an entry falling in a daylight-saving gap or repetition is decided by the one rule
/// this repository already has for that rather than by whichever arithmetic happens here.
/// </para>
/// <para>
/// <b>A zone the file names and this host does not carry is a skip rather than a guess.</b> Reading such an entry in
/// the coordinated zone would put it on a calendar at an hour nobody wrote, which is worse than not importing it and
/// saying so.
/// </para>
/// </remarks>
internal sealed class CalendarFileReader : ICalendarFileReader
{
    /// <inheritdoc />
    public CalendarFileReading Read(Stream file, TimeZoneInfo zoneForUnzonedTimes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(zoneForUnzonedTimes);

        // Decoded once and kept, because the entries are read against the text as well as against the parse: the
        // library invents an identifier for an entry that stated none, and whether the file itself carries the one an
        // entry now reports is the only thing that tells the two apart. It is bounded above by the caller.
        var text = Decoded(file);

        if (Parsed(text) is not { } calendars || calendars.Count == 0)
        {
            return CalendarFileReading.NotCalendarData;
        }

        var sources = calendars.SelectMany(calendar => calendar.Events).ToArray();

        if (sources.Length > CalendarFileImport.MaximumEntryCount)
        {
            return CalendarFileReading.TooManyEntries;
        }

        var entries = new List<CalendarFileEntry>(sources.Length);
        var skipped = new List<CalendarImportSkipReason>();

        foreach (var source in sources)
        {
            if (EntryOf(source, text, zoneForUnzonedTimes, out var reason) is { } entry)
            {
                entries.Add(entry);
            }
            else
            {
                skipped.Add(reason);
            }
        }

        return CalendarFileReading.Read(entries, skipped);
    }

    /// <summary>Parses the octets, or reports that they are not iCalendar this reader can finish.</summary>
    /// <remarks>
    /// The catch is deliberately wide, for the reason
    /// <see cref="BoundedAttachmentTextExtractor" />'s own reading of a hostile document gives: a parser handed
    /// adversarial input raises whatever its reading of that input produces — a malformed property value, a recurrence
    /// rule with no frequency, a number the format cannot hold, an index out of a range it derived — and enumerating
    /// those is a list that goes stale the first time the library is updated, while everything it misses becomes a
    /// failed request rather than the refusal the port promises. What must never be swallowed is the short, stable list
    /// instead: cancellation belongs to whoever asked for it, and a process out of memory is not a fact about one file.
    /// </remarks>
    private static CalendarCollection? Parsed(string text)
    {
        try
        {
            return CalendarCollection.Load(new StringReader(text));
        }
        catch (Exception unreadable) when (unreadable is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Decodes the offered octets as the text the format is written in.</summary>
    /// <remarks>UTF-8 is the encoding RFC 5545 states, and a byte-order mark is detected because exporters write one.</remarks>
    private static string Decoded(Stream file)
    {
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    /// <summary>Reads one entry into the event it would become, or reports why it becomes none.</summary>
    private static CalendarFileEntry? EntryOf(
        Ical.Net.CalendarComponents.CalendarEvent source,
        string text,
        TimeZoneInfo zoneForUnzonedTimes,
        out CalendarImportSkipReason reason)
    {
        // Recurrence is refused in all three of the shapes a file states it in: a rule, an explicit list of further
        // dates, and an entry that is one occurrence of a series somebody edited. This deployment models single events,
        // so expanding any of them would write events the file does not contain and that nothing could later tell
        // apart from events somebody typed.
        if (source.RecurrenceRule is not null || source.RecurrenceIdentifier is not null || NamesFurtherDates(source))
        {
            reason = CalendarImportSkipReason.Recurring;

            return null;
        }

        if (source.DtStart is not { } statedStart)
        {
            reason = CalendarImportSkipReason.NoStart;

            return null;
        }

        if (!ImportedCalendarEventUid.TryCreate(StatedUid(source, text), out var uid))
        {
            reason = CalendarImportSkipReason.UnreadableIdentifier;

            return null;
        }

        if (!CalendarEventTitle.TryCreate(source.Summary, out var title))
        {
            reason = CalendarImportSkipReason.UnreadableTitle;

            return null;
        }

        // Everything below is arithmetic over values the file chose, and arithmetic is where such a value stops being
        // a parsing question: a start near the end of the representable range leaves it the moment anything is added,
        // and a duration may be stated in weeks enough not to be a span at all. Neither is a fault in this deployment
        // and neither is something the reading in front of it could have refused, so both end as this entry's own
        // skip rather than as a request that failed.
        try
        {
            if (!TryResolve(statedStart, zoneForUnzonedTimes, out var start))
            {
                reason = CalendarImportSkipReason.UnknownTimeZone;

                return null;
            }

            DateTimeOffset? end = null;

            if (source.DtEnd is { } statedEnd)
            {
                if (!TryResolve(statedEnd, zoneForUnzonedTimes, out var resolvedEnd))
                {
                    reason = CalendarImportSkipReason.UnknownTimeZone;

                    return null;
                }

                end = resolvedEnd;
            }
            else if (source.Duration is { } duration)
            {
                // A duration is calendar-aware rather than a fixed span — a day across a daylight-saving transition is
                // not 24 hours — so it is measured from the entry's own start by the library that read it.
                end = start + duration.ToTimeSpan(statedStart);
            }

            if (end is { } named && named <= start)
            {
                reason = CalendarImportSkipReason.EndNotAfterStart;

                return null;
            }

            reason = default;

            return new CalendarFileEntry(uid, title, start, end, !statedStart.HasTime);
        }
        catch (Exception unrepresentable)
            when (unrepresentable is ArgumentOutOfRangeException or OverflowException)
        {
            reason = CalendarImportSkipReason.DateOutOfRange;

            return null;
        }
    }

    /// <summary>Reads the identifier the entry itself stated, rather than the one the library invents for an entry naming none.</summary>
    /// <remarks>
    /// <para>
    /// Ical.Net gives every entry a fresh identifier in its own constructor and lets the file replace it, which is
    /// right for a calendar being composed and wrong for one being read: an entry that stated none would be imported
    /// under a different identifier on every reading, so importing one file twice would write its events twice — which
    /// is exactly what the identifier exists to prevent. RFC 5545 requires a <c>UID</c> of every entry, so an entry
    /// without one is skipped instead.
    /// </para>
    /// <para>
    /// The parsed entry cannot tell the two apart, because the property is present either way. The file can: an
    /// invented identifier is always a bare UUID in the hyphenated form and is never written anywhere in the file,
    /// while a stated one is in the file by definition. So anything that is not that form was stated, and anything
    /// that is was stated only if the octets carry it.
    /// </para>
    /// </remarks>
    private static string? StatedUid(Ical.Net.CalendarComponents.CalendarEvent source, string text) =>
        !Guid.TryParseExact(source.Uid, "D", out _) || text.Contains(source.Uid, StringComparison.Ordinal)
            ? source.Uid
            : null;

    /// <summary>Reports whether an entry names further dates of its own beside the one it starts at.</summary>
    private static bool NamesFurtherDates(Ical.Net.CalendarComponents.CalendarEvent source) =>
        source.RecurrenceDates.GetAllDates().Any() || source.RecurrenceDates.GetAllPeriods().Any();

    /// <summary>Resolves one stated date to the instant it names, or reports that its zone is unknown here.</summary>
    private static bool TryResolve(CalDateTime stated, TimeZoneInfo zoneForUnzonedTimes, out DateTimeOffset instant)
    {
        // A date rather than a time: the day opens at midnight, and which midnight is the caller's zone rather than
        // the file's, because the file states none and the server's own would be the one answer nobody asked for.
        if (!stated.HasTime)
        {
            instant = ZonedInstant.Resolve(stated.Date.ToDateTime(TimeOnly.MinValue), zoneForUnzonedTimes).Instant;

            return true;
        }

        if (stated.IsUtc)
        {
            instant = new DateTimeOffset(DateTime.SpecifyKind(stated.Value, DateTimeKind.Utc));

            return true;
        }

        if (string.IsNullOrWhiteSpace(stated.TzId))
        {
            instant = ZonedInstant.Resolve(stated.Value, zoneForUnzonedTimes).Instant;

            return true;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(stated.TzId, out var named))
        {
            instant = default;

            return false;
        }

        instant = ZonedInstant.Resolve(stated.Value, named).Instant;

        return true;
    }
}
