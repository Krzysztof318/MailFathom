// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar.Import;

/// <summary>Puts the events of an iCalendar file the signed-in person chose onto their own calendar.</summary>
/// <remarks>
/// <para>
/// <b>Reading a file is not synchronizing against a calendar server.</b> Nothing is subscribed to, nothing is polled,
/// and nothing is written back: the octets are read once, the events they name are written, and the file is then no
/// part of this deployment's record. What the events keep of it is the identifier each entry named itself by, and that
/// is kept for exactly one question — has this been imported here already.
/// </para>
/// <para>
/// <b>A person sees the count before anything is written.</b> <see cref="SummariseAsync" /> and
/// <see cref="ImportAsync" /> read the same file the same way and answer in the same terms; the first writes
/// nothing, and the second is what the person's confirmation performs. That is the acceptance an imported event
/// rests on rather than a courtesy, because a file somebody else prepared is exactly the case where what is about to
/// be created is not obvious from having chosen it. The two counts differ only where the calendar moved between
/// them — the same file imported elsewhere in the meantime — and then the confirmation's own count is the true one,
/// because it is taken in the attempt that committed.
/// </para>
/// <para>
/// <b>The confirmation carries the file again rather than a token.</b> Nothing is staged between the two acts, so
/// there is no half-import to expire, to clean up, or to leave an entry of somebody's day sitting in this deployment
/// while they decide. The cost is one more upload of a file already bounded at
/// <see cref="MaximumFileBytes" />, which is the cheaper half of that trade by a wide margin.
/// </para>
/// <para>
/// <b>An import is one transaction.</b> Either the calendar holds every event the confirmed summary named or it holds
/// none of them, so a file that fails part way through leaves nothing behind for a person to find and undo by hand.
/// </para>
/// </remarks>
public sealed class CalendarFileImport
{
    /// <summary>The largest file an import reads.</summary>
    /// <remarks>
    /// One megabyte, which is several thousand entries of ordinary iCalendar and far past any schedule a person is
    /// handed. What it guards is that an import is not a way to make an operator's deployment parse arbitrary octets:
    /// the reader takes the whole file in memory, and this is the bound that makes that safe.
    /// </remarks>
    public const int MaximumFileBytes = 1024 * 1024;

    /// <summary>The most entries one import writes.</summary>
    /// <remarks>
    /// Stated beside the byte bound rather than derived from it, because the two refuse different things: a small file
    /// of minimal entries reaches this count long before the megabyte, and a person confirming a summary should be
    /// deciding about a schedule rather than about a bulk load. A file naming more is refused whole rather than
    /// truncated, so nothing is written that the summary did not name.
    /// </remarks>
    public const int MaximumEntryCount = 500;

    private readonly AccessAuthorization authorization;
    private readonly ICalendarFileReader reader;
    private readonly ICalendarEventStore store;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the person it acts for.</param>
    /// <param name="reader">Reads the entries of an offered file.</param>
    /// <param name="store">Holds the calendars.</param>
    /// <param name="concurrencyRetryPolicy">Commits the write, deciding again from a fresh read when another write won.</param>
    /// <param name="timeProvider">Supplies the instant an imported event is identified and recorded by.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public CalendarFileImport(
        AccessAuthorization authorization,
        ICalendarFileReader reader,
        ICalendarEventStore store,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.reader = reader;
        this.store = store;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports what a file would put on the acting person's calendar, writing nothing.</summary>
    /// <param name="file">The octets the person chose.</param>
    /// <param name="zoneId">The zone an entry naming none of its own is read in, or <see langword="null" /> for the coordinated one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the file would create, what it would skip and why, or why the whole file was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="file" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public async Task<CalendarImportSummary> SummariseAsync(
        Stream file,
        string? zoneId,
        CancellationToken cancellationToken)
    {
        var (outcome, offered, skipped) = this.Offered(file, zoneId);

        if (outcome is not CalendarImportOutcome.Read)
        {
            return CalendarImportSummary.Refused(outcome);
        }

        var (summary, _) = await this.ResolveAsync(
            this.authorization.RequireUser(),
            offered,
            skipped,
            cancellationToken);

        return summary;
    }

    /// <summary>Puts the events of a file the acting person confirmed onto their own calendar.</summary>
    /// <param name="file">The octets the person chose, which are read again rather than recalled from the summary.</param>
    /// <param name="zoneId">The zone an entry naming none of its own is read in, or <see langword="null" /> for the coordinated one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the file created, what it skipped and why, or why the whole file was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="file" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// What is written is asserted, because choosing a file and confirming what it holds is a person putting those
    /// events on their calendar. Nothing an import produces is a proposal: a proposal is a date something read out of
    /// mail that nobody has agreed to, and this person has agreed to all of these.
    /// </remarks>
    public async Task<CalendarImportSummary> ImportAsync(
        Stream file,
        string? zoneId,
        CancellationToken cancellationToken)
    {
        var (outcome, offered, skipped) = this.Offered(file, zoneId);

        if (outcome is not CalendarImportOutcome.Read)
        {
            return CalendarImportSummary.Refused(outcome);
        }

        var owner = this.authorization.RequireUser();

        if (offered.Count == 0)
        {
            var (nothingToWrite, _) = await this.ResolveAsync(owner, offered, skipped, cancellationToken);

            return nothingToWrite;
        }

        return await this.concurrencyRetryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                // Which of these the calendar already holds is decided inside the attempt rather than once above it,
                // because that is the whole of what a second attempt has to decide again. Two imports of one file
                // both read a calendar holding neither, so the loser meets the unique index — and an attempt
                // replaying the rows the first read composed would meet it again on every attempt until they ran out,
                // which is a failed request rather than the convergence the index exists to produce. Reading here,
                // the winner's entries are counted as already held and this attempt writes only what is still
                // missing.
                var (summary, events) = await this.ResolveAsync(
                    owner,
                    offered,
                    skipped,
                    attemptCancellationToken);

                foreach (var written in events)
                {
                    await this.store.AddAsync(session, owner, written, attemptCancellationToken);
                }

                return summary;
            },
            cancellationToken);
    }

    /// <summary>Reads the file into the entries it offers, or into why the whole of it is refused.</summary>
    /// <remarks>
    /// The one reading both acts share, which is what makes a confirmation act on what was shown. It reaches nothing
    /// this deployment holds, so it answers the same way however often it is asked; what the calendar already holds is
    /// <see cref="ResolveAsync" />'s question, and it is a separate one because its answer changes underneath a
    /// competing import while this one does not.
    /// </remarks>
    private (CalendarImportOutcome Outcome, IReadOnlyList<CalendarFileEntry> Offered, IReadOnlyList<CalendarImportSkipReason> Skipped) Offered(
        Stream file,
        string? zoneId)
    {
        ArgumentNullException.ThrowIfNull(file);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        if (ZoneOf(zoneId) is not { } zone)
        {
            return (CalendarImportOutcome.UnknownTimeZone, [], []);
        }

        var reading = this.reader.Read(file, zone);

        if (reading.Outcome is not CalendarImportOutcome.Read)
        {
            return (reading.Outcome, [], []);
        }

        var skipped = new List<CalendarImportSkipReason>(reading.Skipped);
        var offered = new List<CalendarFileEntry>(reading.Entries.Count);
        var seen = new HashSet<ImportedCalendarEventUid>();

        foreach (var entry in reading.Entries)
        {
            if (seen.Add(entry.Uid))
            {
                offered.Add(entry);
            }
            else
            {
                skipped.Add(CalendarImportSkipReason.RepeatedInTheFile);
            }
        }

        return (CalendarImportOutcome.Read, offered, skipped);
    }

    /// <summary>Decides which of the offered entries this calendar does not already hold, and what the person is told.</summary>
    /// <remarks>
    /// The events are composed here rather than at the write, so the summary's count and the rows an import stages are
    /// the same list rather than two derivations of one file. It is asked once per attempt, which is what lets an
    /// attempt that lost a race converge instead of replaying rows the winner has already committed.
    /// </remarks>
    private async Task<(CalendarImportSummary Summary, IReadOnlyList<CalendarEvent> Events)> ResolveAsync(
        MailUserId owner,
        IReadOnlyList<CalendarFileEntry> offered,
        IReadOnlyList<CalendarImportSkipReason> skippedByReading,
        CancellationToken cancellationToken)
    {
        // Asked once for the whole file rather than per entry, and only about the identifiers this file names: the
        // question is which of these the calendar already holds, and reading every imported identifier a person has
        // would be a query that grows with their calendar instead of with the file they chose.
        IReadOnlySet<ImportedCalendarEventUid> held = offered.Count == 0
            ? new HashSet<ImportedCalendarEventUid>()
            : await this.store.ReadImportedUidsAsync(
                owner,
                [.. offered.Select(entry => entry.Uid)],
                cancellationToken);

        var skipped = new List<CalendarImportSkipReason>(skippedByReading);
        var recordedAt = this.timeProvider.GetUtcNow();
        var events = new List<CalendarEvent>(offered.Count);

        foreach (var entry in offered)
        {
            if (held.Contains(entry.Uid))
            {
                skipped.Add(CalendarImportSkipReason.AlreadyOnTheCalendar);

                continue;
            }

            events.Add(CalendarEvent.Create(
                CalendarEventId.Create(Guid.CreateVersion7(recordedAt)),
                entry.Title,
                entry.Start,
                entry.End,
                entry.IsAllDay,
                [],
                CalendarEventOrigin.Asserted,
                sourceMessage: null,
                entry.Uid,
                recordedAt,
                recordedAt));
        }

        return (CalendarImportSummary.Of([.. events.Select(written => written.Start)], skipped), events);
    }

    /// <summary>Reads the zone a request named, or the coordinated one where it named none.</summary>
    /// <remarks>
    /// Resolved rather than trusted: the identifier arrives from a client, and a zone this host does not carry is a
    /// refusal of the request instead of a silent fall back to the server's own — which would be the one reading
    /// nobody asked for and the one that moves a whole day.
    /// </remarks>
    private static TimeZoneInfo? ZoneOf(string? zoneId) =>
        string.IsNullOrWhiteSpace(zoneId)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.TryFindSystemTimeZoneById(zoneId.Trim(), out var named) ? named : null;
}
