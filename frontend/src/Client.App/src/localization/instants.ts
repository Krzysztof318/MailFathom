// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Locale } from './locale';

// Every instant the client shows, worded in one place. The service hands the client an instant with an offset —
// `2026-08-31T09:41:00+00:00` — and what a reader is owed is that instant placed against *their* day rather than
// against a server's or a sender's, so nothing here passes `timeZone` to `Intl` and every screen therefore renders in
// the zone the runtime reports. That is the rule this module exists to state: a screen that named a zone of its own,
// or that rendered UTC, would be wrong for every reader who is not sitting in it, and the failure is invisible in
// review because the value still looks like a time.
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
 * and a subject. `full` is an instant a reader has stopped on, in a header they opened the message to read.
 */
export type InstantDetail = 'stamp' | 'full';

const details: Readonly<Record<InstantDetail, Intl.DateTimeFormatOptions>> = {
    stamp: { dateStyle: 'short', timeStyle: 'short' },
    full: { dateStyle: 'long', timeStyle: 'short' },
};

/**
 * An instant as the reader's own language and the reader's own clock word it, or nothing at all where what the service
 * sent is not an instant this client can read — an absence rather than a value to repair.
 */
export function wordInstant(instant: string | null, locale: Locale, detail: InstantDetail): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    return Number.isNaN(at) ? null : new Intl.DateTimeFormat(locale, details[detail]).format(at);
}

/**
 * An instant as the design project words one on a row of the list, against the reader's own clock and calendar: the
 * time alone for something that arrived today, the language's own word for yesterday, the day and the month for
 * anything earlier this year, and the short date for anything older than that.
 *
 * The three calendar comparisons are made in the reader's zone, which is what `Date`'s local getters answer with, so a
 * message that arrived late last night reads as yesterday for the person who received it rather than as today in UTC.
 * `now` is a parameter rather than read here so that a test pins it beside the zone it pins.
 */
export function wordRecentInstant(instant: string | null, locale: Locale, now: number): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    if (Number.isNaN(at)) {
        return null;
    }

    const then = new Date(at);
    const today = new Date(now);
    const yesterday = new Date(now);
    yesterday.setDate(today.getDate() - 1);

    if (sameDay(then, today)) {
        return new Intl.DateTimeFormat(locale, { timeStyle: 'short' }).format(at);
    }

    if (sameDay(then, yesterday)) {
        return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(-1, 'day');
    }

    if (then.getFullYear() === today.getFullYear()) {
        return new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit' }).format(at);
    }

    return new Intl.DateTimeFormat(locale, { dateStyle: 'short' }).format(at);
}

function sameDay(one: Date, other: Date): boolean {
    return (
        one.getFullYear() === other.getFullYear() &&
        one.getMonth() === other.getMonth() &&
        one.getDate() === other.getDate()
    );
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
