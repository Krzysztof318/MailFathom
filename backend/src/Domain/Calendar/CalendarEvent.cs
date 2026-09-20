// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Calendar;

/// <summary>Holds one event this deployment knows about: what it is, when it is, and how it came to be here.</summary>
/// <remarks>
/// <para>
/// A MailFathom event is native to this deployment and to nothing else. No calendar protocol is synchronized against,
/// so nothing here is a local copy of a row some server owns: what is written is what a person typed, what they
/// imported from a file they chose, or what a reading of their mail proposed to them.
/// </para>
/// <para>
/// <see cref="Origin" /> is what separates the calendar from what is merely offered to it, and accepting a proposal
/// changes that value on this event rather than producing a second one. That is why the type is an entity:
/// <see cref="Id" /> is what makes two records the same event, and every method here answers with a new instance
/// carrying that identity.
/// </para>
/// <para>
/// <see cref="SourceMessage" /> is a pointer rather than a copy. An event that came out of a message keeps the
/// message's identity so the thread can be opened from it, and nothing about the message — not its subject, not its
/// participants, not a line of its body — is written here.
/// </para>
/// <para>
/// The title, the times, and the message an event cites are personal data. They are never logged, never a metric
/// dimension, and never written into a failure message; <see cref="Id" /> is what a failure names.
/// </para>
/// </remarks>
public sealed class CalendarEvent
{
    /// <summary>The most reminders one event may carry.</summary>
    /// <remarks>
    /// Above every preset the client offers together, so nothing a person can press reaches it, and bounded at all
    /// because each reminder is a row a run reads and a notification it may write: an event carrying a thousand of
    /// them would be one person's way of filling somebody's notification centre.
    /// </remarks>
    public const int MaximumReminderCount = 16;

    /// <summary>The hour an all-day event's reminders are measured back from, in the offset the event carries.</summary>
    public const int AllDayReminderHour = 9;

    private CalendarEvent(
        CalendarEventId id,
        CalendarEventTitle title,
        DateTimeOffset start,
        DateTimeOffset? end,
        bool isAllDay,
        IReadOnlyList<CalendarReminder> reminders,
        CalendarEventOrigin origin,
        StoredEmailId? sourceMessage,
        ImportedCalendarEventUid? importedUid,
        DateTimeOffset recordedAt,
        DateTimeOffset amendedAt)
    {
        this.Id = id;
        this.Title = title;
        this.Start = start;
        this.End = end;
        this.IsAllDay = isAllDay;
        this.Reminders = reminders;
        this.Origin = origin;
        this.SourceMessage = sourceMessage;
        this.ImportedUid = importedUid;
        this.RecordedAt = recordedAt;
        this.AmendedAt = amendedAt;
    }

    /// <summary>Gets what identifies this event, which neither an amendment nor an acceptance changes.</summary>
    public CalendarEventId Id { get; }

    /// <summary>Gets what this event is called.</summary>
    public CalendarEventTitle Title { get; }

    /// <summary>Gets when the event begins.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Gets when the event ends, or <see langword="null" /> when nothing said how long it lasts.</summary>
    /// <remarks>
    /// An end and a duration are one fact stated two ways, so only one of them is held and the other is derived through
    /// <see cref="Duration" />. Which of the two a writer supplies is that writer's affair: an <c>.ics</c> entry names
    /// either, and a person typing one names whichever the screen asked for.
    /// </remarks>
    public DateTimeOffset? End { get; }

    /// <summary>Gets how long the event lasts, or <see langword="null" /> when it carries no end.</summary>
    public TimeSpan? Duration => this.End - this.Start;

    /// <summary>Gets whether the event is stated as a day rather than as a clock time.</summary>
    /// <remarks>
    /// It changes nothing about when the event is: <see cref="Start" /> is still the instant the day opens at. What it
    /// decides is what a reminder is measured from, because a person who wrote down a day never chose midnight —
    /// <see cref="AnchorsRemindersAt" /> is where that reading lives.
    /// </remarks>
    public bool IsAllDay { get; }

    /// <summary>Gets the leads this event is to be announced at, longest first, which is empty where nothing announces it.</summary>
    /// <remarks>
    /// An empty set is a statement rather than an omission: nothing will be raised about this event. The order is the
    /// order the set is read back in and is the calendar's rather than the writer's, so two writers stating the same
    /// leads produce the same event.
    /// </remarks>
    public IReadOnlyList<CalendarReminder> Reminders { get; }

    /// <summary>Gets the instant every reminder on this event is measured back from.</summary>
    /// <remarks>
    /// <see cref="Start" /> for an event that names a clock time, and <see cref="AllDayReminderHour" /> o'clock on the
    /// day it falls for one that does not. Measuring an all-day event from midnight would announce a day at the
    /// instant it begins, which is the middle of the night before anybody is awake to be told about it; the design
    /// project settles the hour, and the day is read in the offset the event itself carries rather than in any
    /// timezone this deployment would have to be told about.
    /// </remarks>
    public DateTimeOffset AnchorsRemindersAt => this.IsAllDay
        ? new DateTimeOffset(this.Start.Date.AddHours(AllDayReminderHour), this.Start.Offset)
        : this.Start;

    /// <summary>Gets whether this event is on the calendar or offered to it.</summary>
    public CalendarEventOrigin Origin { get; }

    /// <summary>Gets the message this event came out of, or <see langword="null" /> when no message named it.</summary>
    /// <remarks>
    /// Present on a proposal read out of mail, and on an event a person created from an open thread. Accepting a
    /// proposal keeps it, because where a date came from stays true once somebody agrees to it.
    /// </remarks>
    public StoredEmailId? SourceMessage { get; }

    /// <summary>Gets the identifier the file this event was imported from named it by, or <see langword="null" /> when none did.</summary>
    /// <remarks>
    /// Kept so that importing one file twice recognizes what it already created rather than writing a second copy of
    /// it. Nothing in this deployment reads it otherwise, and an event nobody imported carries none.
    /// </remarks>
    public ImportedCalendarEventUid? ImportedUid { get; }

    /// <summary>Gets when this event was first written here.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Gets when it was last amended or accepted, which equals <see cref="RecordedAt" /> until one happens.</summary>
    public DateTimeOffset AmendedAt { get; }

    /// <summary>Builds an event from what a writer supplied, enforcing every invariant the calendar rests on.</summary>
    /// <param name="id">The identity this event keeps for as long as it is held.</param>
    /// <param name="title">What the event is called.</param>
    /// <param name="start">When it begins.</param>
    /// <param name="end">When it ends, or <see langword="null" /> when nothing said how long it lasts.</param>
    /// <param name="isAllDay">Whether the event is stated as a day rather than as a clock time.</param>
    /// <param name="reminders">The leads it is to be announced at, in any order and without repetition, or empty to announce nothing.</param>
    /// <param name="origin">Whether it is on the calendar or offered to it.</param>
    /// <param name="sourceMessage">The message it came out of, or <see langword="null" /> when no message named it.</param>
    /// <param name="importedUid">The identifier the file it came from named it by, or <see langword="null" /> when none did.</param>
    /// <param name="recordedAt">When it was written here.</param>
    /// <param name="amendedAt">When it was last amended, which is <paramref name="recordedAt" /> for a new event.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reminders" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> names no declared value, when <paramref name="end" /> is not after <paramref name="start" />, or when more than <see cref="MaximumReminderCount" /> reminders are supplied.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id" />, <paramref name="title" />, or a supplied <paramref name="importedUid" /> is the default of its type, when a proposal is supplied with <paramref name="importedUid" />, or when one lead is supplied twice.</exception>
    /// <remarks>
    /// An end at the same instant as the start is refused rather than kept as an event of no length: an entry naming
    /// both is naming a span, and one whose span is empty is a file or a reading that went wrong rather than something
    /// to draw. An event that genuinely has no length states no end at all.
    /// </remarks>
    public static CalendarEvent Create(
        CalendarEventId id,
        CalendarEventTitle title,
        DateTimeOffset start,
        DateTimeOffset? end,
        bool isAllDay,
        IReadOnlyCollection<CalendarReminder> reminders,
        CalendarEventOrigin origin,
        StoredEmailId? sourceMessage,
        ImportedCalendarEventUid? importedUid,
        DateTimeOffset recordedAt,
        DateTimeOffset amendedAt)
    {
        ArgumentNullException.ThrowIfNull(reminders);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "A calendar event origin must name a declared value.");
        }

        // Each of the three is a struct whose private constructor a caller cannot reach, so the one value that skips
        // its validation is the default — which would reach the calendar as an event nothing identifies or as a row
        // with no title in a column that requires one.
        if (!id.IsSpecified)
        {
            throw new ArgumentException("A calendar event is identified by a specified identifier.", nameof(id));
        }

        if (!title.IsSpecified)
        {
            throw new ArgumentException("A calendar event carries a title.", nameof(title));
        }

        if (importedUid is { IsSpecified: false })
        {
            throw new ArgumentException(
                "An event imported under an identifier carries the identifier itself.",
                nameof(importedUid));
        }

        RequireEndAfterStart(start, end);

        // An import writes what a person confirmed, so it is asserted by construction; a proposal is read out of mail
        // and no file named it. A proposal carrying an imported identifier would therefore be a record two writers
        // produced together, and the skip that recognizes an already imported entry would answer for something nobody
        // imported.
        if (origin == CalendarEventOrigin.Proposed && importedUid is not null)
        {
            throw new ArgumentException(
                "A proposed event carries no imported identifier, because nothing imports a proposal.",
                nameof(importedUid));
        }

        return new CalendarEvent(
            id,
            title,
            start,
            end,
            isAllDay,
            Ordered(reminders),
            origin,
            sourceMessage,
            importedUid,
            recordedAt,
            amendedAt);
    }

    /// <summary>Produces this event with what an amendment replaced, keeping its identity, its origin, and where it came from.</summary>
    /// <param name="title">What the event is called after the amendment.</param>
    /// <param name="start">When it begins after the amendment.</param>
    /// <param name="end">When it ends after the amendment, or <see langword="null" /> to hold no end.</param>
    /// <param name="isAllDay">Whether it is stated as a day rather than as a clock time afterwards.</param>
    /// <param name="reminders">The leads it is to be announced at afterwards, or empty to announce nothing.</param>
    /// <param name="amendedAt">When the amendment happened.</param>
    /// <returns>The amended event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reminders" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="end" /> is not after <paramref name="start" />, or when more than <see cref="MaximumReminderCount" /> reminders are supplied.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is the default of its type, or when one lead is supplied twice.</exception>
    /// <remarks>
    /// An amendment states the event as it is to stand rather than the difference from the one held, which is what
    /// keeps retitling it, moving it, dropping its end, and changing what announces it one operation instead of four
    /// that each pass through a shape the invariants above refuse. The reminders are part of that record for the same
    /// reason: a person turning the last one off is stating an event that announces nothing rather than omitting a
    /// field.
    /// </remarks>
    public CalendarEvent AmendedWith(
        CalendarEventTitle title,
        DateTimeOffset start,
        DateTimeOffset? end,
        bool isAllDay,
        IReadOnlyCollection<CalendarReminder> reminders,
        DateTimeOffset amendedAt) =>
        Create(
            this.Id,
            title,
            start,
            end,
            isAllDay,
            reminders,
            this.Origin,
            this.SourceMessage,
            this.ImportedUid,
            this.RecordedAt,
            amendedAt);

    /// <summary>Produces this proposal as an event on the calendar, under the identity it already had.</summary>
    /// <param name="acceptedAt">When it was accepted.</param>
    /// <returns>The same event, asserted.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this event is already asserted.</exception>
    /// <remarks>
    /// Accepting one twice is refused rather than treated as a repeat, because the second acceptance is a caller acting
    /// on a proposal somebody else already took: answering it as done would tell them the act they performed is what
    /// put the event on the calendar, and the time they are recording would move the record of when it actually was.
    /// </remarks>
    public CalendarEvent Accepted(DateTimeOffset acceptedAt)
    {
        if (this.Origin != CalendarEventOrigin.Proposed)
        {
            throw new InvalidOperationException("Only a proposed event is accepted, and this one is already on the calendar.");
        }

        return new CalendarEvent(
            this.Id,
            this.Title,
            this.Start,
            this.End,
            this.IsAllDay,
            this.Reminders,
            CalendarEventOrigin.Asserted,
            this.SourceMessage,
            this.ImportedUid,
            this.RecordedAt,
            acceptedAt);
    }

    /// <summary>Reports the instant one of this event's reminders falls at.</summary>
    /// <param name="reminder">The lead to measure back from this event's anchor.</param>
    /// <returns>The instant the reminder comes due.</returns>
    /// <remarks>
    /// Derived rather than stored on the reminder, which is what makes a moved event reminded at its new time without
    /// anything rewriting what the person chose.
    /// </remarks>
    public DateTimeOffset RemindsAt(CalendarReminder reminder) => this.AnchorsRemindersAt - reminder.Lead;

    /// <summary>Puts a stated set of reminders into the order the calendar keeps them in, refusing a set it will not hold.</summary>
    /// <remarks>
    /// Longest lead first, because that is the order a person reads their own reminders in — the earliest warning is
    /// the one furthest from the event. The order is the calendar's rather than the writer's so that two writers
    /// stating the same leads produce the same event, and a repeated lead is refused rather than folded away: it is a
    /// caller stating one reminder twice, and answering as though it had asked for one would hide the mistake.
    /// </remarks>
    private static CalendarReminder[] Ordered(IReadOnlyCollection<CalendarReminder> reminders)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reminders.Count, MaximumReminderCount, nameof(reminders));

        var ordered = reminders.OrderByDescending(reminder => reminder.MinutesBefore).ToArray();

        if (ordered.Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("An event states each reminder lead once.", nameof(reminders));
        }

        return ordered;
    }

    private static void RequireEndAfterStart(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end is { } named && named <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), named, "An event that states an end ends after it begins.");
        }
    }
}
