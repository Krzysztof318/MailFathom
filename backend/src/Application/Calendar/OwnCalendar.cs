// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Calendar;

/// <summary>Reads and writes the signed-in person's own calendar.</summary>
/// <remarks>
/// <para>
/// Whose calendar this is comes from the principal rather than from the request, exactly as it does for the user
/// record, the client preferences, and the notification centre: there is no argument here for another person's
/// identifier, so a reading of somebody else's days is something a caller cannot express rather than something a
/// surface has to refuse. An event named by identifier is addressed with the owner beside it, so one another person
/// holds answers as one that does not exist.
/// </para>
/// <para>
/// Every act here is <see cref="MailFathomPermission.MailRead" />, the four writes included, on the reasoning the
/// personal task list is published under rather than the contact book's: an event is this deployment's own record of
/// when one person is committed, nothing here reaches a mail server, and the message an event cites is a value it
/// carries rather than mail these acts read.
/// </para>
/// <para>
/// A refused write is a result rather than an exception, because each refusal is something a person corrects and
/// continues from. None of them carries the title or the times: <see cref="CalendarEventId" /> is what an answer names,
/// and a refusal names not even that.
/// </para>
/// <para>
/// Each write stages through a fresh session per attempt and commits it, which is what the store's three writes are
/// written for. An amendment and an acceptance read the held event inside the attempt rather than before it, so a
/// replay after a lost race decides again from the calendar as it then stands instead of writing back what it read
/// before.
/// </para>
/// </remarks>
public sealed class OwnCalendar
{
    private readonly AccessAuthorization authorization;
    private readonly ICalendarEventStore store;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the person it acts for.</param>
    /// <param name="store">Holds the calendars.</param>
    /// <param name="concurrencyRetryPolicy">Commits a write, deciding again from a fresh read when another write won.</param>
    /// <param name="timeProvider">Supplies the instant an event is identified, recorded, and amended by.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnCalendar(
        AccessAuthorization authorization,
        ICalendarEventStore store,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.store = store;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads one window of the signed-in person's calendar, earliest first.</summary>
    /// <param name="from">The instant the window opens.</param>
    /// <param name="until">The instant it closes.</param>
    /// <param name="origin">The half of the calendar to read, or <see langword="null" /> for both.</param>
    /// <param name="count">How many events it may answer with, or <see langword="null" /> for <see cref="CalendarEventQuery.DefaultCount" />.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The events, or <see langword="null" /> where what was asked for is not a window this deployment answers.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// The window is refused rather than narrowed to what this deployment would serve, which is where this reading
    /// differs from the notification centre's clamped page: a caller asking for a year of an agenda is asking a
    /// question about a span it drew, and answering a shorter one would be a month silently missing from a screen that
    /// believes it drew the year.
    /// </remarks>
    public async Task<IReadOnlyList<CalendarEvent>?> ReadWindowAsync(
        DateTimeOffset from,
        DateTimeOffset until,
        CalendarEventOrigin? origin,
        int? count,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        // Asked before the window is composed rather than caught afterwards: what a caller states is a request this
        // surface reports on, and the query type raises for a programming error instead.
        if (until <= from
            || count is < 1 or > CalendarEventQuery.MaximumCount
            || (origin is { } narrowed && !Enum.IsDefined(narrowed)))
        {
            return null;
        }

        var query = CalendarEventQuery.Create(from, until, origin, count);

        return await this.store.ReadRangeAsync(this.authorization.RequireUser(), query, cancellationToken);
    }

    /// <summary>Reads one event of the signed-in person's calendar.</summary>
    /// <param name="eventId">The event to read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The event, or <see langword="null" /> where that calendar holds no such event.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<CalendarEvent?> FindAsync(CalendarEventId eventId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.ReadAsync(this.authorization.RequireUser(), eventId, cancellationToken);
    }

    /// <summary>Puts an event the person states on their own calendar.</summary>
    /// <param name="title">What the event is called, as supplied.</param>
    /// <param name="start">When it begins.</param>
    /// <param name="end">When it ends, or <see langword="null" /> to state no end.</param>
    /// <param name="sourceMessage">The message it was created from, or <see langword="null" /> where none was open.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The event as the calendar holds it, or why it was refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// What is written is asserted by construction, because a person typing into their own client is putting an event
    /// on their calendar. Nothing here creates a proposal: those are read out of mail, and an event created from an
    /// open thread cites the message while being every bit as much the person's own.
    /// </remarks>
    public async Task<CalendarEventWriteResult> CreateAsync(
        string? title,
        DateTimeOffset start,
        DateTimeOffset? end,
        StoredEmailId? sourceMessage,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = this.authorization.RequireUser();

        if (Refusal(title, start, end) is { } refused)
        {
            return refused;
        }

        var recordedAt = this.timeProvider.GetUtcNow();
        var created = CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7(recordedAt)),
            CalendarEventTitle.Create(title),
            start,
            end,
            CalendarEventOrigin.Asserted,
            sourceMessage,
            importedUid: null,
            recordedAt,
            recordedAt);

        await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) =>
                this.store.AddAsync(session, owner, created, attemptCancellationToken),
            cancellationToken);

        return CalendarEventWriteResult.Written(created);
    }

    /// <summary>Amends one event of the signed-in person's calendar to the record they state.</summary>
    /// <param name="eventId">The event to amend.</param>
    /// <param name="title">What it is called afterwards, as supplied.</param>
    /// <param name="start">When it begins afterwards.</param>
    /// <param name="end">When it ends afterwards, or <see langword="null" /> to hold no end.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The event as the calendar holds it, or why it was refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// The whole record rather than the difference from the one held, which is what keeps retitling an event, moving
    /// it, and dropping its end one operation. What it never changes is the identity, the origin, the message the event
    /// cites, and the identifier it was imported under — so this is not how a proposal is accepted.
    /// </remarks>
    public async Task<CalendarEventWriteResult> AmendAsync(
        CalendarEventId eventId,
        string? title,
        DateTimeOffset start,
        DateTimeOffset? end,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = this.authorization.RequireUser();

        if (Refusal(title, start, end) is { } refused)
        {
            return refused;
        }

        var amendedTitle = CalendarEventTitle.Create(title);

        return await this.concurrencyRetryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.store.ReadAsync(owner, eventId, attemptCancellationToken) is not { } held)
                {
                    return CalendarEventWriteResult.NotFound;
                }

                var amended = held.AmendedWith(amendedTitle, start, end, this.timeProvider.GetUtcNow());

                return await this.store.ReplaceAsync(session, owner, amended, attemptCancellationToken)
                    ? CalendarEventWriteResult.Written(amended)
                    : CalendarEventWriteResult.NotFound;
            },
            cancellationToken);
    }

    /// <summary>Takes a date the person's mail proposed onto their calendar, under the identity it already has.</summary>
    /// <param name="eventId">The proposal to accept.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The event as the calendar now holds it, or why it was refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Accepting an event already on the calendar is refused rather than answered as done, which is the domain's
    /// decision: the second acceptance is a caller acting on a proposal that was already taken, and reporting it as
    /// performed would move the record of when the event actually reached the calendar.
    /// </remarks>
    public async Task<CalendarEventWriteResult> AcceptAsync(
        CalendarEventId eventId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = this.authorization.RequireUser();

        return await this.concurrencyRetryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.store.ReadAsync(owner, eventId, attemptCancellationToken) is not { } held)
                {
                    return CalendarEventWriteResult.NotFound;
                }

                if (held.Origin != CalendarEventOrigin.Proposed)
                {
                    return CalendarEventWriteResult.AlreadyOnTheCalendar;
                }

                var accepted = held.Accepted(this.timeProvider.GetUtcNow());

                return await this.store.ReplaceAsync(session, owner, accepted, attemptCancellationToken)
                    ? CalendarEventWriteResult.Written(accepted)
                    : CalendarEventWriteResult.NotFound;
            },
            cancellationToken);
    }

    /// <summary>Takes one event off the signed-in person's calendar for good.</summary>
    /// <param name="eventId">The event to delete.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the calendar held the event and it is gone; <see langword="false" /> when it held none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Dismissing a proposal is this operation and not one of its own: a date nobody wanted is not a fact worth
    /// keeping, so what a person dismisses and what they delete leave the calendar the same way.
    /// </remarks>
    public async Task<bool> DeleteAsync(CalendarEventId eventId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = this.authorization.RequireUser();

        return await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) =>
                this.store.DeleteAsync(session, owner, eventId, attemptCancellationToken),
            cancellationToken);
    }

    /// <summary>Reports which rule the record a caller stated breaks, or that it breaks none.</summary>
    /// <remarks>
    /// Asked before the event is composed, so the invariants the domain raises on are never reached by a request: what
    /// a person typed is something this surface reports on, and an exception from the record's own constructor would be
    /// a fault in this deployment rather than an answer to them.
    /// </remarks>
    private static CalendarEventWriteResult? Refusal(string? title, DateTimeOffset start, DateTimeOffset? end)
    {
        if (!CalendarEventTitle.TryCreate(title, out _))
        {
            return CalendarEventWriteResult.TitleRefused;
        }

        return end is { } stated && stated <= start ? CalendarEventWriteResult.EndNotAfterStart : null;
    }
}
