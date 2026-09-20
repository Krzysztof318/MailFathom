// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent, CalendarEventAmendment } from '@mailfathom/client-backend';

// What somebody has typed into an event, and the two directions it travels: out of an event the deployment holds, and
// into the record a write states. Both dialogs read it — writing an event down and amending one are the same four
// fields — so it is stated once rather than twice with a difference nobody meant.
//
// **A day and a time are what a person picks, and an instant is what the deployment holds.** The platform's own date
// and time fields answer `2026-09-24` and `09:00`, which are a day and a clock reading rather than moments, so they
// are resolved here against the reader's own zone — never against UTC, which would put every reader west of Greenwich
// on the day before the one they picked.

/** What a person has typed into an event, in the shape the fields on the screen hold it. */
export interface EventDraft {
    readonly title: string;

    /** The day it is on, as a date field answers one: `yyyy-mm-dd`. */
    readonly day: string;

    /** When it begins, as a time field answers one: `hh:mm`. */
    readonly start: string;

    /** When it ends, in the same form, or empty to state no end. */
    readonly end: string;

    /** Whether it is stated as a day rather than as a clock time, which is what puts the two time fields away. */
    readonly allDay: boolean;

    /** What is to announce it, in minutes before it begins, which the event's own panel decides. */
    readonly reminders: readonly number[];
}

/** An event with nothing typed into it yet, which is what the new-event dialog opens on. */
export const nothingDrafted: EventDraft = { title: '', day: '', start: '', end: '', allDay: false, reminders: [] };

/** The event the deployment holds, read back into the fields that would have produced it. */
export function draftOf(event: CalendarEvent): EventDraft {
    const begins = new Date(event.start);
    const ends = event.end === null ? null : new Date(event.end);

    // An event whose beginning cannot be read leaves every time empty rather than only that one: an end standing in a
    // form with no day and no beginning is a time that means nothing, and a reader would have to clear it themselves
    // before the form could be saved at all.
    if (Number.isNaN(begins.getTime())) {
        return { ...nothingDrafted, title: event.title, allDay: event.isAllDay, reminders: event.reminders };
    }

    // A day carries no clock reading, so the two time fields are empty rather than holding the midnight its instant
    // was written at — a reader turning the day off is picking a time rather than correcting one this form invented.
    return {
        title: event.title,
        day: writtenDay(begins),
        start: event.isAllDay ? '' : writtenTime(begins),
        end: event.isAllDay || ends === null || Number.isNaN(ends.getTime()) ? '' : writtenTime(ends),
        allDay: event.isAllDay,
        reminders: event.reminders,
    };
}

/** The day a date field opens on where the reader is standing on that day, which is what the new-event dialog uses. */
export function draftOn(day: Date): EventDraft {
    return { ...nothingDrafted, day: writtenDay(day) };
}

/**
 * The record a write would state, or `null` where the draft is not one yet.
 *
 * Three things make it `null`, and each is something the screen can see for itself: a title nobody typed, a day or a
 * beginning nobody picked, and an end that is not after the beginning. Everything else the deployment judges is
 * reported as an outcome rather than refused here, because a second copy of a rule is how two of them come to
 * disagree.
 *
 * **A day asks for no beginning and states none**, which is the one place the three rules above read differently: what
 * it is written down as is the day's own midnight in the reader's zone, and the deployment is told it is a day.
 */
export function recordOf(draft: EventDraft): CalendarEventAmendment | null {
    const title = draft.title.trim();

    if (title === '' || draft.day === '') {
        return null;
    }

    if (draft.allDay) {
        const day = new Date(`${draft.day}T00:00`);

        return Number.isNaN(day.getTime())
            ? null
            : { title, start: day.toISOString(), end: null, isAllDay: true, reminders: draft.reminders };
    }

    if (draft.start === '') {
        return null;
    }

    const begins = new Date(`${draft.day}T${draft.start}`);

    if (Number.isNaN(begins.getTime())) {
        return null;
    }

    if (draft.end === '') {
        return { title, start: begins.toISOString(), end: null, isAllDay: false, reminders: draft.reminders };
    }

    const ends = new Date(`${draft.day}T${draft.end}`);

    if (Number.isNaN(ends.getTime()) || ends.getTime() <= begins.getTime()) {
        return null;
    }

    return {
        title,
        start: begins.toISOString(),
        end: ends.toISOString(),
        isAllDay: false,
        reminders: draft.reminders,
    };
}

/** The draft an answered description leaves behind, which is the deployment's reading put into the fields to edit. */
export function draftedFrom(
    drafted: { readonly title: string | null; readonly start: string | null; readonly end: string | null },
    standing: EventDraft,
): EventDraft {
    const begins = drafted.start === null ? null : new Date(drafted.start);
    const ends = drafted.end === null ? null : new Date(drafted.end);

    return {
        ...standing,
        title: drafted.title ?? standing.title,
        day: begins === null || Number.isNaN(begins.getTime()) ? standing.day : writtenDay(begins),
        start: begins === null || Number.isNaN(begins.getTime()) ? standing.start : writtenTime(begins),
        end: ends === null || Number.isNaN(ends.getTime()) ? standing.end : writtenTime(ends),
    };
}

function writtenDay(at: Date): string {
    return `${padded(at.getFullYear(), 4)}-${padded(at.getMonth() + 1, 2)}-${padded(at.getDate(), 2)}`;
}

function writtenTime(at: Date): string {
    return `${padded(at.getHours(), 2)}:${padded(at.getMinutes(), 2)}`;
}

function padded(value: number, places: number): string {
    return value.toFixed(0).padStart(places, '0');
}
