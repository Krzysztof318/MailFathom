// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { conversationId } from './mail';

// The signed-in person's calendar, as the client surface publishes it: what they have agreed to, and the dates their
// own mail proposed and nobody has answered yet.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. Nobody here exists,
// every name is invented and every host is a reserved one, exactly as for the mail.
//
// **A window is composed against the span it was asked for rather than stated at fixed dates**, which is what makes
// this file different from the books beside it. A calendar is read as the week somebody is standing in, so events
// written down on a date in this file would be on the screen for one week of one year and nowhere else — the screen
// would be empty on every run and in every capture. The route answers a window, so the corpus answers one: the same
// seven entries, placed against whatever span was asked for.

/**
 * One window of the calendar, placed against the span that was asked for.
 *
 * @param from The beginning of the span, as the request states it.
 * @param until Its end, which decides how many days the entries are spread over.
 */
export function calendarWindow(from: string, until: string): { events: readonly unknown[] } {
    const opens = new Date(from);
    const closes = new Date(until);

    if (Number.isNaN(opens.getTime()) || Number.isNaN(closes.getTime()) || closes <= opens) {
        return { events: [] };
    }

    const days = Math.max(1, Math.round((closes.getTime() - opens.getTime()) / 86_400_000));

    return {
        events: placed.map((entry, at) => ({
            id: `calendar-${String(at + 1)}`,
            title: entry.title,
            start: startingOn(opens, entry.day % days, entry.hour),
            end: entry.hours === null ? null : startingOn(opens, entry.day % days, entry.hour + entry.hours),
            isAllDay: false,
            reminders: entry.reminds,
            remindsAt: entry.reminds.map((lead) => leadingUpTo(opens, entry.day % days, entry.hour, lead)),
            origin: entry.origin,
            sourceMessage: entry.origin === 'Proposed' ? conversationId : null,
            recordedAt: startingOn(opens, 0, 8),
            amendedAt: startingOn(opens, 0, 8),
        })),
    };
}

/** A calendar with nothing on it, which is the empty state of every view and of the column beside them. */
export const emptyCalendarWindow = { events: [] };

/**
 * The event a write answers with, which is the record as the calendar now holds it.
 *
 * One record for every write on the surface: the screen reads the span again rather than believing this, so what it
 * carries only has to be an event the client can read.
 */
export const calendarEventWritten = {
    id: 'calendar-written',
    title: 'Warehouse handover',
    start: '2026-09-24T09:00:00+02:00',
    end: '2026-09-24T10:00:00+02:00',
    isAllDay: false,
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-24T08:00:00+02:00',
    amendedAt: '2026-09-24T08:00:00+02:00',
};

/** Whether this deployment turns a typed description into an event, which is what draws the field at all. */
export const calendarDescriptionsRead = { readsDescriptions: true };

/** One typed sentence read into the event it describes, which is what the field answers with. */
export const calendarEventDrafted = {
    drafted: true,
    title: 'Lunch with Anna',
    start: '2026-09-24T13:00:00+02:00',
    end: '2026-09-24T14:00:00+02:00',
};

// The seven entries, said as a day of the span and an hour of that day rather than as dates. Two of them are what the
// mail proposed, which is what puts something in the column beside the views on every run; one states no end, which is
// the entry an hour row and an agenda line each draw differently, and one carries what announces it so the panel that
// edits a set has a set to open on.
const placed = [
    { title: 'Warehouse handover', day: 0, hour: 9, hours: 1, reminds: [15], origin: 'Asserted' },
    { title: 'Stand-up', day: 1, hour: 9, hours: null, reminds: [], origin: 'Asserted' },
    { title: 'Carrier review', day: 1, hour: 14, hours: 2, reminds: [], origin: 'Asserted' },
    { title: 'Racking quote walkthrough', day: 2, hour: 11, hours: 1, reminds: [], origin: 'Proposed' },
    { title: 'Quarter close', day: 3, hour: 10, hours: 3, reminds: [], origin: 'Asserted' },
    { title: 'Site visit in Gdańsk', day: 4, hour: 8, hours: 6, reminds: [], origin: 'Proposed' },
    { title: 'Retro', day: 4, hour: 16, hours: 1, reminds: [], origin: 'Asserted' },
] as const;

function startingOn(opens: Date, day: number, hour: number): string {
    return new Date(opens.getFullYear(), opens.getMonth(), opens.getDate() + day, hour).toISOString();
}

function leadingUpTo(opens: Date, day: number, hour: number, lead: number): string {
    return new Date(opens.getFullYear(), opens.getMonth(), opens.getDate() + day, hour, -lead).toISOString();
}
