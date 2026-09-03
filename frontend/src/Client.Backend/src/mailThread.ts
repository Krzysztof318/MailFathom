// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { parseTimelineEntry, type MailTimelineEntry } from './mailTimeline';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// One conversation, read across every folder and every account the owner holds. It is the one mail read that names no
// folder at all: the question is in the inbox, the answer is in the sent folder, and a forwarded copy is somewhere else
// again, so the route names the conversation and the deployment decides what of it this caller may see.
//
// Everything outside `messages` describes the whole conversation rather than the page in hand, which is what lets a
// screen draw a thread header from the first page and keep it accurate without holding the rest. The participants are
// the clearest case: they are authors of the whole conversation, so a client deriving them from the messages it holds
// would be paging a conversation in order to draw its header.
//
// A message arrives as the mail list route's own row, field for field, and is parsed by that module's own parser rather
// than by a second one here. What a row does not carry is the message: the whole of one, quoted history included, is a
// request of its own naming the identity the row already carries.

/** The route one page of one conversation is served at, relative to the client prefix. */
export function mailThreadRoute(threadId: string): string {
    return `/threads/${encodeURIComponent(threadId)}`;
}

/** One message of a conversation, and where it sits in the conversation's order. */
export interface MailThreadMessage {
    /** The zero-based place the message holds in the conversation's order. */
    readonly position: number;

    /**
     * The message this one answers among the ones shown, or `null` where it is a root of what is shown.
     *
     * A message whose parent sits in a folder an operator withheld arrives as a root naming nothing, so the withheld
     * message is not disclosed by the gap it would otherwise leave.
     */
    readonly answeredId: string | null;

    /** The message itself, in the same shape a list row carries. Its `preview` is what this message added. */
    readonly email: MailTimelineEntry;
}

/** Somebody who has written in the conversation, and how much of it is theirs. */
export interface MailThreadParticipant {
    readonly address: string;
    readonly displayName: string | null;
    readonly messageCount: number;
}

/** One page of one conversation, beside what is true of the whole of it. */
export interface MailThreadPage {
    readonly threadId: string;

    /** The page's messages, in the conversation's own order. */
    readonly messages: readonly MailThreadMessage[];

    /** Everybody who wrote in the conversation, in the order they first wrote in it. */
    readonly participants: readonly MailThreadParticipant[];

    /** How many messages the conversation holds of those this caller may see. */
    readonly messageCount: number;

    /** Whether the conversation runs past what one read assembles at all. */
    readonly moreMessagesNotAssembled: boolean;

    /** Whether the conversation has authors the participant list does not name. */
    readonly moreParticipantsNotNamed: boolean;

    /** The cursor the following page is asked with, or `null` at the end of the conversation. */
    readonly nextCursor: string | null;

    /** How many messages the read ran under, which is what the request asked for. */
    readonly pageSize: number;
}

/**
 * The largest page this surface serves, which is the deployment's bound rather than a preference.
 *
 * It is the same bound the mail list route holds, because one page size governs every mailbox query the service
 * answers — stated here rather than borrowed, so each of the two contracts this client speaks says its own size.
 * A screen asking for more would be refused with a `400`, so this is also what every read below asks for: a
 * conversation is read whole wherever one page holds it, and paged where it does not.
 */
export const longestThreadPage = 100;

// The most of one page this client reads. A page is at most a hundred rows of the shape the mail list answers with,
// beside the conversation's authors — who are bounded by the messages one read assembles rather than by the page. That
// puts a full answer well inside this, and this well inside the transport's own backstop.
const longestThreadAnswer = 512 * 1024;

// What one answer may carry before the page is refused unread. Each is far above anything the service composes and
// exists for the answer that is not a page at all — checked while the collections are walked rather than after.
const longestIdentity = 256;
const longestCursor = 4_096;
const longestText = 4_096;

// The service assembles at most five hundred messages of a conversation, so it names at most that many authors. This is
// above that and bounds the walk rather than trusting the answer to stop.
const mostParticipants = 1_024;

/**
 * Reads one page of one of the owner's conversations, answering an expected failure as a value rather than by throwing.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param threadId The conversation to read, as a message row published it.
 * @param cursor The cursor a previous page answered with, or `null` for the beginning of the conversation.
 * @returns The page, or why it never arrived.
 */
export function readMailThread(
    session: ClientSession,
    transport: MailFathomTransport,
    threadId: string,
    cursor: string | null,
): Promise<ClientResult<MailThreadPage>> {
    return spanned('GET /threads/{threadId}', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailThreadRoute(threadId)) + threadQueryString(cursor),
            headers: headersFor(session),
            longestAnswer: longestThreadAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const page = parsePage(response.body);

        return page === null ? failed('unreadable', response.status) : read(page);
    });
}

/** The query string one page is asked with: the size every read here runs under, and where in the conversation it starts. */
export function threadQueryString(cursor: string | null): string {
    const asked = [`pageSize=${String(longestThreadPage)}`];

    if (cursor !== null) {
        asked.push(`cursor=${encodeURIComponent(cursor)}`);
    }

    return `?${asked.join('&')}`;
}

// The page is held against what was asked for as well as against its own shape: a deployment answering with more
// messages than the request admits is one this client refuses to render rather than one it draws, which is the bound
// the root instructions place at every remote boundary.
function parsePage(body: string): MailThreadPage | null {
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

    const threadId = record['threadId'];
    const messageCount = record['messageCount'];
    const moreMessagesNotAssembled = record['moreMessagesNotAssembled'];
    const moreParticipantsNotNamed = record['moreParticipantsNotNamed'];
    const nextCursor = record['nextCursor'] ?? null;
    const pageSize = record['pageSize'];

    if (!isIdentity(threadId) || !isCount(messageCount)) {
        return null;
    }

    if (typeof moreMessagesNotAssembled !== 'boolean' || typeof moreParticipantsNotNamed !== 'boolean') {
        return null;
    }

    if (!isCursor(nextCursor)) {
        return null;
    }

    if (!isCount(pageSize) || pageSize < 1 || pageSize > longestThreadPage) {
        return null;
    }

    const messages = parseMessages(record['messages'], pageSize);
    const participants = parseParticipants(record['participants']);

    if (messages === null || participants === null) {
        return null;
    }

    return {
        threadId,
        messages,
        participants,
        messageCount,
        moreMessagesNotAssembled,
        moreParticipantsNotNamed,
        nextCursor,
        pageSize,
    };
}

function parseMessages(value: unknown, asked: number): readonly MailThreadMessage[] | null {
    if (!Array.isArray(value) || value.length > asked) {
        return null;
    }

    const messages: MailThreadMessage[] = [];
    for (const entry of value) {
        const message = parseMessage(entry);
        if (message === null) {
            return null;
        }

        messages.push(message);
    }

    return messages;
}

function parseMessage(value: unknown): MailThreadMessage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const position = record['position'];
    const answeredId = record['answeredId'] ?? null;
    const email = parseTimelineEntry(record['email']);

    if (!isCount(position) || email === null) {
        return null;
    }

    if (answeredId !== null && !isIdentity(answeredId)) {
        return null;
    }

    return { position, answeredId, email };
}

function parseParticipants(value: unknown): readonly MailThreadParticipant[] | null {
    if (!Array.isArray(value) || value.length > mostParticipants) {
        return null;
    }

    const participants: MailThreadParticipant[] = [];
    for (const entry of value) {
        const participant = parseParticipant(entry);
        if (participant === null) {
            return null;
        }

        participants.push(participant);
    }

    return participants;
}

function parseParticipant(value: unknown): MailThreadParticipant | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const address = record['address'];
    const displayName = record['displayName'] ?? null;
    const messageCount = record['messageCount'];

    if (typeof address !== 'string' || address.length === 0 || address.length > longestText) {
        return null;
    }

    if (displayName !== null && (typeof displayName !== 'string' || displayName.length > longestText)) {
        return null;
    }

    return isCount(messageCount) ? { address, displayName, messageCount } : null;
}

// A name the deployment assigned, which is what the conversation and each of its messages are reached by.
function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}

function isCursor(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && value.length > 0 && value.length <= longestCursor);
}

// A count is a whole number, so a fraction, a negative, and a value past what arithmetic here stays exact for are each
// an answer no conversation produced.
function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
