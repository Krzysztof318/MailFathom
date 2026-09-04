// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The routes in this package that are about changing a mailbox rather than reading one. Nothing here reaches a mail
// server: a submission writes a durable record and answers, and the account's own reconciliation pass is what issues
// the IMAP command — so a screen never waits on somebody else's server, and a change asked for while the account is
// unreachable is kept rather than lost.
//
// Each act is a function named for what it asks rather than a general submitter the others are written in terms of:
// marking read, changing the two flags a mail server keeps, and filing a message in another folder. What they share is
// how an answer is read, because both routes answer one result per message in the vocabulary the surface publishes.
//
// Flags and folders are separate routes because they are separate grants: a wrong flag misdescribes mail the owner can
// still find, and a wrong move puts it somewhere else. A caller holding one grant and not the other therefore meets a
// refusal on the act it may not perform rather than on every act.
//
// Reading where a record stands is not act-specific in that way — a record is a record whichever route wrote it — so
// the read below takes record identities and nothing else.

/** The route a batch of flag changes is written down at, relative to the client prefix. */
export const mailFlagMutationsRoute = '/mutations/flags';

/** The route a batch of folder moves is written down at, relative to the client prefix. */
export const mailMoveMutationsRoute = '/mutations/moves';

/** The route the caller's own change records are read back at, relative to the client prefix. */
export const mailMutationRecordsRoute = '/mutations';

/**
 * The most messages one submission may name, which is the deployment's bound rather than a preference.
 *
 * Stated here rather than borrowed, so this contract says its own size: a batch past it is refused whole with a `400`
 * rather than partly applied, which is why the caller splits instead of discovering the bound from a refusal.
 */
export const mostMessagesPerMutation = 200;

/**
 * The most records one read may name, which is the deployment's bound rather than a preference.
 *
 * The route names them in the request line, so it enforces this hundred itself and answers a longer read with a `400`.
 * A caller following more changes than that reads them a hundred at a time rather than discovering the bound from a
 * refusal.
 */
export const mostRecordsPerRead = 100;

/**
 * The most records one message in one submission may produce.
 *
 * A record is written per value rather than per message, and what one request can name is the two system flags and one
 * tag change — so this is well above the three that exist and far below anything worth buffering. A longer list is an
 * answer this package refuses rather than one it walks.
 */
const mostRecordsPerChange = 8;

// A result is five short fields per message, and the batch is bounded above. Generous against that arithmetic and far
// below anything worth buffering: what the bound guards against is an answer that was never a batch of results.
const longestMutationAnswer = 262_144;

// A record is six short fields and a read names at most a hundred of them. Generous against that arithmetic on the
// same reasoning as the bound above.
const longestRecordsAnswer = 65_536;

/**
 * What became of one message in a submitted batch.
 *
 * `recorded` is the only outcome that wrote anything down. The other four are each about that one message rather than
 * about the request, which is what lets a batch composed from a list that has moved on report exactly which entries did
 * not apply and write the rest down.
 */
export type MailMutationOutcome =
    | 'recorded'
    | 'message-not-found'
    | 'destination-not-found'
    | 'already-in-destination'
    | 'account-no-longer-configured'
    | 'change-not-usable';

const mutationOutcomes: readonly MailMutationOutcome[] = [
    'recorded',
    'message-not-found',
    'destination-not-found',
    'already-in-destination',
    'account-no-longer-configured',
    'change-not-usable',
];

/**
 * Where one change stands on its way to the mailbox.
 *
 * `pending` and `converging` are both on their way and differ only in whether a pass has picked the record up yet;
 * `completed` reached the mail server; `dead-lettered` exhausted the account's own bounded retries and is nobody's to
 * keep waiting for; `cancelled` was taken back before anything went out.
 */
export type MailMutationRecordState = 'pending' | 'converging' | 'completed' | 'dead-lettered' | 'cancelled';

const recordStates: readonly MailMutationRecordState[] = [
    'pending',
    'converging',
    'completed',
    'dead-lettered',
    'cancelled',
];

/** One record a submission wrote down, as the answer to that submission names it. */
export interface MailMutationChange {
    readonly recordId: string;
    readonly state: MailMutationRecordState;
}

/** What one message in a batch was answered with, and what was written down for it. */
export interface MailMutationResult {
    readonly storedEmailId: string;
    readonly outcome: MailMutationOutcome;

    /**
     * The records this message's change became, which is empty for every outcome but `recorded`.
     *
     * A record per value rather than per message, because a record is the unit the account's pass resumes, abandons,
     * and attributes an observation back to: a message whose seen flag completes while its keywords are still
     * converging is two records saying two different things.
     */
    readonly changes: readonly MailMutationChange[];
}

/** Where one change this caller asked for stands, as the read route reports it. */
export interface MailMutationRecord {
    readonly recordId: string;
    readonly storedEmailId: string;
    readonly state: MailMutationRecordState;

    /**
     * Whether a command went out and its answer never came back, so the mailbox may be in either of two states.
     *
     * The one field on this answer a person acts on rather than waits through: MailFathom will not guess which of the
     * two happened, and a caller that resolved it by asking again would be deciding on somebody's behalf.
     */
    readonly outcomeUnknown: boolean;
}

/**
 * Where a change leaves the two flags the mail server keeps for one message.
 *
 * A flag left unstated stays where it stands, which is what makes starring a message and marking it unread two changes
 * that do not undo each other when they travel in one batch.
 */
export interface MailFlagChange {
    readonly storedEmailId: string;

    /** `true` marks the message read, `false` marks it unread, and absent leaves the flag alone. */
    readonly seen?: boolean;

    /** `true` stars the message, `false` unstars it, and absent leaves the flag alone. */
    readonly flagged?: boolean;
}

/** One message to file elsewhere, and the folder it is going to. */
export interface MailMove {
    readonly storedEmailId: string;

    /** MailFathom's own name for the destination folder, exactly as the folders route publishes it. */
    readonly destinationFolder: string;
}

/**
 * Writes down that the named messages have been read, as one batch of flag changes their reader's act authored.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param storedEmailIds The messages to mark read, at most {@link mostMessagesPerMutation} of them.
 * @returns One result per message the deployment answered for, or an expected failure as a value.
 */
export function markMailRead(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailIds: readonly string[],
): Promise<ClientResult<readonly MailMutationResult[]>> {
    return changeMailFlags(
        session,
        transport,
        storedEmailIds.map((storedEmailId) => ({ storedEmailId, seen: true })),
    );
}

/**
 * Writes down where the named messages' flags are to be left, as one batch their reader's act authored.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param changes The messages to change, at most {@link mostMessagesPerMutation} of them.
 * @returns One result per message the deployment answered for, or an expected failure as a value.
 */
export function changeMailFlags(
    session: ClientSession,
    transport: MailFathomTransport,
    changes: readonly MailFlagChange[],
): Promise<ClientResult<readonly MailMutationResult[]>> {
    const asked = changes.slice(0, mostMessagesPerMutation).map((change) => ({
        storedEmailId: change.storedEmailId,
        flags: { seen: change.seen, flagged: change.flagged },
    }));

    return submit(session, transport, mailFlagMutationsRoute, { changes: asked });
}

/**
 * Writes down that the named messages are to be filed in another folder, as one batch their reader's act authored.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param moves The messages to file and where each is going, at most {@link mostMessagesPerMutation} of them.
 * @returns One result per message the deployment answered for, or an expected failure as a value.
 */
export function moveMail(
    session: ClientSession,
    transport: MailFathomTransport,
    moves: readonly MailMove[],
): Promise<ClientResult<readonly MailMutationResult[]>> {
    return submit(session, transport, mailMoveMutationsRoute, { moves: moves.slice(0, mostMessagesPerMutation) });
}

/** Puts one batch on the wire and reads what came back, which is the same exchange whichever act asked for it. */
function submit(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    body: object,
): Promise<ClientResult<readonly MailMutationResult[]>> {
    return spanned(`POST ${route}`, async () =>
        answerOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, route),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
                longestAnswer: longestMutationAnswer,
            }),
        ),
    );
}

/**
 * Reads where each of the caller's own changes stands.
 *
 * A record belonging to somebody else, or one recorded in a folder this caller may no longer read, is absent from the
 * answer rather than refused — so an answer shorter than the read is the ordinary case and never an error.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param recordIds The records to ask about, at most {@link mostRecordsPerRead} of them.
 * @returns One record per change the deployment still answers for, or an expected failure as a value.
 */
export function readMailMutationRecords(
    session: ClientSession,
    transport: MailFathomTransport,
    recordIds: readonly string[],
): Promise<ClientResult<readonly MailMutationRecord[]>> {
    const naming = recordIds.slice(0, mostRecordsPerRead);
    const asked = naming.map((recordId) => `record=${encodeURIComponent(recordId)}`).join('&');

    return spanned(`GET ${mailMutationRecordsRoute}`, async () =>
        recordsOf(
            await send(transport, {
                method: 'GET',
                path: `${routeFor(session, mailMutationRecordsRoute)}?${asked}`,
                headers: headersFor(session),
                longestAnswer: longestRecordsAnswer,
            }),
            naming.length,
        ),
    );
}

function recordsOf(response: ClientResponse | null, asked: number): ClientResult<readonly MailMutationRecord[]> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const records = parseRecords(response.body, asked);

    return records === null ? failed('unreadable', response.status) : read(records);
}

function parseRecords(body: string, asked: number): readonly MailMutationRecord[] | null {
    // Bounded by what this read actually named rather than by what the route permits, the way `mailTimeline.ts` and
    // `mailSearch.ts` bound their own pages: an answer carrying records nobody asked about is one this client has no
    // way to place, and reading it would put somebody else's change in front of a person as though it were theirs.
    const answered = parsedArray(body, 'changes', asked);

    if (answered === null) {
        return null;
    }

    const records: MailMutationRecord[] = [];

    for (const entry of answered) {
        const record = parseRecord(entry);

        if (record === null) {
            return null;
        }

        records.push(record);
    }

    return records;
}

function parseRecord(entry: unknown): MailMutationRecord | null {
    const record = asRecord(entry);

    if (record === null) {
        return null;
    }

    const recordId = record['recordId'];
    const storedEmailId = record['storedEmailId'];
    const state = record['state'];
    const outcomeUnknown = record['outcomeUnknown'];

    if (
        typeof recordId !== 'string' ||
        typeof storedEmailId !== 'string' ||
        !isRecordState(state) ||
        typeof outcomeUnknown !== 'boolean'
    ) {
        return null;
    }

    // What a record was retried, when it was written, and which failure it last met are deliberately not read. A
    // screen says a change is on its way or that it stopped, and a field parsed for nobody is a field to keep true.
    return { recordId, storedEmailId, state, outcomeUnknown };
}

function answerOf(response: ClientResponse | null): ClientResult<readonly MailMutationResult[]> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const results = parseResults(response.body);

    return results === null ? failed('unreadable', response.status) : read(results);
}

/** The array one named field of a JSON body holds, refused where it is absent, malformed, or longer than the bound. */
function parsedArray(body: string, named: string, bound: number): readonly unknown[] | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    const answered = record?.[named];

    // Bounded before the walk rather than after it, because a walk that completed has already paid for what it read.
    return Array.isArray(answered) && answered.length <= bound ? answered : null;
}

function parseResults(body: string): readonly MailMutationResult[] | null {
    const answered = parsedArray(body, 'results', mostMessagesPerMutation);

    if (answered === null) {
        return null;
    }

    const results: MailMutationResult[] = [];

    for (const entry of answered) {
        const result = parseResult(entry);

        if (result === null) {
            return null;
        }

        results.push(result);
    }

    return results;
}

function parseResult(entry: unknown): MailMutationResult | null {
    const record = asRecord(entry);

    if (record === null) {
        return null;
    }

    const storedEmailId = record['storedEmailId'];
    const outcome = record['outcome'];

    if (typeof storedEmailId !== 'string' || !isMutationOutcome(outcome)) {
        return null;
    }

    const changes = parseChanges(record['changes']);

    return changes === null ? null : { storedEmailId, outcome, changes };
}

function parseChanges(answered: unknown): readonly MailMutationChange[] | null {
    // An outcome that wrote nothing down carries no list at all, which is the ordinary shape rather than a hole in the
    // answer: what is refused is a value that is present and is not a list of the stated length.
    if (answered === undefined || answered === null) {
        return [];
    }

    if (!Array.isArray(answered) || answered.length > mostRecordsPerChange) {
        return null;
    }

    const changes: MailMutationChange[] = [];

    for (const entry of answered) {
        const record = asRecord(entry);
        const recordId = record?.['recordId'];
        const state = record?.['state'];

        if (typeof recordId !== 'string' || !isRecordState(state)) {
            return null;
        }

        changes.push({ recordId, state });
    }

    return changes;
}

function isMutationOutcome(value: unknown): value is MailMutationOutcome {
    return typeof value === 'string' && mutationOutcomes.includes(value as MailMutationOutcome);
}

function isRecordState(value: unknown): value is MailMutationRecordState {
    return typeof value === 'string' && recordStates.includes(value as MailMutationRecordState);
}
