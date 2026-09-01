// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type MailFathomTransport } from './transport';

// One page of the owner's mail, keyset-paged in both directions. The route is the one a mail screen spends its time
// in, so what this package owes it is a request that says exactly which list is being read and a parser that refuses a
// page rather than handing a screen a row with a hole in it.
//
// The two cursors are opaque and holdable: each names a row together with the list it was read under, and nothing on
// the deployment remembers one. That is what lets a screen keep one while it is closed and continue from it — and it
// is why the filters and the order are part of the request rather than something the deployment recalls, because a
// cursor presented under different ones is refused rather than silently reinterpreted.

/** The route one page of the owner's mail is served at, relative to the client prefix. */
export const mailTimelineRoute = '/emails';

/** Which end of the received order leads. */
export type MailTimelineOrder = 'newestFirst' | 'oldestFirst';

/** Whether the page asked for lies after the cursor or before it. */
export type MailTimelinePageDirection = 'forward' | 'backward';

/**
 * Which list is being read, in full.
 *
 * Every field is stated rather than defaulted, because the whole of it is what a cursor was issued under: a request
 * that left one out would be asking for a different list than the one the cursor names, which the deployment refuses.
 */
export interface MailTimelineQuery {
    /** The account to draw from, or `null` for every account the owner owns. */
    readonly account: string | null;

    /** The folder to draw from, by its alias or as `role:Inbox`, or `null` for every folder. */
    readonly folder: string | null;

    /** Whether the junk folder takes part, which it does not unless the request asks. */
    readonly includeJunk: boolean;

    /** Keep only unread mail, only read mail, or `null` for both. */
    readonly unread: boolean | null;

    /** Keep only flagged mail, only unflagged mail, or `null` for both. */
    readonly flagged: boolean | null;

    /** Keep only mail with attachments, only mail without, or `null` for both. */
    readonly hasAttachments: boolean | null;

    readonly order: MailTimelineOrder;
    readonly direction: MailTimelinePageDirection;

    /** How many rows the page may hold, between one and {@link longestTimelinePage}. */
    readonly pageSize: number;

    /** The cursor a previous page answered with, or `null` for the leading end of the list. */
    readonly cursor: string | null;
}

/** One row of the list, carrying what a screen draws and nothing it does not. */
export interface MailTimelineEntry {
    /** The stable local identity of the message, which is what a row is keyed by and what every later request names it by. */
    readonly id: string;

    readonly account: string;
    readonly folder: string;
    readonly threadId: string | null;
    readonly subject: string | null;

    /** When the last receiving hop recorded the message, which is what the list is ordered by. */
    readonly receivedAt: string | null;
    readonly sentAt: string | null;

    readonly senderAddress: string | null;
    readonly senderDisplayName: string | null;

    /** The `To` addresses in header order, which is what a sent-mail row draws instead of a sender. */
    readonly toAddresses: readonly string[];

    readonly unread: boolean;
    readonly flagged: boolean;
    readonly answered: boolean;
    readonly hasAttachments: boolean;
    readonly attachmentCount: number;
    readonly sizeOctets: number;

    /** The opening of the message's own text, or `null` for a message this deployment has stored but not extracted. */
    readonly preview: string | null;
}

/** One page of the list, and the two cursors the pages either side of it are asked with. */
export interface MailTimelinePage {
    readonly emails: readonly MailTimelineEntry[];

    /** The cursor the following page is asked with, or `null` at the end of the list. */
    readonly nextCursor: string | null;

    /** The cursor the preceding page is asked with, or `null` at the beginning of the list. */
    readonly previousCursor: string | null;

    /** How many rows the read ran under, which is what the request asked for. */
    readonly pageSize: number;
}

/**
 * The largest page this surface serves, which is the deployment's bound rather than a preference.
 *
 * A screen asking for more would be refused with a `400`, so the client holds the same number and asks within it.
 */
export const longestTimelinePage = 100;

// The most of one page this client reads. A row carries a subject, a bounded preview, and a handful of addresses, so a
// full page is on the order of a hundred kilobytes; this is well above that and well below the transport's backstop,
// which is written for an address nobody has trusted yet rather than for a route the client has already signed in to.
const longestTimelineAnswer = 256 * 1024;

// What one row may carry before the page is refused unread. Each is far above anything a mail server produces and
// exists for the answer that is not a page at all — checked while the rows are walked rather than after.
const longestIdentity = 256;
const longestCursor = 4_096;
const longestText = 4_096;
const mostRecipients = 256;

/**
 * Reads one page of the signed-in owner's mail, answering an expected failure as a value rather than by throwing.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param query Which list is being read, and where in it.
 * @returns The page, or why it never arrived.
 */
export async function readMailTimeline(
    session: ClientSession,
    transport: MailFathomTransport,
    query: MailTimelineQuery,
): Promise<ClientResult<MailTimelinePage>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, mailTimelineRoute) + timelineQueryString(query),
        headers: headersFor(session),
        longestAnswer: longestTimelineAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const page = parsePage(response.body, query.pageSize);

    return page === null ? failed('unreadable', response.status) : read(page);
}

/**
 * The query string one page is asked with, including the two values the route would default anyway.
 *
 * They are written out because the request is the statement of which list the cursor belongs to: a screen that stopped
 * naming its order would be asking for a different list with a cursor issued for the one before it, and the difference
 * between the two spellings would only appear once somebody had scrolled.
 */
export function timelineQueryString(query: MailTimelineQuery): string {
    const asked: string[] = [
        `sort=receivedAt`,
        `order=${query.order}`,
        `direction=${query.direction}`,
        `pageSize=${String(query.pageSize)}`,
    ];

    if (query.account !== null) {
        asked.push(`account=${encodeURIComponent(query.account)}`);
    }

    if (query.folder !== null) {
        asked.push(`folder=${encodeURIComponent(query.folder)}`);
    }

    if (query.includeJunk) {
        asked.push('includeJunk=true');
    }

    for (const [name, wanted] of [
        ['unread', query.unread],
        ['flagged', query.flagged],
        ['hasAttachments', query.hasAttachments],
    ] as const) {
        if (wanted !== null) {
            asked.push(`${name}=${wanted ? 'true' : 'false'}`);
        }
    }

    if (query.cursor !== null) {
        asked.push(`cursor=${encodeURIComponent(query.cursor)}`);
    }

    return `?${asked.join('&')}`;
}

// The page is held against what was asked for as well as against its own shape: a deployment answering with more rows
// than the request admits is one this client refuses to render rather than one it draws, which is the bound the root
// instructions place at every remote boundary.
function parsePage(body: string, asked: number): MailTimelinePage | null {
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

    const rows = record['emails'];
    const pageSize = record['pageSize'];
    const nextCursor = record['nextCursor'] ?? null;
    const previousCursor = record['previousCursor'] ?? null;

    if (!Array.isArray(rows) || rows.length > asked) {
        return null;
    }

    if (typeof pageSize !== 'number' || !Number.isSafeInteger(pageSize) || pageSize < 1) {
        return null;
    }

    if (!isCursor(nextCursor) || !isCursor(previousCursor)) {
        return null;
    }

    const emails: MailTimelineEntry[] = [];
    for (const row of rows) {
        const entry = parseEntry(row);
        if (entry === null) {
            return null;
        }

        emails.push(entry);
    }

    return { emails, nextCursor, previousCursor, pageSize };
}

function parseEntry(value: unknown): MailTimelineEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const account = record['account'];
    const folder = record['folder'];
    const threadId = record['threadId'] ?? null;
    const subject = record['subject'] ?? null;
    const receivedAt = record['receivedAt'] ?? null;
    const sentAt = record['sentAt'] ?? null;
    const senderAddress = record['senderAddress'] ?? null;
    const senderDisplayName = record['senderDisplayName'] ?? null;
    const preview = record['preview'] ?? null;

    if (!isIdentity(id) || !isIdentity(account) || !isIdentity(folder)) {
        return null;
    }

    if (threadId !== null && !isIdentity(threadId)) {
        return null;
    }

    if (!isOptionalText(subject) || !isOptionalText(senderAddress) || !isOptionalText(senderDisplayName)) {
        return null;
    }

    if (!isOptionalText(preview) || !isOptionalText(receivedAt) || !isOptionalText(sentAt)) {
        return null;
    }

    const toAddresses = parseRecipients(record['toAddresses']);
    if (toAddresses === null) {
        return null;
    }

    const unread = record['unread'];
    const flagged = record['flagged'];
    const answered = record['answered'];
    const hasAttachments = record['hasAttachments'];

    if (
        typeof unread !== 'boolean' ||
        typeof flagged !== 'boolean' ||
        typeof answered !== 'boolean' ||
        typeof hasAttachments !== 'boolean'
    ) {
        return null;
    }

    const attachmentCount = record['attachmentCount'];
    const sizeOctets = record['sizeOctets'];

    if (!isCount(attachmentCount) || !isCount(sizeOctets)) {
        return null;
    }

    return {
        id,
        account,
        folder,
        threadId,
        subject,
        receivedAt,
        sentAt,
        senderAddress,
        senderDisplayName,
        toAddresses,
        unread,
        flagged,
        answered,
        hasAttachments,
        attachmentCount,
        sizeOctets,
        preview,
    };
}

function parseRecipients(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostRecipients) {
        return null;
    }

    const addresses: string[] = [];
    for (const address of value) {
        if (typeof address !== 'string' || address.length > longestText) {
            return null;
        }

        addresses.push(address);
    }

    return addresses;
}

// A name the deployment assigned, which is what a row is keyed by and what a later request names the message with.
function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && value.length <= longestText);
}

function isCursor(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && value.length > 0 && value.length <= longestCursor);
}

// A count is a whole number, so a fraction, a negative, and a value past what arithmetic here stays exact for are each
// an answer no mailbox produced.
function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
