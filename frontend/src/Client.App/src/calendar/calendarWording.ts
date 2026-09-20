// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Locale } from '../localization/locale';
import { addDays, daysOf, type CalendarSpan, type CalendarView } from './calendarSpan';

// What the days a view covers are called, which is the locale's answer rather than this client's: a month name, a
// weekday, and the dash between two dates are each something `Intl` already knows in every language the client reads,
// and a catalogue entry spelling one would be a second copy of it to keep in step.
//
// It is separate from `localization/instants.ts` for the reason that module states about itself: what is worded here
// is a *day* the calendar computed rather than an instant the deployment sent, and a day read as an instant is the
// defect that puts a reader west of Greenwich on the wrong date.

/** The month and the year a view is anchored in, which is the heading the design project draws over every view. */
export function wordMonth(anchor: Date, locale: Locale): string {
    return new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' }).format(anchor);
}

/**
 * The days a view covers, said under the heading — or `null` where the heading already said them.
 *
 * The month and the agenda are the two that say nothing here: both cover the month the heading names, and a second
 * line repeating it would be the same sentence twice.
 */
export function wordSpan(view: CalendarView, span: CalendarSpan, locale: Locale): string | null {
    if (view === 'day') {
        return new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long' }).format(span.from);
    }

    if (view !== 'week') {
        return null;
    }

    return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' }).formatRange(
        span.from,
        addDays(span.until, -1),
    );
}

/** One day's own number, which is what a month cell and a week column carry. */
export function wordDayOfMonth(day: Date, locale: Locale): string {
    return new Intl.NumberFormat(locale).format(day.getDate());
}

/** The short weekday the design project draws over a column and over a month grid. */
export function wordWeekday(day: Date, locale: Locale): string {
    return new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(day);
}

/** The day and month an agenda row stands under. */
export function wordAgendaDay(day: Date, locale: Locale): string {
    return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short' }).format(day);
}

/** One hour of the day, as the row the day view draws its events in is named. */
export function wordHour(hour: Date, locale: Locale): string {
    return new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(hour);
}

/** The whole day, said where a reader has landed on it rather than scanned past it. */
export function wordDay(day: Date, locale: Locale): string {
    return new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(
        day,
    );
}

/** The seven weekday names a month grid is drawn under, in the order the grid draws its columns. */
export function weekdayNames(span: CalendarSpan, locale: Locale): readonly string[] {
    return daysOf({ from: span.from, until: addDays(span.from, 7) }).map((day) => wordWeekday(day, locale));
}
