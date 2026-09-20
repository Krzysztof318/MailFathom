// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import type { RunTail } from './runFollowing';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What one Discover run has published so far, read from a cursor. The run is a row the deployment journals rather than
// a connection it holds open, so one route serves the first read and every later one and a client that was not
// listening is given what it missed — which is what lets the canvas draw a finished run with no hub at all.
//
// **The plan is versioned apart from the application.** A deployment and a client are updated separately, so this
// client will meet a plan a newer service wrote: `planSchemaVersion` on the run's start says which revision the plan
// was written against, and `understoodPlanSchemaVersion` below is the revision this client was written against. A
// screen renders every block it can draw and says what it could not, because the only other answer to one unfamiliar
// block would be to discard the whole answer.
//
// **Nothing here decides what a block looks like.** A block arrives named by its type and nothing more: what each type
// carries is read by the renderer that draws it, and a type this build has no renderer for is named to the reader
// rather than dropped. That is the same forward compatibility one revision further down, and it is why an event kind
// this client does not know is carried as `other` instead of being skipped — a sequence dropped on the floor is a
// cursor that never advances past it.

/** The route one run is read at, relative to the client prefix. */
export function discoveryRunRoute(runId: string): string {
    return `/discovery/runs/${encodeURIComponent(runId)}`;
}

/**
 * The revision of the presentation plan this client was written against.
 *
 * A run whose plan states a higher revision is drawn as far as it can be and the reader is told so; a run stating this
 * one or lower carries nothing this client was not built for.
 */
export const understoodPlanSchemaVersion = 2;

/** The block catalogue the plan is closed over, as the service spells each type on the wire. */
export const answerBlockTypes = [
    'answer',
    'evidenceList',
    'timeline',
    'factTable',
    'people',
    'threadState',
    'attachmentGallery',
    'draft',
    'suggestedAction',
] as const;

/** One of the block types this contract carries. */
export type AnswerBlockType = (typeof answerBlockTypes)[number];

/** One block of an answer, named by its type. */
export interface AnswerBlock {
    /** The type the catalogue carries, or `null` where the run named one this contract does not. */
    readonly type: AnswerBlockType | null;

    /**
     * What the run called the block, whichever of the two it is.
     *
     * It is carried even for a type the catalogue does carry, because it is what a screen names to a reader when this
     * build has nothing to draw it with — and a screen that had to spell a type it did not recognise out of a value it
     * had refused would have nothing to spell.
     */
    readonly named: string;
}

/**
 * One thing a run published, as far as drawing an answer is concerned.
 *
 * `other` is every kind this client does not act on, which is both the ones the contract already carries and the ones
 * it will gain: what a follower needs of an event it ignores is that it happened and where, so the cursor moves past it.
 */
export type DiscoveryRunEvent =
    | { readonly kind: 'started'; readonly sequence: number; readonly planSchemaVersion: number }
    | { readonly kind: 'block'; readonly sequence: number; readonly block: AnswerBlock }
    | { readonly kind: 'other'; readonly sequence: number };

// The whole of one answer: twenty blocks of prose with their citations. Generous against that and small enough that
// anything which is not a run's tail is refused before it is read.
const longestRunAnswer = 512 * 1024;

// Everything one run may publish, which is what a first read of a finished run answers with: its start, one report per
// lookup its retrieval plan may hold, one citation per source it may declare, one event per block, and its ending.
const mostEventsPerRead = 1 + 6 + 200 + 20 + 1;

// A type name the service assigned. Bounded because it is spelled onto a screen for a type this build cannot draw.
const longestBlockType = 128;

/**
 * Reads what one run has published after a cursor, and whether it is still working.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param runId The run to read, as starting one answered.
 * @param since The last sequence the caller holds, and zero to read the run from its beginning.
 * @returns The tail, or why it did not arrive. A run this user does not hold is `missing` rather than a failure to
 * retry: the deployment answers a run belonging to somebody else exactly as it answers one that never existed, so
 * asking again reaches the same answer for ever.
 */
export function readDiscoveryRunTail(
    session: ClientSession,
    transport: MailFathomTransport,
    runId: string,
    since: number,
): Promise<ClientResult<RunTail<DiscoveryRunEvent>>> {
    return spanned('GET /discovery/runs/{runId}', async () => {
        const path = routeFor(session, discoveryRunRoute(runId));

        const response = await send(transport, {
            method: 'GET',
            path: since > 0 ? `${path}?since=${String(since)}` : path,
            headers: headersFor(session),
            longestAnswer: longestRunAnswer,
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

        const tail = parseTail(response.body);

        return tail === null ? failed('unreadable', response.status) : read(tail);
    });
}

function parseTail(body: string): RunTail<DiscoveryRunEvent> | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null || typeof record['running'] !== 'boolean') {
        return null;
    }

    const events = parseEvents(record['events']);

    return events === null ? null : { running: record['running'], events };
}

function parseEvents(value: unknown): readonly DiscoveryRunEvent[] | null {
    if (!Array.isArray(value) || value.length > mostEventsPerRead) {
        return null;
    }

    const events: DiscoveryRunEvent[] = [];
    for (const written of value) {
        const event = parseEvent(written);
        if (event === null) {
            return null;
        }

        events.push(event);
    }

    return events;
}

function parseEvent(value: unknown): DiscoveryRunEvent | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const sequence = record['sequence'];
    if (typeof sequence !== 'number' || !Number.isSafeInteger(sequence) || sequence < 1) {
        return null;
    }

    switch (record['event']) {
        case 'started': {
            const planSchemaVersion = record['planSchemaVersion'];

            return typeof planSchemaVersion === 'number' && Number.isSafeInteger(planSchemaVersion)
                ? { kind: 'started', sequence, planSchemaVersion }
                : null;
        }

        case 'block': {
            const block = parseBlock(record['block']);

            return block === null ? null : { kind: 'block', sequence, block };
        }

        default:
            // A kind this client does not act on, which is every one it does not draw an answer out of — the run's
            // retrieval reports and its ending among them, whether a run has stopped being what the tail itself says —
            // and every kind a later revision of the contract adds. The sequence is what it is read for, so the cursor
            // moves past it rather than asking for it again for ever.
            return typeof record['event'] === 'string' ? { kind: 'other', sequence } : null;
    }
}

function parseBlock(value: unknown): AnswerBlock | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const named = record['type'];
    if (typeof named !== 'string' || named.length === 0 || named.length > longestBlockType) {
        return null;
    }

    return { type: isBlockType(named) ? named : null, named };
}

function isBlockType(value: string): value is AnswerBlockType {
    return (answerBlockTypes as readonly string[]).includes(value);
}
