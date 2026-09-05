// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { mostReconnectionAttempts, reconnectionDelay } from './reconnection';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What a deployment says changed while somebody has this client open, and the connection it says it over.
//
// A signal is an instruction to look again rather than something to keep. Nothing here writes to a store, nothing here
// carries mail, and no screen draws a signal: the application decides what to re-read over the routes it already reads,
// which is what keeps a client with no channel behaving exactly as one that never had it.
//
// This package owns the connection — when it opens, what it presents, what a payload has to be before anyone is told
// about it, and how long it waits before opening again — and owns none of the socket. `lib` here names the standard
// library alone, so `WebSocket` is as undeclared as `fetch` is, and the channel arrives as a function the application
// supplies exactly as the transport does.

/** The route a connection ticket is minted on, relative to the client prefix. */
export const signalTicketRoute = '/signals/ticket';

/** The route the hub answers on, relative to the client prefix. */
export const signalHubRoute = '/signals';

/** The query parameter a connection presents its ticket under, which is the hub's own name for it. */
export const signalTicketParameter = 'access_token';

/** The name of the one method the deployment invokes on a connection. */
export const signalMethod = 'signal';

/** The most of an answer to the minting route this client reads, which composes to a ticket and an instant. */
const longestTicketAnswer = 1_024;

// Every field of a payload is bounded as it is walked, for the reason every other reading here bounds its own: what
// arrives is untrusted input however it got onto the socket, and a connection that has been open for hours is the
// least supervised place a value enters this client. The two numbers are what the deployment's own vocabulary
// composes to — an alias or an identifier it issued, and a notification's own two lines.
const longestSignalIdentity = 256;
const longestSignalText = 4_096;

/** The most stored identities one signal names, which is the deployment's own bound on the same list. */
export const mostNamedSignalEmails = 100;

/** What part of MailFathom a raised notification was about, which is the same closed set the record routes publish. */
export type SignalNotificationKind = 'Mail' | 'Calendar' | 'Case' | 'Task' | 'System';

/**
 * One statement that something changed.
 *
 * Five closed shapes rather than one record of optional fields, because what a reader does with a signal is decided
 * entirely by which of them arrived: a folder set that moved and a message that changed are read again from different
 * routes, and a shape carrying both would leave every reader checking which fields happened to be present.
 */
export type ClientSignal =
    | { readonly kind: 'mail.arrived'; readonly account: string; readonly folder: string; readonly count: number }
    | {
          readonly kind: 'mail.changed';
          readonly account: string;
          readonly folder: string;
          readonly emails: readonly string[];
      }
    | { readonly kind: 'folders.changed'; readonly account: string }
    | {
          readonly kind: 'notification.raised';
          readonly notificationKind: SignalNotificationKind;
          readonly headline: string;
          readonly secondLine: string;
          readonly unreadCount: number;
      }
    | { readonly kind: 'account.state'; readonly account: string };

/** The ticket one connection is opened against, and when presenting it stops working. */
export interface SignalTicket {
    readonly ticket: string;
    readonly expiresAt: string;
}

/** What the application is asked for when a connection is to be opened. */
export interface SignalChannelOpening {
    /** The absolute address of the hub, with the ticket already on it. */
    readonly url: string;

    /** The ticket on its own, for a channel that presents it some other way than on the address. */
    readonly ticket: string;

    /** Called once per payload the deployment sent, with whatever arrived and before anything has read it. */
    readonly arrived: (payload: unknown) => void;

    /** Called once when the connection ends for any reason other than this client closing it. */
    readonly dropped: () => void;
}

/** One open connection, as the application hands it back. */
export interface SignalChannelHandle {
    close: () => Promise<void>;
}

/**
 * How a connection is opened.
 *
 * The application supplies it for the reason it supplies the transport: this package declares no DOM, so it names no
 * socket. A channel that could not open rejects or throws, which this package reads as a deployment that is not
 * serving one — never as an error to report.
 */
export type MailFathomSignalChannel = (opening: SignalChannelOpening) => Promise<SignalChannelHandle>;

/** What the stream needs from the host to wait and to spread its waiting, neither of which is in this package's `lib`. */
export interface SignalStreamSchedule {
    /** Resolves after roughly that many milliseconds. */
    readonly wait: (milliseconds: number) => Promise<void>;

    /** Draws a value in `[0, 1)`, which is what keeps a fleet of clients from reopening in step. */
    readonly draw: () => number;
}

/** An open subscription to what a deployment says changed. */
export interface SignalStream {
    /** Stops it, closing whatever is open and reopening nothing. */
    close: () => Promise<void>;
}

/**
 * Mints the ticket one connection is opened against.
 *
 * @returns The ticket, or why the deployment did not mint one.
 */
export function readSignalTicket(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<SignalTicket>> {
    return spanned(`POST ${signalTicketRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, signalTicketRoute),
            headers: headersFor(session),
            longestAnswer: longestTicketAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const minted = parseTicket(response.body);

        return minted === null ? failed('unreadable', response.status) : read(minted);
    });
}

/**
 * Reads one payload the deployment sent, refusing anything that is not one of the five statements.
 *
 * @returns The signal, or `null` where the payload is not one this client acts on.
 */
export function parseClientSignal(payload: unknown): ClientSignal | null {
    const record = asRecord(payload);

    if (record === null) {
        return null;
    }

    switch (record['kind']) {
        case 'mail.arrived':
            return parseArrival(record);
        case 'mail.changed':
            return parseChange(record);
        case 'folders.changed':
            return isIdentity(record['account']) ? { kind: 'folders.changed', account: record['account'] } : null;
        case 'notification.raised':
            return parseRaisedNotification(record);
        case 'account.state':
            return isIdentity(record['account']) ? { kind: 'account.state', account: record['account'] } : null;
        default:
            return null;
    }
}

/**
 * Opens a connection and keeps one open, telling the caller what the deployment says changed.
 *
 * A connection is opened by minting a ticket and handing it to the channel, and a ticket opens exactly one — so every
 * reopening mints another. Both halves are retried on the same bounded, spread schedule the shell reaches a lost
 * deployment on, and after `mostReconnectionAttempts` the stream stops trying: a client whose channel never came back
 * reads on its own interval, which is what it does when a deployment serves no channel at all.
 *
 * **Nothing here reports a failure.** A deployment that serves no hub, a proxy that will not pass the upgrade, and a
 * connection that dropped are all the same thing to a person looking at the screen — a client reading on its interval
 * — and saying so would be a client complaining about an optimization it never promised.
 *
 * @param told Called once per statement, after the payload has been read as one of the five.
 * @returns The subscription, which the caller closes when the person signs out or the deployment changes.
 */
export function openSignalStream(
    session: ClientSession,
    transport: MailFathomTransport,
    channel: MailFathomSignalChannel,
    told: (signal: ClientSignal) => void,
    schedule: SignalStreamSchedule,
): SignalStream {
    let open: SignalChannelHandle | null = null;
    let closed = false;

    // Asked through a function rather than read off the variable, so nothing decides at the first check that it can
    // never be true at the second: what changes it is the caller closing the stream while an attempt is in flight.
    const hasClosed = (): boolean => closed;

    const keepOpen = async (): Promise<void> => {
        let refusals = 0;

        while (!hasClosed()) {
            const outcome = await openOnce();

            if (hasClosed()) {
                return;
            }

            // A connection that stood and then dropped is not the next failed attempt in a row: the schedule measures
            // how long a deployment has been unreachable, and one that answered is reachable. It is still waited out
            // before another is opened, so a deployment closing every connection at once costs one attempt a second
            // rather than a loop.
            refusals = outcome === 'opened' ? 0 : refusals + 1;

            if (refusals > mostReconnectionAttempts) {
                return;
            }

            await schedule.wait(reconnectionDelay(refusals, schedule.draw()));
        }
    };

    const openOnce = async (): Promise<'opened' | 'refused'> => {
        const minted = await readSignalTicket(session, transport);

        if (minted.outcome === 'failed' || hasClosed()) {
            return 'refused';
        }

        const ended = deferred();

        try {
            open = await channel({
                url: hubAddressFor(session, minted.value.ticket),
                ticket: minted.value.ticket,
                arrived: (payload) => {
                    const signal = parseClientSignal(payload);

                    if (signal !== null) {
                        told(signal);
                    }
                },
                dropped: ended.settle,
            });
        } catch {
            return 'refused';
        }

        if (hasClosed()) {
            await closeQuietly(open);

            return 'opened';
        }

        await ended.reached;
        open = null;

        return 'opened';
    };

    void keepOpen();

    return {
        close: async () => {
            closed = true;

            const closing = open;
            open = null;

            await closeQuietly(closing);
        },
    };
}

/** The address a connection is opened at, with the ticket on it. */
export function hubAddressFor(session: ClientSession, ticket: string): string {
    return `${routeFor(session, signalHubRoute)}?${signalTicketParameter}=${encodeURIComponent(ticket)}`;
}

function deferred(): { readonly reached: Promise<void>; readonly settle: () => void } {
    let settle = (): void => undefined;
    const reached = new Promise<void>((resolve) => {
        settle = resolve;
    });

    return { reached, settle };
}

async function closeQuietly(handle: SignalChannelHandle | null): Promise<void> {
    if (handle === null) {
        return;
    }

    try {
        await handle.close();
    } catch {
        // A channel that could not be closed cleanly is a channel that is going away regardless, and the person
        // signing out is not waiting to hear about a socket.
    }
}

function parseTicket(body: string): SignalTicket | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);

    if (record === null) {
        return null;
    }

    const ticket = record['ticket'];
    const expiresAt = record['expiresAt'];

    return isIdentity(ticket) && isIdentity(expiresAt) ? { ticket, expiresAt } : null;
}

function parseArrival(record: Readonly<Record<string, unknown>>): ClientSignal | null {
    const account = record['account'];
    const folder = record['folder'];
    const count = record['count'];

    if (!isIdentity(account) || !isIdentity(folder) || !isCount(count) || count <= 0) {
        return null;
    }

    return { kind: 'mail.arrived', account, folder, count };
}

function parseChange(record: Readonly<Record<string, unknown>>): ClientSignal | null {
    const account = record['account'];
    const folder = record['folder'];
    const named = record['emails'];

    if (!isIdentity(account) || !isIdentity(folder) || !Array.isArray(named)) {
        return null;
    }

    if (named.length > mostNamedSignalEmails || !named.every(isIdentity)) {
        return null;
    }

    return { kind: 'mail.changed', account, folder, emails: [...named] };
}

function parseRaisedNotification(record: Readonly<Record<string, unknown>>): ClientSignal | null {
    const notificationKind = record['notificationKind'];
    const headline = record['headline'];
    const secondLine = record['secondLine'];
    const unreadCount = record['count'];

    if (!isNotificationKind(notificationKind) || !isText(headline) || !isText(secondLine)) {
        return null;
    }

    return isCount(unreadCount)
        ? { kind: 'notification.raised', notificationKind, headline, secondLine, unreadCount }
        : null;
}

function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestSignalIdentity;
}

function isText(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestSignalText;
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isNotificationKind(value: unknown): value is SignalNotificationKind {
    return value === 'Mail' || value === 'Calendar' || value === 'Case' || value === 'Task' || value === 'System';
}
