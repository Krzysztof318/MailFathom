// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { request } from '@playwright/test';
import { credential, required } from './deployment';

// The sample calendar, task list and address book the run reads, and the write that puts them into the deployment
// before the first spec opens a browser. `frontend/playwright.end-to-end.config.ts` names this file as its global
// setup, which is where the writing happens.
//
// **It exists because only Mail arrives by itself.** `scripts/run-end-to-end-client.sh` replays a mail corpus into a
// mailbox and MailFathom synchronizes it, so the Mail space is full before anything drives it. Nothing fills the other
// three: a calendar, a task list and an address book are written by the person who owns them, so a deployment nobody
// has used holds none of them and a spec reaching one of those spaces would be asserting against an empty screen.
//
// **It is written over the client API rather than through the screens**, for the reason the mail is replayed rather
// than composed: a spec that has to build its own furniture first asserts against what it just did, and the order the
// files happen to run in becomes part of what they prove. Each of the three write specs still creates one record of
// its own through the screen, which is where the client's own write path is held to account.
//
// **Every day is an offset from the day the run happens on**, so *Today*, *This week* and *Later* are each reachable
// whenever a run happens, and the events below fall in the week and the month the calendar opens on. The instants are
// stated in UTC and the browser is pinned to UTC by the configuration, which is what makes the day this file names and
// the day the client draws the same day.
//
// **Nothing here is anybody's**: every name is invented and every host is a reserved name, on the rule
// `frontend/tests/AGENTS.md` states for the mail corpus and for the same reason.

/** One event of the sample calendar, as a day the run resolves and a clock reading. */
export interface SeededEvent {
    readonly title: string;

    /** How many days after the day the run happens on it falls. */
    readonly dayOffset: number;

    /** When it begins, as `hh:mm`, which a day-long event states none of. */
    readonly startTime: string | null;

    /** When it ends, in the same form, or `null` where it states no end. */
    readonly endTime: string | null;
}

/** One task of the sample list, dated relative to the run's own day or not dated at all. */
export interface SeededTask {
    readonly title: string;

    /** How many days after the day the run happens on it is due, or `null` where nobody has said when. */
    readonly dayOffset: number | null;
}

/** One person in the sample address book. */
export interface SeededContact {
    readonly displayName: string;
    readonly address: string;
    readonly note: string | null;
}

// The sample calendar. Two events today, so the day the space opens on is not a single row; one tomorrow and one six
// days out, so the week and the month have more than the day in them; and one stated as a day rather than as a clock
// time, because a screen that draws only timed events draws half of what a calendar holds.
//
// Each is named rather than picked out of the list by index, because what a spec wants is *the one stated as a day*
// rather than *the fourth*, and a list read by position is a spec that changes meaning when somebody adds a row.

const standup: SeededEvent = {
    title: 'Standup with the platform group',
    dayOffset: 0,
    startTime: '09:30',
    endTime: '10:15',
};

const numbersReview: SeededEvent = {
    title: 'Quarterly numbers review',
    dayOffset: 0,
    startTime: '14:00',
    endTime: '15:00',
};

const interview: SeededEvent = {
    title: 'Interview for the archivist post',
    dayOffset: 1,
    startTime: '11:00',
    endTime: '12:30',
};

const officeClosed: SeededEvent = {
    title: 'Office closed for maintenance',
    dayOffset: 3,
    startTime: null,
    endTime: null,
};

const retrospective: SeededEvent = {
    title: 'Retrospective of the migration',
    dayOffset: 6,
    startTime: '16:00',
    endTime: '17:00',
};

/** The whole sample calendar, in the order it is written. */
export const seededEvents: readonly SeededEvent[] = [standup, numbersReview, interview, officeClosed, retrospective];

/** The two on the day the run happens on, which is the one day every view of the calendar has in it. */
export const eventsSeededToday: readonly SeededEvent[] = [standup, numbersReview];

/** One of those two, which is what a spec asserting only that the seeded calendar was drawn reaches for. */
export const anEventSeededToday: SeededEvent = standup;

/** The one stated as a day rather than as a clock time. */
export const theDayLongEvent: SeededEvent = officeClosed;

// The sample task list, which reaches each of the three headings the screen draws. *This week* is the six days after
// today rather than the remainder of a calendar week, and *Later* is everything else including what nobody has dated —
// so three days out, twenty days out, and no day at all fall under the three headings whichever weekday a run happens
// on.

const theLease: SeededTask = { title: 'Send the signed lease back', dayOffset: 0 };
const theCourier: SeededTask = { title: 'Confirm the courier pickup', dayOffset: 0 };
const theSummary: SeededTask = { title: "Draft the quarter's summary", dayOffset: 3 };
const theDomain: SeededTask = { title: 'Renew the domain registration', dayOffset: 20 };
const theCoffee: SeededTask = { title: 'Choose a new coffee supplier', dayOffset: null };

/** The whole sample list, in the order it is written. */
export const seededTasks: readonly SeededTask[] = [theLease, theCourier, theSummary, theDomain, theCoffee];

/** One task under each of the three headings, which is what makes the grouping assertable. */
export const tasksUnderEachHeading: Readonly<Record<string, SeededTask>> = {
    Today: theLease,
    'This week': theSummary,
    Later: theCoffee,
};

/** The second one due today, which is what a heading holding more than one row is read from. */
export const theOtherTaskDueToday: SeededTask = theCourier;

// The sample address book. Three people, none of them anybody: every name is invented and every host is a reserved
// name, so no record here can reach a machine that exists.

const nadia: SeededContact = {
    displayName: 'Nadia Kerr',
    address: 'nadia.kerr@orchard-lane.invalid',
    note: 'Runs the archive the office rents.',
};

const tomas: SeededContact = { displayName: 'Tomas Brandt', address: 't.brandt@northfield.example', note: null };

const priya: SeededContact = {
    displayName: 'Priya Raman',
    address: 'priya@harbourworks.invalid',
    note: 'Signs off the maintenance windows.',
};

/** The whole sample book, in the order it is written. */
export const seededContacts: readonly SeededContact[] = [nadia, tomas, priya];

/** The person a spec opens, which is one with a note so that the page has more than a name on it. */
export const theOpenedContact: SeededContact = nadia;

// The day the whole run is read against, taken once so that a run crossing midnight seeds one day rather than two.
const runsOn = Date.now();

/** The day an offset names, as `yyyy-mm-dd` in UTC. */
export function seededDay(dayOffset: number): string {
    return new Date(runsOn + dayOffset * 86_400_000).toISOString().slice(0, 10);
}

/** The instant an event begins at, which a day-long one states as that day's own midnight. */
function beginsAt(event: SeededEvent): string {
    return `${seededDay(event.dayOffset)}T${event.startTime ?? '00:00'}:00.000Z`;
}

/** The instant an event ends at, or `null` where it states no end — which every day-long one does. */
function endsAt(event: SeededEvent): string | null {
    return event.endTime === null ? null : `${seededDay(event.dayOffset)}T${event.endTime}:00.000Z`;
}

/**
 * Writes the three sets into the deployment, over the same surface the client writes through.
 *
 * A refusal stops the run here rather than in a spec, and says which of the three it was: a calendar the deployment
 * would not take and a screen that cannot draw one are two different defects, and a suite that discovered the first
 * as an empty list would report the second.
 */
export default async function seed(): Promise<void> {
    const api = await request.newContext({
        baseURL: required('MAILFATHOM_CLIENT_ORIGIN'),
        extraHTTPHeaders: {
            // Stated on every request rather than left to a challenge, because the client surface answers an
            // unauthenticated write with its own refusal rather than with one this context would answer.
            Authorization: `Basic ${Buffer.from(`${credential.userName}:${credential.password}`).toString('base64')}`,
        },
    });

    try {
        for (const event of seededEvents) {
            await written(api, '/api/client/calendar', {
                title: event.title,
                start: beginsAt(event),
                end: endsAt(event),
                isAllDay: event.startTime === null,
                reminders: [],
                sourceMessage: null,
            });
        }

        for (const task of seededTasks) {
            await written(api, '/api/client/tasks', {
                title: task.title,
                dueOn: task.dayOffset === null ? null : seededDay(task.dayOffset),
                reminders: [],
                dueDayOffsetMinutes: null,
                sourceMessageId: null,
            });
        }

        for (const contact of seededContacts) {
            await written(api, '/api/client/contacts', {
                displayName: contact.displayName,
                addresses: [contact.address],
                preferredAddress: contact.address,
                note: contact.note,
            });
        }
    } finally {
        await api.dispose();
    }
}

/**
 * Writes one record and answers only where the deployment took it.
 *
 * Two of the three routes refuse with a status and the third answers `200` naming an outcome, so both are read: a
 * contact the book already held comes back as a successful request that wrote nothing, which is exactly the seed that
 * would leave a screen half full.
 */
async function written(
    api: Awaited<ReturnType<typeof request.newContext>>,
    route: string,
    record: unknown,
): Promise<void> {
    const answer = await api.post(route, { data: record });

    if (!answer.ok()) {
        throw new Error(`The deployment refused ${route} with ${answer.status().toFixed(0)}: ${await answer.text()}`);
    }

    const answered: unknown = await answer.json();
    const outcome =
        typeof answered === 'object' && answered !== null && 'outcome' in answered ? answered.outcome : 'Written';

    if (outcome !== 'Written') {
        throw new Error(`The deployment answered ${route} with '${String(outcome)}' rather than writing the record.`);
    }
}
