// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The signed-in person's own calendar: the events they put on it, the dates their mail proposed to them, and the five
// acts they perform on either. Nothing here reaches a calendar server — a MailFathom event is native to the deployment
// the credential signed in to.
//
// **Every read is a window, because every view over a calendar is one.** A month, a week, a day, and an agenda are
// four spans rather than four questions, so one function answers them all and the caller states the span it drew. The
// half of the calendar is stated beside it: what is on the calendar and what mail proposed are drawn in different
// places, so a caller asks for one half or for both rather than sorting an answer out afterwards.
//
// **A write reports an outcome rather than refusing.** An event the calendar no longer holds, a record the deployment
// would not accept, and a proposal somebody else already took are each something a screen says a sentence about and
// carries on from, so they are read off the status here and answered as values. `ClientResult` therefore means *the
// request was answered*, exactly as it does for the address book.

/** The route a window of the calendar is read from and an event is written to, relative to the client prefix. */
export const calendarRoute = '/calendar';

/** The route a description is read into a draft at, relative to the client prefix. */
export const calendarEventDraftRoute = '/calendar/drafts';

/** The route one event is read, amended, and deleted at, relative to the client prefix. */
export function calendarEventRoute(eventId: string): string {
    return `${calendarRoute}/${encodeURIComponent(eventId)}`;
}

/** The route a proposed event is taken onto the calendar at, relative to the client prefix. */
export function calendarEventAcceptanceRoute(eventId: string): string {
    return `${calendarEventRoute(eventId)}/acceptance`;
}

/** Whether an event is on the calendar or offered to it. */
export type CalendarEventOrigin = 'Asserted' | 'Proposed';

/** One event as the calendar holds it. */
export interface CalendarEvent {
    readonly id: string;
    readonly title: string;

    /** When it begins, as the instant the deployment holds with the offset it holds it in. */
    readonly start: string;

    /** When it ends, or `null` where nothing said how long it lasts. */
    readonly end: string | null;

    /** Whether it is stated as a day rather than as a clock time, which decides how it is drawn and announced. */
    readonly isAllDay: boolean;

    /** What announces it, in minutes before it begins, in the order the deployment holds them. */
    readonly reminders: readonly number[];

    /** When each of those falls, resolved by the deployment so that no screen computes an instant of its own. */
    readonly remindsAt: readonly string[];

    readonly origin: CalendarEventOrigin;

    /** The message it came out of, or `null` where no message named it. */
    readonly sourceMessage: string | null;

    readonly recordedAt: string;
    readonly amendedAt: string;
}

/** One window of the calendar, earliest first. */
export interface CalendarWindow {
    readonly events: readonly CalendarEvent[];
}

/** The event a caller states for their own calendar. */
export interface CalendarEventRecord {
    readonly title: string;
    readonly start: string;

    /** When it ends, or `null` to state no end. */
    readonly end: string | null;

    /** Whether it is stated as a day rather than as a clock time. */
    readonly isAllDay: boolean;

    /** What is to announce it, in minutes before it begins, which `isStatableReminderSet` is the bar for. */
    readonly reminders: readonly number[];

    /** The message it was created from, or `null` where none was open. */
    readonly sourceMessage: string | null;
}

/** The record a caller states one of their own events is to stand as, which cannot restate its origin or its source. */
export interface CalendarEventAmendment {
    readonly title: string;
    readonly start: string;
    readonly end: string | null;

    /** Whether it now stands as a day rather than as a clock time. */
    readonly isAllDay: boolean;

    /** What is to announce it from now on, which replaces whatever it carried rather than adding to it. */
    readonly reminders: readonly number[];
}

/**
 * How one write to the calendar ended.
 *
 * `Refused` is every rule the deployment enforces about the record itself — a title it will not take, an end before a
 * beginning — reported as one, because a screen says the same sentence about each and the refusal deliberately carries
 * nothing of what was sent.
 */
export type CalendarWriteOutcome = 'Written' | 'NotFound' | 'Refused' | 'AlreadyOnTheCalendar';

/** What one write to the calendar produced. */
export interface CalendarEventWrite {
    readonly outcome: CalendarWriteOutcome;

    /** The event as the calendar now holds it, present exactly where the write was performed. */
    readonly event: CalendarEvent | null;
}

/** What taking one event off the calendar removed, which is also how a proposal is dismissed. */
export interface CalendarEventRemoval {
    /** Whether the calendar still held it, which is `false` for one somebody else had already removed. */
    readonly wasHeld: boolean;
}

/** Whether this deployment turns a typed description into an event at all. */
export interface CalendarEventDrafting {
    readonly readsDescriptions: boolean;
}

/** What one typed description was read as: the event it describes, or the statement that none was read. */
export interface CalendarEventDraft {
    /** Whether a draft was read at all, which is `false` where the deployment reads none or the sentence named no day. */
    readonly drafted: boolean;

    /**
     * Whether the deployment has spent what it allows a chat provider for the current period.
     *
     * Its own answer rather than a failure, because a person typing sentences into a field that has quietly stopped
     * working is owed the reason and the form beside it, not a screen that says the deployment is unreachable.
     */
    readonly spent: boolean;

    readonly title: string | null;
    readonly start: string | null;
    readonly end: string | null;
}

// What the deployment will accept as an event's reminders, which is a fact about the contract rather than about a
// screen — so it is stated here once and the panel asks rather than carrying a second copy of the same three rules.
// What carries a changed set to the deployment is the amendment below, an event's reminders being part of the event
// rather than a record of their own.

/** The most reminders one event carries, which is the deployment's own ceiling and a refusal rather than a clamp. */
export const mostRemindersOnAnEvent = 16;

/** The longest lead a reminder may state, in minutes before the event, which is four weeks. */
export const longestReminderLead = 28 * 24 * 60;

/**
 * Reports whether a set of leads is one an event may carry.
 *
 * The same three rules the deployment applies — how many, how far ahead, and each one once — so a panel refuses a
 * lead as it is added rather than only once the event is written.
 */
export function isStatableReminderSet(reminders: readonly number[]): boolean {
    return (
        reminders.length <= mostRemindersOnAnEvent &&
        reminders.every(isStatableReminderLead) &&
        new Set(reminders).size === reminders.length
    );
}

/** Reports whether one lead, in minutes before the event, is one a reminder may state. */
export function isStatableReminderLead(minutesBefore: number): boolean {
    return Number.isSafeInteger(minutesBefore) && minutesBefore >= 0 && minutesBefore <= longestReminderLead;
}

/**
 * The most events one window may answer with, which is the deployment's own bound rather than a preference.
 *
 * A request asking for more than this is refused rather than quietly served this many, so a caller states a window it
 * can render instead of discovering the bound from a refusal.
 */
export const mostCalendarEventsPerWindow = 1000;

/** How many a screen asks for where it states no bound of its own, which is what the service serves by default. */
export const calendarEventsPerWindow = 200;

// An event is a title, two instants, an identity and three short fields. Generous against that arithmetic over a full
// window and well under the transport's own backstop.
const longestCalendarWindow = 512 * 1024;

// One event, one write answer, and one draft are each a single record of the same shape.
const longestCalendarAnswer = 32 * 1024;

const calendarEventOrigins: readonly CalendarEventOrigin[] = ['Asserted', 'Proposed'];

/**
 * Reads one window of the signed-in person's calendar.
 *
 * @param window The span to read, the half of the calendar to read, and how many events the answer may carry.
 */
export function readCalendarWindow(
    session: ClientSession,
    transport: MailFathomTransport,
    span: {
        readonly from: string;
        readonly until: string;
        readonly origin?: CalendarEventOrigin | null;
        readonly count?: number;
    },
): Promise<ClientResult<CalendarWindow>> {
    const count = Math.min(span.count ?? calendarEventsPerWindow, mostCalendarEventsPerWindow);

    return spanned(`GET ${calendarRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, calendarRoute)}?${windowArguments(span.from, span.until, span.origin ?? null, count)}`,
            headers: headersFor(session),
            longestAnswer: longestCalendarWindow,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const answered = parseWindow(bodyOf(response), count);

        return answered === null ? failed('unreadable', response.status) : read(answered);
    });
}

/**
 * Reads one event of the signed-in person's calendar.
 *
 * A `404` here is `missing` rather than `unavailable`, which is the reading `failureReasonForStatus` leaves to a route
 * that names one thing: an event deleted while somebody had it open is let go of, where a deployment that is down is
 * retried.
 */
export function readCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    eventId: string,
): Promise<ClientResult<CalendarEvent>> {
    return spanned(`GET ${calendarRoute}/{eventId}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, calendarEventRoute(eventId)),
            headers: headersFor(session),
            longestAnswer: longestCalendarAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const held = parseCalendarEvent(bodyOf(response));

        return held === null ? failed('unreadable', response.status) : read(held);
    });
}

/** Puts an event the signed-in person states on their own calendar, which is always one they asserted. */
export function recordCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    record: CalendarEventRecord,
): Promise<ClientResult<CalendarEventWrite>> {
    return spanned(`POST ${calendarRoute}`, async () =>
        writeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, calendarRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(record),
                longestAnswer: longestCalendarAnswer,
            }),
        ),
    );
}

/** Amends one event to the record the caller states, which is the whole record rather than the part that changed. */
export function amendCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    eventId: string,
    amendment: CalendarEventAmendment,
): Promise<ClientResult<CalendarEventWrite>> {
    return spanned(`PUT ${calendarRoute}/{eventId}`, async () =>
        writeOf(
            await send(transport, {
                method: 'PUT',
                path: routeFor(session, calendarEventRoute(eventId)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(amendment),
                longestAnswer: longestCalendarAnswer,
            }),
        ),
    );
}

/** Takes a date the person's mail proposed onto their calendar. */
export function acceptCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    eventId: string,
): Promise<ClientResult<CalendarEventWrite>> {
    return spanned(`POST ${calendarRoute}/{eventId}/acceptance`, async () =>
        writeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, calendarEventAcceptanceRoute(eventId)),
                headers: headersFor(session),
                longestAnswer: longestCalendarAnswer,
            }),
        ),
    );
}

/**
 * Takes one event off the calendar, whether the person put it there or their mail proposed it.
 *
 * The one route here that answers no body, so a `404` is the event having gone rather than a failure to report: the
 * calendar holds no such event either way, which is what the caller asked for.
 */
export function deleteCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    eventId: string,
): Promise<ClientResult<CalendarEventRemoval>> {
    return spanned(`DELETE ${calendarRoute}/{eventId}`, async () => {
        const response = await send(transport, {
            method: 'DELETE',
            path: routeFor(session, calendarEventRoute(eventId)),
            headers: headersFor(session),
            longestAnswer: longestCalendarAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return read({ wasHeld: false });
        }

        return response.status === 204
            ? read({ wasHeld: true })
            : failed(failureReasonForStatus(response.status), response.status);
    });
}

/**
 * Says whether this deployment reads a typed description into an event.
 *
 * A capability rather than a probe: it costs the deployment no provider call, so a screen asks it once and draws the
 * field only where there is something behind it.
 */
export function readsCalendarDescriptions(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<CalendarEventDrafting>> {
    return spanned(`GET ${calendarEventDraftRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, calendarEventDraftRoute),
            headers: headersFor(session),
            longestAnswer: longestCalendarAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const answer = asRecord(bodyOf(response));
        const readsDescriptions = answer?.['readsDescriptions'];

        return typeof readsDescriptions === 'boolean'
            ? read({ readsDescriptions })
            : failed('unreadable', response.status);
    });
}

/**
 * Reads one sentence somebody typed into the event it describes, storing none of it.
 *
 * @param described What was typed, and the instant the person typing it is standing on — which is what every relative
 * day and hour in the sentence is resolved against, and why it carries the reader's own offset rather than the
 * deployment's.
 */
export function draftCalendarEvent(
    session: ClientSession,
    transport: MailFathomTransport,
    described: { readonly description: string; readonly writtenAt: string },
): Promise<ClientResult<CalendarEventDraft>> {
    return spanned(`POST ${calendarEventDraftRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, calendarEventDraftRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify(described),
            longestAnswer: longestCalendarAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 429) {
            return read({ drafted: false, spent: true, title: null, start: null, end: null });
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const draft = parseDraft(bodyOf(response));

        return draft === null ? failed('unreadable', response.status) : read(draft);
    });
}

/**
 * An instant in the one form the drafting route reads: a local wall clock with the offset it is being read in.
 *
 * Composed here rather than taken from `toISOString`, which words every instant in UTC with a `Z` the route's own
 * format refuses — and the offset is the whole point of the value, being what turns *Thursday at three* into three
 * o'clock where the person typing it is.
 */
export function describedAt(at: Date): string {
    const aheadOfUtcBy = -at.getTimezoneOffset();
    const sign = aheadOfUtcBy < 0 ? '-' : '+';
    const away = Math.abs(aheadOfUtcBy);

    return (
        `${padded(at.getFullYear(), 4)}-${padded(at.getMonth() + 1, 2)}-${padded(at.getDate(), 2)}` +
        `T${padded(at.getHours(), 2)}:${padded(at.getMinutes(), 2)}:${padded(at.getSeconds(), 2)}` +
        `${sign}${padded(Math.floor(away / 60), 2)}:${padded(away % 60, 2)}`
    );
}

function padded(value: number, places: number): string {
    return value.toFixed(0).padStart(places, '0');
}

// Written out rather than composed with `URLSearchParams`, which is a browser API and therefore one this package
// declares nothing of: the wire half of the client knows a route and a status code and nothing about a document.
function windowArguments(from: string, until: string, origin: CalendarEventOrigin | null, count: number): string {
    const asked = [`from=${encodeURIComponent(from)}`, `until=${encodeURIComponent(until)}`];

    if (origin !== null) {
        asked.push(`origin=${origin}`);
    }

    asked.push(`count=${count.toFixed(0)}`);

    return asked.join('&');
}

function bodyOf(response: ClientResponse): unknown {
    try {
        return JSON.parse(response.body);
    } catch {
        return null;
    }
}

// The four statuses a write on this surface answers with, read into the vocabulary a screen acts on. `400` is the
// deployment refusing the record rather than the client having reached the wrong address, and `409` is a proposal
// somebody else already took — neither is a failure to retry, and both are sentences a screen says.
function writeOf(response: ClientResponse | null): ClientResult<CalendarEventWrite> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return read({ outcome: 'NotFound', event: null });
    }

    if (response.status === 409) {
        return read({ outcome: 'AlreadyOnTheCalendar', event: null });
    }

    if (response.status === 400) {
        return read({ outcome: 'Refused', event: null });
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const written = parseCalendarEvent(bodyOf(response));

    return written === null ? failed('unreadable', response.status) : read({ outcome: 'Written', event: written });
}

function parseWindow(value: unknown, asked: number): CalendarWindow | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const entries = record['events'];

    if (!Array.isArray(entries) || entries.length > asked) {
        return null;
    }

    const events: CalendarEvent[] = [];
    for (const entry of entries) {
        const held = parseCalendarEvent(entry);
        if (held === null) {
            return null;
        }

        events.push(held);
    }

    return { events };
}

function parseDraft(value: unknown): CalendarEventDraft | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const drafted = record['drafted'];
    const title = record['title'] ?? null;
    const start = record['start'] ?? null;
    const end = record['end'] ?? null;

    if (typeof drafted !== 'boolean') {
        return null;
    }

    if ((title !== null && typeof title !== 'string') || (start !== null && typeof start !== 'string')) {
        return null;
    }

    if (end !== null && typeof end !== 'string') {
        return null;
    }

    // A draft that says it read something and names no day read nothing a form could be filled from, which is an
    // answer this client cannot use rather than one it renders half of.
    if (drafted && (title === null || start === null)) {
        return null;
    }

    return { drafted, spent: false, title, start, end };
}

/** Reads one event off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseCalendarEvent(value: unknown): CalendarEvent | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const title = record['title'];
    const start = record['start'];
    const end = record['end'] ?? null;
    const isAllDay = record['isAllDay'];
    const reminders = record['reminders'];
    const remindsAt = record['remindsAt'];
    const origin = record['origin'];
    const sourceMessage = record['sourceMessage'] ?? null;
    const recordedAt = record['recordedAt'];
    const amendedAt = record['amendedAt'];

    if (typeof id !== 'string' || typeof title !== 'string' || typeof start !== 'string') {
        return null;
    }

    if (typeof recordedAt !== 'string' || typeof amendedAt !== 'string' || !isCalendarEventOrigin(origin)) {
        return null;
    }

    if (end !== null && typeof end !== 'string') {
        return null;
    }

    if (sourceMessage !== null && typeof sourceMessage !== 'string') {
        return null;
    }

    // An announcement the contract says cannot exist — more than an event may carry, a lead further ahead than one may
    // state, or the same lead twice — is a body this client refuses rather than a set it draws: the panel enforces the
    // same three rules before it writes, so an answer breaking them describes an event nothing here could have made.
    if (typeof isAllDay !== 'boolean' || !isReminderSet(reminders) || !isInstantList(remindsAt)) {
        return null;
    }

    return { id, title, start, end, isAllDay, reminders, remindsAt, origin, sourceMessage, recordedAt, amendedAt };
}

function isReminderSet(value: unknown): value is readonly number[] {
    return (
        Array.isArray(value) &&
        value.length <= mostRemindersOnAnEvent &&
        value.every((lead) => typeof lead === 'number') &&
        isStatableReminderSet(value)
    );
}

function isInstantList(value: unknown): value is readonly string[] {
    return (
        Array.isArray(value) && value.length <= mostRemindersOnAnEvent && value.every((at) => typeof at === 'string')
    );
}

/** Whether the value is one of the two origins this surface publishes. */
export function isCalendarEventOrigin(value: unknown): value is CalendarEventOrigin {
    return typeof value === 'string' && calendarEventOrigins.includes(value as CalendarEventOrigin);
}
