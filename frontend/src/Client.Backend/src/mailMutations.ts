// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The two routes in this package that change a mailbox rather than read one. Nothing here reaches a mail server: a
// submission writes a durable record and answers, and the account's own reconciliation pass is what issues the IMAP
// command — so a screen never waits on somebody else's server, and a change asked for while the account is unreachable
// is kept rather than lost.
//
// Each act is a function named for what it asks rather than a general submitter the others are written in terms of:
// marking read, changing the two flags a mail server keeps, and filing a message in another folder. What they share is
// how an answer is read, because both routes answer one result per message in the vocabulary the surface publishes.
//
// Flags and folders are separate routes because they are separate grants: a wrong flag misdescribes mail the owner can
// still find, and a wrong move puts it somewhere else. A caller holding one grant and not the other therefore meets a
// refusal on the act it may not perform rather than on every act.

/** The route a batch of flag changes is written down at, relative to the client prefix. */
export const mailFlagMutationsRoute = '/mutations/flags';

/** The route a batch of folder moves is written down at, relative to the client prefix. */
export const mailMoveMutationsRoute = '/mutations/moves';

/**
 * The most messages one submission may name, which is the deployment's bound rather than a preference.
 *
 * Stated here rather than borrowed, so this contract says its own size: a batch past it is refused whole with a `400`
 * rather than partly applied, which is why the caller splits instead of discovering the bound from a refusal.
 */
export const mostMessagesPerMutation = 200;

// A result is five short fields per message, and the batch is bounded above. Generous against that arithmetic and far
// below anything worth buffering: what the bound guards against is an answer that was never a batch of results.
const longestMutationAnswer = 262_144;

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

/** What one message in a batch was answered with. */
export interface MailMutationResult {
    readonly storedEmailId: string;
    readonly outcome: MailMutationOutcome;
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

function parseResults(body: string): readonly MailMutationResult[] | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    const answered = record?.['results'];

    // Bounded during the walk rather than after it, because a walk that completed has already paid for what it read.
    if (!Array.isArray(answered) || answered.length > mostMessagesPerMutation) {
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

    // What each change became — the record it was written down as and where that record stands — is deliberately not
    // read here. Nothing in this client follows a record yet, and a field parsed for nobody is a field to keep true.
    return { storedEmailId, outcome };
}

function isMutationOutcome(value: unknown): value is MailMutationOutcome {
    return typeof value === 'string' && mutationOutcomes.includes(value as MailMutationOutcome);
}
