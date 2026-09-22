// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Locale } from './locale';

// Every instant the client shows, worded in one place. The service hands the client an instant with an offset —
// `2026-08-31T09:41:00+00:00` — and what a reader is owed is that instant placed against *their* day rather than
// against a server's or a sender's. Which day that is comes from the zone their own record states, which every caller
// passes in: it is the same value the deployment anchors a relative period on, so a message the client draws on
// Tuesday is one the deployment would answer a question about as Tuesday. `null` is the runtime's own zone, which is
// what stands until a record has answered and what a deployment holding no record for the reader leaves in force.
//
// That is the rule this module exists to state: a screen that named a zone of its own, or that rendered UTC, would be
// wrong for every reader who is not sitting in it, and the failure is invisible in review because the value still
// looks like a time.
//
// It sits here rather than beside any one screen because three of them word an instant — the message row's time, the
// reading pane's sent and received headers, and anything a later stage adds — and a second copy of the decision is how
// two screens come to disagree about when the same message arrived.
//
// A machine-readable form is never one of these. What a `<time>` element carries in `dateTime` is the instant the
// service sent, unchanged, because that is what anything reading the document works with; these functions answer the
// human spelling of it and nothing else.

/**
 * How much of an instant is said.
 *
 * `stamp` is an instant standing in a row that is scanned rather than read, where the date has to fit beside a sender
 * and a subject. `full` is an instant a reader has stopped on, in a header they opened the message to read. `time` is
 * an instant whose day the surface around it has already said — an event in a calendar column that is one day wide,
 * where repeating the date on every entry would say the same thing as many times as there are entries. `day` is an
 * instant that stands for a whole day, where the clock time it happens to carry would claim an hour nobody chose.
 */
export type InstantDetail = 'stamp' | 'full' | 'time' | 'day';

const details: Readonly<Record<InstantDetail, Intl.DateTimeFormatOptions>> = {
    stamp: { dateStyle: 'short', timeStyle: 'short' },
    full: { dateStyle: 'long', timeStyle: 'short' },
    time: { timeStyle: 'short' },
    day: { dateStyle: 'full' },
};

/**
 * An instant as the reader's own language and the reader's own clock word it, or nothing at all where what the service
 * sent is not an instant this client can read — an absence rather than a value to repair.
 *
 * @param instant What the service sent, as an instant carrying its own offset.
 * @param locale The language it is worded in.
 * @param detail How much of it is said.
 * @param timeZone The zone the reader's record states their days are read in, or `null` for the runtime's own.
 */
export function wordInstant(
    instant: string | null,
    locale: Locale,
    detail: InstantDetail,
    timeZone: string | null,
): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    return Number.isNaN(at) ? null : wordedIn(locale, details[detail], timeZone).format(at);
}

/**
 * Two instants said as the run between them, or the first alone where there is no second.
 *
 * Through `Intl`'s own range formatting rather than by joining two spellings with a dash, for the reason a sentence is
 * one catalogue entry rather than fragments: which dash a language uses, whether it repeats the part the two ends
 * share, and where it puts the whole of it are the locale's answers and not this client's.
 *
 * @param from The instant the run begins at, as the service sent it.
 * @param to The instant it ends at, or `null` where it has no end.
 * @param locale The language it is worded in.
 * @param detail How much of each end is said.
 * @param timeZone The zone the reader's record states their days are read in, or `null` for the runtime's own.
 */
export function wordInstantRange(
    from: string,
    to: string | null,
    locale: Locale,
    detail: InstantDetail,
    timeZone: string | null,
): string | null {
    const begins = Date.parse(from);

    if (Number.isNaN(begins)) {
        return null;
    }

    const format = wordedIn(locale, details[detail], timeZone);

    if (to === null) {
        return format.format(begins);
    }

    const ends = Date.parse(to);

    return Number.isNaN(ends) ? format.format(begins) : format.formatRange(begins, ends);
}

/**
 * An instant as the design project words one on a row of the list, against the reader's own clock and calendar: the
 * time alone for something that arrived today, the language's own word for yesterday, the day and the month for
 * anything earlier this year, and the short date for anything older than that.
 *
 * The three calendar comparisons are made in the reader's own zone rather than against the runtime's getters, so a
 * message that arrived late last night reads as yesterday for the person who received it rather than as today in the
 * zone a browser happens to report. `now` is a parameter rather than read here so that a test pins it beside the zone
 * it pins.
 *
 * @param instant What the service sent, as an instant carrying its own offset.
 * @param locale The language it is worded in.
 * @param now The instant the row is being drawn at.
 * @param timeZone The zone the reader's record states their days are read in, or `null` for the runtime's own.
 */
export function wordRecentInstant(
    instant: string | null,
    locale: Locale,
    now: number,
    timeZone: string | null,
): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    if (Number.isNaN(at)) {
        return null;
    }

    const then = calendarDayIn(at, timeZone);
    const today = calendarDayIn(now, timeZone);

    if (then === today) {
        return wordedIn(locale, { timeStyle: 'short' }, timeZone).format(at);
    }

    if (then === dayBefore(today)) {
        return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(-1, 'day');
    }

    if (then.slice(0, 4) === today.slice(0, 4)) {
        return wordedIn(locale, { day: '2-digit', month: '2-digit' }, timeZone).format(at);
    }

    return wordedIn(locale, { dateStyle: 'short' }, timeZone).format(at);
}

/**
 * The day a task is due, as the design project words one on a row of the list.
 *
 * It is the calendar day of {@link wordCalendarDay} written short: the language's own word for the day the reader is
 * on, the day and the month for anything else this year, and the short date for anything further out. The design
 * writes it beside the title in the space of two words, which a long date does not fit and which is why this is a
 * function of its own rather than a second option on that one — and what carries the meaning a bare date loses is the
 * sentence the row states to a screen reader, not a longer spelling drawn for everybody.
 *
 * Only the reader's own day is worded relatively. The design draws every other day as a number, and a client that
 * also said *tomorrow* would be inventing a wording rather than following one.
 *
 * Which day *today* is comes from the reader's own zone rather than from the runtime's, for the reason the rest of
 * this module reads one that way: the clock states an instant, and the day it falls on is a different day either side
 * of midnight in two zones — so a runtime that is not the reader's would word the day before or the day after as
 * *today* and word their own due date as a number.
 *
 * @param day The calendar day, read as local midnight for {@link wordCalendarDay}'s reason.
 * @param now The reader's own clock, passed rather than read so a test pins it beside the zone it pins.
 * @param timeZone The zone the reader's record states, or `null` for the runtime's own.
 */
export function wordDueDay(day: string, locale: Locale, now: number, timeZone: string | null): string {
    const at = new Date(`${day}T00:00:00`);

    if (Number.isNaN(at.getTime())) {
        return day;
    }

    const today = calendarDayIn(now, timeZone);

    if (day === today) {
        return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(0, 'day');
    }

    // Worded without the zone, as the day itself is read: `at` is already local midnight of the day somebody picked,
    // and placing that instant in another zone is what would draw the day either side of it.
    return day.slice(0, 4) === today.slice(0, 4)
        ? new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit' }).format(at)
        : new Intl.DateTimeFormat(locale, { dateStyle: 'short' }).format(at);
}

/**
 * The calendar day an instant falls on in one zone, as `yyyy-mm-dd`.
 *
 * Read back out of the formatter rather than off a `Date`, because `Date`'s getters answer in the runtime's own zone
 * and there is nothing to tell them another one — which is the whole defect this module was changed to fix.
 *
 * Exported because which day *today* is decides more than a wording: a screen grouping work under *Today* has to
 * agree with the row under it that says *today*, and two readings of one day is how the two come to disagree.
 *
 * @param at The instant to read the day off.
 * @param timeZone The zone the reader's record states, or `null` for the runtime's own.
 */
export function calendarDayIn(at: number, timeZone: string | null): string {
    const parts = wordedIn('en-US', { year: 'numeric', month: '2-digit', day: '2-digit' }, timeZone).formatToParts(at);
    const partOf = (type: Intl.DateTimeFormatPartTypes): string =>
        parts.find((part) => part.type === type)?.value ?? '';

    return `${partOf('year')}-${partOf('month')}-${partOf('day')}`;
}

/** The calendar day before one, which is a walk over the calendar and never over an instant. */
function dayBefore(day: string): string {
    return dayFrom(day, -1);
}

/**
 * The calendar day a number of days from one, walked over the calendar and never over an instant.
 *
 * Walking the calendar is what keeps a week that crosses a daylight-saving change six days rather than six days less
 * an hour, and it is why nothing here adds milliseconds to an instant.
 *
 * @param day The day to walk from, as `yyyy-mm-dd`.
 * @param days How many days to walk, which may be negative.
 * @returns The day walked to, as `yyyy-mm-dd`.
 */
export function dayFrom(day: string, days: number): string {
    const at = new Date(`${day}T00:00:00Z`);

    at.setUTCDate(at.getUTCDate() + days);

    return at.toISOString().slice(0, 10);
}

/**
 * A formatter placed in the reader's own zone, or in the runtime's where the record states none.
 *
 * A zone this runtime's own database does not carry is fallen back from rather than thrown through: the deployment's
 * database is what validated the identifier, the two need not be the same build, and an exception raised here would
 * blank every row on the screen at once instead of one date reading in the wrong zone.
 */
function wordedIn(locale: string, options: Intl.DateTimeFormatOptions, timeZone: string | null): Intl.DateTimeFormat {
    if (timeZone === null) {
        return new Intl.DateTimeFormat(locale, options);
    }

    try {
        return new Intl.DateTimeFormat(locale, { ...options, timeZone });
    } catch {
        return new Intl.DateTimeFormat(locale, options);
    }
}

/**
 * A calendar day as the reader's language writes one.
 *
 * This is the one value here that is not an instant, and the difference is the whole reason it has a function of its
 * own: `2026-08-15` chosen in a date field is the day somebody picked rather than a moment, so it is read as local
 * midnight and never as UTC midnight. Read the other way, every reader west of Greenwich would see the chip read back
 * the day before the one they chose.
 */
export function wordCalendarDay(day: string, locale: Locale): string {
    const at = new Date(`${day}T00:00:00`);

    return Number.isNaN(at.getTime()) ? day : new Intl.DateTimeFormat(locale, { dateStyle: 'long' }).format(at);
}
