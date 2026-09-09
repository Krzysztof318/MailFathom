// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, read, type ClientFailureReason, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What a sentence somebody typed turns into, before anything is searched. It is a route of its own rather than a field
// on the search because the two are different acts: reading a sentence produces an interpretation somebody looks at
// and corrects, and searching happens afterwards with whatever survived that. A route that did both would leave the
// interpretation invisible, and an interpretation nobody can see is one nobody can correct.
//
// What this package owes the screen above it is three things. Whether the deployment reads a sentence at all, which is
// what decides whether the field may offer to take one. The reading itself, split into the constraints and the
// criteria and never folded together. And the part of the sentence nothing was made of, which is the difference
// between a person correcting an interpretation and a person doubting their own mailbox.
//
// The sentence goes out in a body rather than in a query string, which is why reading one is a `POST` for something
// that changes nothing: what somebody is looking for in their own mail is the most revealing value this client sends,
// and a query string is the part of a request that reaches an access log by default.

/** The route a sentence is read at, relative to the client prefix. */
export const mailSearchPhrasingRoute = '/emails/search/phrasing';

/** The constraints a sentence states: what a search may return, rather than what it ranks by. */
export interface MailSearchPhraseFilters {
    /** The address the sender must carry, or `null` for any sender. */
    readonly sender: string | null;

    /** The address a recipient must carry, or `null` for any recipient. */
    readonly recipient: string | null;

    /** The first calendar day the search reaches back to, as `yyyy-mm-dd`, or `null` for no start. */
    readonly receivedFrom: string | null;

    /** The last calendar day it reaches, inclusive, as `yyyy-mm-dd`, or `null` for no end. */
    readonly receivedTo: string | null;

    readonly unread: boolean;
    readonly flagged: boolean;
    readonly hasAttachments: boolean;
}

/** What one sentence was read as. */
export interface MailSearchPhraseReading {
    /**
     * Whether a reading happened at all.
     *
     * `false` is the plain word search rather than a failure: it is what a deployment reading no sentence answers, and
     * what this one answers while its provider is unreachable. A screen shows nothing about it and searches the words.
     */
    readonly read: boolean;

    readonly filters: MailSearchPhraseFilters;

    /** What is left to rank by, best first, which orders results and excludes nothing. */
    readonly criteria: readonly string[];

    /** The part of the sentence nothing was made of, or `null` where all of it was read. */
    readonly unaccounted: string | null;
}

/** The reading a sentence gets where nothing read it, which leaves the word search exactly as it was. */
export const phraseNotRead: MailSearchPhraseReading = {
    read: false,
    filters: {
        sender: null,
        recipient: null,
        receivedFrom: null,
        receivedTo: null,
        unread: false,
        flagged: false,
        hasAttachments: false,
    },
    criteria: [],
    unaccounted: null,
};

// An interpretation of one bounded sentence: a handful of short strings and a few flags. Well above what an honest
// answer occupies and far below the transport's own backstop.
const longestPhraseAnswer = 16 * 1024;

// What the deployment bounds each part of a reading by, held here as well so an answer past any of them is refused
// rather than drawn. A criterion is shown to somebody as their own words, so one the length of a paragraph is not a
// reading of their sentence whatever the deployment thinks.
const mostCriteria = 4;
const longestCriterion = 96;
const longestUnaccounted = 256;
const longestAddress = 320;

/**
 * Asks whether this deployment turns a typed sentence into filters.
 *
 * Asked once and held, because it is a property of the deployment rather than of a search: the field's promise has to
 * be right on the first search rather than after one has already failed.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @returns Whether a sentence is read, or why the answer never arrived.
 */
export function readsMailSearchPhrases(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<boolean>> {
    return spanned(`GET ${mailSearchPhrasingRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailSearchPhrasingRoute),
            headers: headersFor(session),
            longestAnswer: longestPhraseAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(reasonForPhraseStatus(response.status), response.status);
        }

        const record = parsed(response.body);
        const reads = record === null ? null : record['readsPhrases'];

        return typeof reads === 'boolean' ? read(reads) : failed('unreadable', response.status);
    });
}

/**
 * Reads one typed sentence into the filters it states and the words it leaves to rank by.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param phrase What was typed.
 * @param askedOn The reader's own calendar day as `yyyy-mm-dd`, which every relative time expression is resolved against.
 * @returns The reading, or why it never arrived.
 */
export function readMailSearchPhrase(
    session: ClientSession,
    transport: MailFathomTransport,
    phrase: string,
    askedOn: string,
): Promise<ClientResult<MailSearchPhraseReading>> {
    return spanned(`POST ${mailSearchPhrasingRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, mailSearchPhrasingRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({ phrase, askedOn }),
            longestAnswer: longestPhraseAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(reasonForPhraseStatus(response.status), response.status);
        }

        const reading = parseReading(response.body);

        return reading === null ? failed('unreadable', response.status) : read(reading);
    });
}

/**
 * The day a calendar control would show the reader, which is what a relative expression is resolved against.
 *
 * Composed from the local parts rather than from an ISO instant, because `toISOString` answers in UTC: somebody
 * searching at eleven at night east of Greenwich would otherwise have "today" resolved to tomorrow.
 *
 * @param at The instant to read the day off, which the caller supplies so nothing here reads a clock.
 * @returns The calendar day as `yyyy-mm-dd`.
 */
export function calendarDayOf(at: Date): string {
    const month = String(at.getMonth() + 1).padStart(2, '0');
    const day = String(at.getDate()).padStart(2, '0');

    return `${String(at.getFullYear()).padStart(4, '0')}-${month}-${day}`;
}

/**
 * The failure a status this read did not expect to succeed stands for.
 *
 * A `429` is the deployment having spent what its operator allows a provider, which is neither a defect in this client
 * nor something a reader repairs — so it is reported as the capability being unavailable, and the screen searches the
 * words as it would on a deployment that reads no sentence at all.
 */
function reasonForPhraseStatus(status: number): ClientFailureReason {
    switch (status) {
        case 400:
            return 'unreadable';
        case 401:
            return 'unauthenticated';
        case 403:
            return 'unauthorized';
        default:
            return 'unavailable';
    }
}

function parsed(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

// Every part is held against the bound the deployment states for it, because each one becomes something a person is
// shown as their own interpretation. An answer past any of them is refused whole rather than trimmed: half a reading
// drawn as a complete one is the silent reinterpretation this screen exists to prevent.
function parseReading(body: string): MailSearchPhraseReading | null {
    const record = parsed(body);
    if (record === null) {
        return null;
    }

    const wasRead = record['read'];
    const unaccounted = record['unaccounted'] ?? null;
    const criteria = record['criteria'];

    if (typeof wasRead !== 'boolean' || !Array.isArray(criteria) || criteria.length > mostCriteria) {
        return null;
    }

    if (unaccounted !== null && (typeof unaccounted !== 'string' || unaccounted.length > longestUnaccounted)) {
        return null;
    }

    if (!criteria.every((criterion) => typeof criterion === 'string' && criterion.length <= longestCriterion)) {
        return null;
    }

    const filters = parseFilters(record['filters']);

    return filters === null ? null : { read: wasRead, filters, criteria, unaccounted };
}

function parseFilters(value: unknown): MailSearchPhraseFilters | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const sender = address(record['sender']);
    const recipient = address(record['recipient']);
    const receivedFrom = calendarDay(record['receivedFrom']);
    const receivedTo = calendarDay(record['receivedTo']);

    if (sender === undefined || recipient === undefined || receivedFrom === undefined || receivedTo === undefined) {
        return null;
    }

    const unread = record['unread'];
    const flagged = record['flagged'];
    const hasAttachments = record['hasAttachments'];

    if (typeof unread !== 'boolean' || typeof flagged !== 'boolean' || typeof hasAttachments !== 'boolean') {
        return null;
    }

    return { sender, recipient, receivedFrom, receivedTo, unread, flagged, hasAttachments };
}

// `undefined` is the refusal and `null` is the absent filter, because those are two different answers: a filter the
// sentence did not state is the ordinary case, and a filter answered as something an address is not is a response this
// client will not draw.
function address(value: unknown): string | null | undefined {
    if (value === null || value === undefined) {
        return null;
    }

    return typeof value === 'string' && value.length > 0 && value.length <= longestAddress ? value : undefined;
}

const calendarDayPattern = /^\d{4}-\d{2}-\d{2}$/;

function calendarDay(value: unknown): string | null | undefined {
    if (value === null || value === undefined) {
        return null;
    }

    return typeof value === 'string' && calendarDayPattern.test(value) ? value : undefined;
}
