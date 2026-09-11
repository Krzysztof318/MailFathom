// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Where one conversation stands: what its people settled, what they raised and left open, what anybody undertook, and
// how a document they exchanged changed between two versions of it. It is a record a deployment wrote behind its own
// account run rather than something composed while a screen waits, which is why it is a route of its own rather than a
// member of the conversation document beside it.
//
// **Absence is a state rather than a failure.** A deployment that has not turned the derivation on, one that has not
// reached this conversation yet, and a conversation this user does not hold all answer alike, and this module reports
// every one of them as `null` rather than as a failure — so a screen draws a conversation nothing has been derived
// about instead of an error nobody can act on.
//
// It publishes no participants. The conversation route already carries the authors of the whole exchange, counted and
// ordered, so a screen draws that card from the document it already holds.

/** The route where one conversation stands is served at, relative to the client prefix. */
export function mailThreadStateRoute(threadId: string): string {
    return `/threads/${encodeURIComponent(threadId)}/state`;
}

/** Which of the four things a statement says. */
export type MailThreadStateAspect = 'Agreement' | 'OpenQuestion' | 'Commitment' | 'VersionDifference';

/** How much of the conversation the derivation was shown. */
export type MailThreadStateCoverage = 'WholeThread' | 'ThreadTooLarge';

/**
 * One message a statement rests on, spelled as every citation on this surface is spelled.
 *
 * It is the presentation plan's own citation target, so a reader follows a source here through exactly the request a
 * Discover answer's sources are followed through, and the client holds one implementation of that affordance.
 */
export interface MailThreadStateSource {
    readonly kind: 'email';
    readonly email: string;
}

/** One statement about where a conversation stands. */
export interface MailThreadStateEntry {
    readonly aspect: MailThreadStateAspect;

    /** What the statement says, in the language the conversation is written in. */
    readonly text: string;

    /** Who undertook a commitment, and `null` for every other aspect and for a commitment naming nobody. */
    readonly owedBy: string | null;

    /** When a commitment falls due, and `null` for every other aspect and for one the conversation gave no date for. */
    readonly dueAt: string | null;

    /** The messages the statement rests on, best first. Never empty. */
    readonly sources: readonly MailThreadStateSource[];
}

/** Where one conversation stands, as a screen draws it beside the conversation. */
export interface MailThreadState {
    readonly threadId: string;

    /** `ThreadTooLarge` arrives with no statements, and a screen says the exchange is too long rather than drawing part of it. */
    readonly coverage: MailThreadStateCoverage;

    /** When the state was derived, which is what a screen says the block is as of. */
    readonly derivedAt: string;

    /**
     * Whether the state was derived from the conversation as it stands now. `false` once a message has joined or left
     * it since, and a screen never draws such a state as the conversation's current one.
     */
    readonly current: boolean;

    /** The statements, at most one of each aspect, in aspect order. */
    readonly entries: readonly MailThreadStateEntry[];
}

// The block is a handful of short sentences and their sources, so a generous ceiling here is still far below anything
// worth buffering; it exists for the answer that is not a block at all.
const longestStateAnswer = 64 * 1024;

// What one answer may carry before the block is refused unread. The service bounds each of these itself; these are the
// client's own copy of the bound, checked while the collections are walked rather than after.
const longestIdentity = 256;
const longestText = 4_096;
const mostEntries = 64;
const mostSourcesPerEntry = 16;

const aspects: readonly MailThreadStateAspect[] = ['Agreement', 'OpenQuestion', 'Commitment', 'VersionDifference'];

const coverages: readonly MailThreadStateCoverage[] = ['WholeThread', 'ThreadTooLarge'];

/**
 * Reads where one of the user's conversations stands, answering an absence as a value rather than as a failure.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param threadId The conversation to read, as a message row published it.
 * @returns The block, `null` where this deployment has none for that conversation, or why neither arrived.
 */
export function readMailThreadState(
    session: ClientSession,
    transport: MailFathomTransport,
    threadId: string,
): Promise<ClientResult<MailThreadState | null>> {
    return spanned('GET /threads/{threadId}/state', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailThreadStateRoute(threadId)),
            headers: headersFor(session),
            longestAnswer: longestStateAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        // The one status this route answers that is not a failure. A conversation nothing has been derived about is a
        // state a screen draws, so it is read as an absence rather than reported as something that went wrong.
        if (response.status === 404) {
            return read(null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const state = parseState(response.body);

        return state === null ? failed('unreadable', response.status) : read(state);
    });
}

function parseState(body: string): MailThreadState | null {
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
    const coverage = record['coverage'];
    const derivedAt = record['derivedAt'];
    const current = record['current'];

    if (!isIdentity(threadId) || !isCoverage(coverage) || !isInstant(derivedAt) || typeof current !== 'boolean') {
        return null;
    }

    const entries = parseEntries(record['entries']);

    return entries === null ? null : { threadId, coverage, derivedAt, current, entries };
}

function parseEntries(value: unknown): readonly MailThreadStateEntry[] | null {
    if (!Array.isArray(value) || value.length > mostEntries) {
        return null;
    }

    const entries: MailThreadStateEntry[] = [];
    for (const written of value) {
        const entry = parseEntry(written);
        if (entry === null) {
            return null;
        }

        entries.push(entry);
    }

    return entries;
}

function parseEntry(value: unknown): MailThreadStateEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const aspect = record['aspect'];
    const text = record['text'];
    const owedBy = record['owedBy'] ?? null;
    const dueAt = record['dueAt'] ?? null;

    if (!isAspect(aspect) || typeof text !== 'string' || text.length === 0 || text.length > longestText) {
        return null;
    }

    if (owedBy !== null && (typeof owedBy !== 'string' || owedBy.length === 0 || owedBy.length > longestText)) {
        return null;
    }

    if (dueAt !== null && !isInstant(dueAt)) {
        return null;
    }

    const sources = parseSources(record['sources']);

    // A statement no message backs is the thing the record exists to rule out, so one arriving without sources is an
    // answer this client refuses to draw rather than one it draws without its evidence.
    return sources === null || sources.length === 0 ? null : { aspect, text, owedBy, dueAt, sources };
}

function parseSources(value: unknown): readonly MailThreadStateSource[] | null {
    if (!Array.isArray(value) || value.length > mostSourcesPerEntry) {
        return null;
    }

    const sources: MailThreadStateSource[] = [];
    for (const written of value) {
        const record = asRecord(written);
        if (record?.['kind'] !== 'email' || !isIdentity(record['email'])) {
            return null;
        }

        sources.push({ kind: 'email', email: record['email'] });
    }

    return sources;
}

function isAspect(value: unknown): value is MailThreadStateAspect {
    return typeof value === 'string' && (aspects as readonly string[]).includes(value);
}

function isCoverage(value: unknown): value is MailThreadStateCoverage {
    return typeof value === 'string' && (coverages as readonly string[]).includes(value);
}

// A name the deployment assigned, which is what the conversation and each of its messages are reached by.
function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}

// An instant the service wrote, held to being readable as one rather than to a spelling: a value nothing can date is a
// value a screen would render as `Invalid Date`.
function isInstant(value: unknown): value is string {
    return (
        typeof value === 'string' &&
        value.length > 0 &&
        value.length <= longestIdentity &&
        !Number.isNaN(Date.parse(value))
    );
}
