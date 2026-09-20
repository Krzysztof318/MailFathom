// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';

// The arithmetic every view over the calendar is: which days a view covers, which day an event falls on, and where the
// next span begins. It sits beside the screen rather than inside it because four components read the same answers and
// a fifth — the read itself — turns one of them into the window it asks the deployment for.
//
// **Every day here is a day in the reader's own zone**, because that is what a calendar is: the service sends an
// instant with an offset and a reader is owed it placed against *their* Thursday. So every boundary is built with
// `Date`'s local constructors and never with `Date.UTC`, and a screen west of Greenwich does not draw an eight o'clock
// meeting on the day before.
//
// **Nothing here words anything.** A month name, a weekday, and a week number said out loud are the locale's, so this
// module answers days and numbers and `calendarWording.ts` beside it asks `Intl` what they are called.

/** The four views the design project draws over a calendar, in the order it draws them. */
export const calendarViews = ['day', 'week', 'month', 'agenda'] as const;

export type CalendarView = (typeof calendarViews)[number];

/** Half-open, exactly as the deployment's own window is: an event beginning at `until` falls outside. */
export interface CalendarSpan {
    readonly from: Date;
    readonly until: Date;
}

/** How long a day is, where nothing crosses a daylight-saving boundary — which is every use of it below. */
const aDay = 86_400_000;

/** Midnight at the start of that day, in the reader's own zone. */
export function startOfDay(day: Date): Date {
    return new Date(day.getFullYear(), day.getMonth(), day.getDate());
}

/**
 * The same clock time that many days later, which is midnight where it started at midnight.
 *
 * Through the calendar rather than through milliseconds, so a week crossing a daylight-saving boundary is seven days
 * rather than seven days and an hour.
 */
export function addDays(day: Date, count: number): Date {
    const moved = new Date(day);

    moved.setDate(moved.getDate() + count);

    return moved;
}

/** Midnight on the Monday of that day's week, which is the week the design project draws. */
export function startOfWeek(day: Date): Date {
    const midnight = startOfDay(day);

    // `getDay` counts from Sunday, and the design project's own week runs Monday to Sunday.
    return addDays(midnight, -((midnight.getDay() + 6) % 7));
}

/** Midnight on the first of that day's month. */
export function startOfMonth(day: Date): Date {
    return new Date(day.getFullYear(), day.getMonth(), 1);
}

/**
 * The days a view covers when it is anchored on that day.
 *
 * The month view covers whole weeks rather than the month itself, because that is the grid it draws — the days either
 * side of the month are in the span so that the cells holding them are not drawn empty for events that are there. The
 * agenda covers the month proper, being a list rather than a grid.
 */
export function spanOf(view: CalendarView, anchor: Date): CalendarSpan {
    switch (view) {
        case 'day': {
            const from = startOfDay(anchor);

            return { from, until: addDays(from, 1) };
        }

        case 'week': {
            const from = startOfWeek(anchor);

            return { from, until: addDays(from, 7) };
        }

        case 'month': {
            const from = startOfWeek(startOfMonth(anchor));
            const beyond = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 1);

            return { from, until: startOfWeek(addDays(beyond, 6)) };
        }

        default:
            return { from: startOfMonth(anchor), until: new Date(anchor.getFullYear(), anchor.getMonth() + 1, 1) };
    }
}

/** Where the anchor lands after moving that many spans forward, or backward for a negative count. */
export function movedBy(view: CalendarView, anchor: Date, spans: number): Date {
    switch (view) {
        case 'day':
            return addDays(startOfDay(anchor), spans);

        case 'week':
            return addDays(startOfWeek(anchor), spans * 7);

        default:
            return new Date(anchor.getFullYear(), anchor.getMonth() + spans, 1);
    }
}

/** Every day the span covers, earliest first. */
export function daysOf(span: CalendarSpan): readonly Date[] {
    const days: Date[] = [];

    for (let day = span.from; day < span.until; day = addDays(day, 1)) {
        days.push(day);
    }

    return days;
}

/** The twenty-four hours of that day, earliest first — every one of them, so no event is drawn outside the grid. */
export function hoursOfDay(day: Date): readonly Date[] {
    const midnight = startOfDay(day);

    return Array.from(
        { length: 24 },
        (_, hour) => new Date(midnight.getFullYear(), midnight.getMonth(), midnight.getDate(), hour),
    );
}

/** Whether the two instants fall on one day in the reader's own zone. */
export function sameDay(one: Date, other: Date): boolean {
    return (
        one.getFullYear() === other.getFullYear() &&
        one.getMonth() === other.getMonth() &&
        one.getDate() === other.getDate()
    );
}

/**
 * The week of the year that day falls in, as ISO 8601 counts one.
 *
 * The design project draws it beside the week's own dates, and it is arithmetic rather than something `Intl` answers.
 * The standard puts a week in the year that holds its Thursday, which is both shifts below: the week is named by its
 * own Thursday, and week one is the week holding the fourth of January — the one day the standard guarantees is in it.
 * The distance is rounded rather than truncated because a daylight-saving boundary inside the run would otherwise
 * leave it an hour short of a whole number of weeks.
 */
export function isoWeek(day: Date): number {
    const thursday = addDays(startOfWeek(day), 3);
    const firstThursday = addDays(startOfWeek(new Date(thursday.getFullYear(), 0, 4)), 3);

    return Math.round((thursday.getTime() - firstThursday.getTime()) / (aDay * 7)) + 1;
}

/**
 * The events that touch that day, earliest first.
 *
 * An event touches a day when any part of it falls on one, which is what puts a meeting running from Monday evening
 * into Tuesday morning in both columns rather than only in the one it began in. An event stating no end lasts an
 * instant, which is what the surface says it does.
 */
export function eventsOn(events: readonly CalendarEvent[], day: Date): readonly CalendarEvent[] {
    const opens = startOfDay(day).getTime();
    const closes = addDays(startOfDay(day), 1).getTime();

    return events.filter((event) => {
        const begins = Date.parse(event.start);

        if (Number.isNaN(begins)) {
            return false;
        }

        const ends = event.end === null ? begins : Date.parse(event.end);

        return begins < closes && (Number.isNaN(ends) ? begins : ends) >= opens;
    });
}

/**
 * The events drawn in that hour of the day, which are the ones beginning inside it.
 *
 * One that began before the day is drawn in its first hour rather than not at all: a reader looking at Tuesday has to
 * see the meeting that started on Monday night, and the row it stands in is the earliest one the day has.
 */
export function eventsInHour(events: readonly CalendarEvent[], hour: Date): readonly CalendarEvent[] {
    const opens = hour.getTime();
    const closes = new Date(hour.getFullYear(), hour.getMonth(), hour.getDate(), hour.getHours() + 1).getTime();
    const dayOpens = startOfDay(hour).getTime();

    return events.filter((event) => {
        const begins = Date.parse(event.start);

        if (Number.isNaN(begins)) {
            return false;
        }

        const drawnAt = Math.max(begins, dayOpens);

        return drawnAt >= opens && drawnAt < closes;
    });
}

/** The events in the order a reader meets them: earliest first, and by title where two begin together. */
export function inTimeOrder(events: readonly CalendarEvent[]): readonly CalendarEvent[] {
    return [...events].sort((one, other) => {
        const difference = Date.parse(one.start) - Date.parse(other.start);

        return difference === 0 ? one.title.localeCompare(other.title) : difference;
    });
}
