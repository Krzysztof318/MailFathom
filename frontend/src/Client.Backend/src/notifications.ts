// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What happened to the signed-in person while nobody was looking at the screen, and the two ways of marking it read.
// Four routes over one person's own working state, and nothing here streams: the deployment serves no events for this,
// so a client asks for the count on an interval and for the list when it has somewhere to draw it.
//
// The count stands on its own rather than being counted out of a page, which is the service's own division: the badge
// is the answer asked for most, and deriving it would cost a screenful of rows every time it is asked.
//
// Nothing in this module says what a notification looks like. The kind, the target, and the instant are what the
// service decided; which symbol a kind is drawn with, what a screen calls it, and how long ago it was are the
// application's, exactly as they are for every other reading here.

/** The route one page of the acting person's notifications is read from, relative to the client prefix. */
export const notificationsRoute = '/notifications';

/** The route the acting person's unread count is read from, on its own. */
export const unreadNotificationCountRoute = `${notificationsRoute}/unread-count`;

/** The route every one of the acting person's notifications is marked read on. */
export const markAllNotificationsReadRoute = `${notificationsRoute}/read`;

/**
 * The most notifications one page may hold, which is the service's own ceiling.
 *
 * A larger request is served this many rather than refused, so it is also what an answer is refused above: a page
 * carrying more than was asked for is not an answer this deployment produced.
 */
export const longestNotificationPage = 100;

// The most of one page this client reads. A row carries a title, a bounded body, and a source, so a full page is on
// the order of tens of kilobytes; this is well above that and well below the transport's backstop, which is written
// for an address nobody has trusted yet rather than for a route the client has already signed in to.
const longestNotificationsAnswer = 128 * 1024;

/** The most of an answer to any of the three routes that report a count or one row, which each compose to a line. */
const longestNotificationAnswer = 1_024;

// Every field of a row is bounded as the row is walked rather than left to the page's own ceiling, which is what
// `mailTimeline.ts` does beside this and for the same reason: a page cap stops a mailbox arriving in one answer and
// stops nothing about one row spending the whole budget on a title. What the two numbers are sized for is what a row
// draws — an identifier a deployment issued, and a line of prose a person reads on a row two lines tall.
const longestNotificationIdentity = 256;
const longestNotificationText = 4_096;

/** What part of MailFathom a notification is about, which is what a row is drawn by. */
export type NotificationKind = 'Mail' | 'Calendar' | 'Case' | 'Task' | 'System';

/** A screen a notification leads to, where it leads to a screen rather than to a record. */
export type NotificationScreen = 'Mail' | 'Settings';

/**
 * Where opening a notification leads.
 *
 * Three closed shapes rather than two optional values, because which of them a producer chose is what a reader
 * switches on: a notification that leads nowhere is a different thing from one whose record this client cannot name.
 */
export type NotificationTarget =
    | { readonly kind: 'Nothing' }
    | { readonly kind: 'Message'; readonly storedEmailId: string }
    | { readonly kind: 'Screen'; readonly screen: NotificationScreen };

/** One thing that happened to a person, as a row draws it. */
export interface ClientNotification {
    /** What addresses the notification, and what the read-state route names it by. */
    readonly id: string;

    readonly kind: NotificationKind;
    readonly title: string;
    readonly body: string;

    /** What the source line names beyond the kind, or `null` where the kind is the whole of it. */
    readonly source: string | null;

    readonly target: NotificationTarget;

    /** When the thing it describes happened, as the instant the service sent it. */
    readonly occurredAt: string;

    readonly read: boolean;
}

/** One page of what happened, newest first. */
export interface NotificationPage {
    readonly notifications: readonly ClientNotification[];

    /** The boundary the following page would be asked with, or `null` at the end of the centre. */
    readonly nextCursor: string | null;
}

/** What one notification's read state now is, and what that leaves on the bell. */
export interface NotificationReadState {
    readonly id: string;
    readonly read: boolean;
    readonly unreadCount: number;
}

/** What marking the whole centre read changed. */
export interface MarkedNotifications {
    readonly markedRead: number;
    readonly unreadCount: number;
}

const kinds: readonly NotificationKind[] = ['Mail', 'Calendar', 'Case', 'Task', 'System'];
const screens: readonly NotificationScreen[] = ['Mail', 'Settings'];

/** Reads one page of the signed-in person's notifications, newest first. */
export function readNotifications(
    session: ClientSession,
    transport: MailFathomTransport,
    pageSize: number,
): Promise<ClientResult<NotificationPage>> {
    return spanned(`GET ${notificationsRoute}`, async () => {
        const asked = Math.min(pageSize, longestNotificationPage);
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, notificationsRoute)}?pageSize=${String(asked)}`,
            headers: headersFor(session),
            longestAnswer: longestNotificationsAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const page = parsePage(response.body, asked);

        return page === null ? failed('unreadable', response.status) : read(page);
    });
}

/** Reads how many of the signed-in person's notifications stand unread, which is the whole of what the bell draws. */
export function readUnreadNotificationCount(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<number>> {
    return spanned(`GET ${unreadNotificationCountRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, unreadNotificationCountRoute),
            headers: headersFor(session),
            longestAnswer: longestNotificationAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const count = countIn(response.body, 'unreadCount');

        return count === null ? failed('unreadable', response.status) : read(count);
    });
}

/**
 * Puts one notification into the read state stated, and answers with what a client redraws the row and the badge from.
 *
 * @param stands Whether it is to stand read.
 */
export function setNotificationRead(
    session: ClientSession,
    transport: MailFathomTransport,
    notificationId: string,
    stands: boolean,
): Promise<ClientResult<NotificationReadState>> {
    return spanned(`POST ${notificationsRoute}/read-state`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: `${routeFor(session, notificationsRoute)}/${encodeURIComponent(notificationId)}/read-state`,
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({ read: stands }),
            longestAnswer: longestNotificationAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const state = parseReadState(response.body);

        return state === null ? failed('unreadable', response.status) : read(state);
    });
}

/** Marks every one of the signed-in person's unread notifications read, in one request. */
export function markAllNotificationsRead(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<MarkedNotifications>> {
    return spanned(`POST ${markAllNotificationsReadRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, markAllNotificationsReadRoute),
            headers: headersFor(session),
            longestAnswer: longestNotificationAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const record = bodyRecord(response.body);
        const markedRead = record === null ? null : countField(record['markedRead']);
        const unreadCount = record === null ? null : countField(record['unreadCount']);

        return markedRead === null || unreadCount === null
            ? failed('unreadable', response.status)
            : read({ markedRead, unreadCount });
    });
}

function parsePage(body: string, asked: number): NotificationPage | null {
    const record = bodyRecord(body);

    if (record === null) {
        return null;
    }

    const entries = record['notifications'];
    const nextCursor = record['nextCursor'] ?? null;

    if (!Array.isArray(entries) || entries.length > asked) {
        return null;
    }

    if (nextCursor !== null && typeof nextCursor !== 'string') {
        return null;
    }

    const notifications: ClientNotification[] = [];
    for (const entry of entries) {
        const notification = parseNotification(entry);

        if (notification === null) {
            return null;
        }

        notifications.push(notification);
    }

    return { notifications, nextCursor };
}

function parseNotification(value: unknown): ClientNotification | null {
    const record = asRecord(value);

    if (record === null) {
        return null;
    }

    const id = record['id'];
    const kind = record['kind'];
    const title = record['title'];
    const body = record['body'];
    const source = record['source'] ?? null;
    const occurredAt = record['occurredAt'];
    const isRead = record['read'];

    if (!isNotificationIdentity(id) || !isNotificationText(title) || !isNotificationText(body)) {
        return null;
    }

    if (!isNotificationIdentity(occurredAt) || typeof isRead !== 'boolean' || !isKind(kind)) {
        return null;
    }

    if (source !== null && !isNotificationText(source)) {
        return null;
    }

    const target = parseTarget(record['target']);

    return target === null ? null : { id, kind, title, body, source, target, occurredAt, read: isRead };
}

function parseTarget(value: unknown): NotificationTarget | null {
    const record = asRecord(value);

    if (record === null) {
        return null;
    }

    const storedEmailId = record['messageId'] ?? null;
    const screen = record['screen'] ?? null;

    switch (record['kind']) {
        case 'Nothing':
            return { kind: 'Nothing' };
        case 'Message':
            return isNotificationIdentity(storedEmailId) ? { kind: 'Message', storedEmailId } : null;
        case 'Screen':
            return isScreen(screen) ? { kind: 'Screen', screen } : null;
        default:
            return null;
    }
}

function parseReadState(body: string): NotificationReadState | null {
    const record = bodyRecord(body);

    if (record === null) {
        return null;
    }

    const id = record['id'];
    const isRead = record['read'];
    const unreadCount = countField(record['unreadCount']);

    if (typeof id !== 'string' || id === '' || typeof isRead !== 'boolean' || unreadCount === null) {
        return null;
    }

    return { id, read: isRead, unreadCount };
}

function countIn(body: string, field: string): number | null {
    const record = bodyRecord(body);

    return record === null ? null : countField(record[field]);
}

function bodyRecord(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

// A count is a whole number of notifications, so a fraction, a negative, and a value past what arithmetic here stays
// exact for are each an answer this deployment did not produce.
function countField(value: unknown): number | null {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

// An identifier a deployment issued, and an instant it stated, are each a short string this client only ever passes
// back or hands to `Intl` — so the same bound answers for both, and an empty one is no identifier at all.
function isNotificationIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestNotificationIdentity;
}

// A line somebody reads. Empty is permitted here and nowhere above: a notification with no body is a title on its own,
// which is a row this client draws rather than an answer it refuses.
function isNotificationText(value: unknown): value is string {
    return typeof value === 'string' && value.length <= longestNotificationText;
}

function isKind(value: unknown): value is NotificationKind {
    return typeof value === 'string' && kinds.includes(value as NotificationKind);
}

function isScreen(value: unknown): value is NotificationScreen {
    return typeof value === 'string' && screens.includes(value as NotificationScreen);
}
