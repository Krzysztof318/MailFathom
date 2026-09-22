// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { parseBlock, type AnswerBlock } from './discoveryRun';
import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { parseDeclaredSource, type DeclaredSource } from './presentationBlocks';
import type { RunTail } from './runFollowing';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// The Agent's conversations as `/api/client` serves them: the history, one conversation read from a cursor, and the
// four writes a person makes into one — asking, steering the answer being composed, stopping it, and deleting the whole
// conversation. A conversation is the entries it was written as, so what arrives here is that record rather than a
// rendering of it: which entries make one answer, and which proposal a resolution decides, is the screen's to fold.

/** Where the history is read. */
export const agentConversationsRoute = '/agent/conversations';

/** Where one conversation is read and deleted. */
export function agentConversationRoute(conversationId: string): string {
    return `${agentConversationsRoute}/${encodeURIComponent(conversationId)}`;
}

/** The most conversations one listing answers with, which is the service's own ceiling. */
const mostConversationsListed = 100;

/** The most entries one read of a conversation answers with, which is the service's own ceiling. */
const mostEntriesPerRead = 250;

// An identifier or a title the deployment wrote, bounded because it is put back in a path or spelled onto a screen.
const longestIdentifier = 128;
const longestTitle = 1024;
const longestText = 64 * 1024;
const longestWriteAnswer = 4 * 1024;

/** One conversation as the history lists it. */
export interface AgentConversationSummary {
    readonly id: string;

    /** What the service named it after its first question, and `null` before it has. */
    readonly title: string | null;

    readonly startedAt: string;
    readonly lastActivityAt: string;
}

/** What a person's question was about, where they asked it from a screen that carries one. */
export interface AgentMessageScope {
    readonly kind: 'mailbox' | 'thread' | 'calendarEvent' | 'discoveryRun';
    readonly subject: string | null;
}

/** How an answer ended. An ending this build has no name for is read as `failed`. */
export type AgentAnswerOutcome = 'completed' | 'stopped' | 'failed';

/** What a person decided about a proposal, and what became of one they accepted. */
export type AgentProposalState = 'accepted' | 'failed' | 'declined';

/**
 * One entry of a conversation, in the order it was written.
 *
 * `run` is the answer an entry belongs to, which is the message identifier the answer was started under. The last
 * member is a kind this build does not read, which moves the cursor past it rather than refusing the conversation.
 */
export type AgentConversationEntry =
    | {
          readonly kind: 'message';
          readonly sequence: number;
          readonly messageId: string;
          readonly author: 'person' | 'agent';
          readonly text: string;
          readonly scope: AgentMessageScope | null;
      }
    | { readonly kind: 'answerStarted'; readonly sequence: number; readonly run: string }
    | { readonly kind: 'status'; readonly sequence: number; readonly run: string; readonly status: string }
    | { readonly kind: 'citation'; readonly sequence: number; readonly run: string; readonly source: DeclaredSource }
    | { readonly kind: 'block'; readonly sequence: number; readonly run: string; readonly block: AnswerBlock }
    | { readonly kind: 'proposal'; readonly sequence: number; readonly run: string; readonly block: AnswerBlock }
    | {
          readonly kind: 'answerEnded';
          readonly sequence: number;
          readonly run: string;
          readonly outcome: AgentAnswerOutcome;
      }
    | {
          readonly kind: 'resolution';
          readonly sequence: number;
          readonly proposedAt: number;
          readonly state: AgentProposalState;
      }
    | { readonly kind: 'other'; readonly sequence: number };

/**
 * One read of a conversation: what it is called and what was written past the cursor.
 *
 * `running` is true while an answer is being composed and while more entries wait past this read's ceiling — either way
 * more is coming, which is what following a run reads.
 */
export interface AgentConversationPage extends RunTail<AgentConversationEntry> {
    readonly title: string | null;
}

/** What posting a message into a conversation answered: the answer it opened. */
export interface AgentMessagePosted {
    readonly run: string;
}

/**
 * Lists this person's conversations, newest activity first.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @returns The conversations, or why they did not arrive.
 */
export function listAgentConversations(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<readonly AgentConversationSummary[]>> {
    return spanned(`GET ${agentConversationsRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, agentConversationsRoute),
            headers: headersFor(session),
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const conversations = parseSummaries(response.body);

        return conversations === null ? failed('unreadable', response.status) : read(conversations);
    });
}

/**
 * Reads one conversation from a cursor.
 *
 * @param since The last sequence the caller holds, and zero to read the conversation from its beginning.
 * @returns The page, or why it did not arrive. A conversation this person does not hold is `missing`: the service
 * answers another person's exactly as one that never existed, so asking again reaches the same answer for ever.
 */
export function readAgentConversation(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
    since: number,
): Promise<ClientResult<AgentConversationPage>> {
    return spanned('GET /agent/conversations/{conversationId}', async () => {
        const path = routeFor(session, agentConversationRoute(conversationId));

        const response = await send(transport, {
            method: 'GET',
            path: since > 0 ? `${path}?since=${String(since)}` : path,
            headers: headersFor(session),
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

        const page = parsePage(response.body);

        return page === null ? failed('unreadable', response.status) : read(page);
    });
}

/**
 * Asks the Agent something in a conversation, which the first question into a new identifier opens.
 *
 * @param conversationId The conversation, which the client names itself when it starts one.
 * @param messageId The question's own identifier, so a retry of the same post is the same question.
 * @returns The answer it opened, or why it was not asked.
 */
export function askAgent(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
    messageId: string,
    text: string,
): Promise<ClientResult<AgentMessagePosted>> {
    return spanned('POST /agent/conversations/{conversationId}/messages', () =>
        posted(session, transport, `${agentConversationRoute(conversationId)}/messages`, { messageId, text }),
    );
}

/**
 * Adds an instruction to the answer being composed, which it takes from its next turn rather than starting again.
 *
 * @returns The answer it joined, or why it was not added — an answer that ended before it arrived among them.
 */
export function steerAgentRun(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
    run: string,
    messageId: string,
    text: string,
): Promise<ClientResult<AgentMessagePosted>> {
    return spanned('POST /agent/conversations/{conversationId}/runs/{runId}/messages', () =>
        posted(
            session,
            transport,
            `${agentConversationRoute(conversationId)}/runs/${encodeURIComponent(run)}/messages`,
            { messageId, text },
        ),
    );
}

/**
 * Stops the answer being composed where it stands. What arrived stays, and the conversation records the stop.
 *
 * @returns Nothing where the stop was recorded, an answer that had already ended among them.
 */
export function stopAgentRun(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
    run: string,
): Promise<ClientResult<void>> {
    return spanned('DELETE /agent/conversations/{conversationId}/runs/{runId}', () =>
        removed(session, transport, `${agentConversationRoute(conversationId)}/runs/${encodeURIComponent(run)}`),
    );
}

/**
 * Deletes a conversation and everything written in it, ending an answer still being composed.
 *
 * @returns Nothing where it was deleted, or why not. One already gone is `missing`.
 */
export function deleteAgentConversation(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
): Promise<ClientResult<void>> {
    return spanned('DELETE /agent/conversations/{conversationId}', () =>
        removed(session, transport, agentConversationRoute(conversationId)),
    );
}

async function posted(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    message: { readonly messageId: string; readonly text: string },
): Promise<ClientResult<AgentMessagePosted>> {
    const response = await send(transport, {
        method: 'POST',
        path: routeFor(session, route),
        headers: { ...headersFor(session), 'Content-Type': 'application/json' },
        body: JSON.stringify(message),
        longestAnswer: longestWriteAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    if (response.status !== 202) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const run = parsePosted(response.body);

    return run === null ? failed('unreadable', response.status) : read({ run });
}

async function removed(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
): Promise<ClientResult<void>> {
    const response = await send(transport, {
        method: 'DELETE',
        path: routeFor(session, route),
        headers: headersFor(session),
        longestAnswer: longestWriteAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    return response.status === 204 ? read(undefined) : failed(failureReasonForStatus(response.status), response.status);
}

function parsed(body: string): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

function parsePosted(body: string): string | null {
    return identifier(parsed(body)?.['runId']);
}

function parseSummaries(body: string): readonly AgentConversationSummary[] | null {
    const listed = parsed(body)?.['conversations'];
    if (!Array.isArray(listed) || listed.length > mostConversationsListed) {
        return null;
    }

    const conversations: AgentConversationSummary[] = [];
    for (const written of listed) {
        const record = asRecord(written);
        const id = identifier(record?.['id']);
        const title = record?.['title'];
        const startedAt = instant(record?.['startedAt']);
        const lastActivityAt = instant(record?.['lastActivityAt']);

        if (id === null || startedAt === null || lastActivityAt === null || !isTitle(title)) {
            return null;
        }

        conversations.push({ id, title: title ?? null, startedAt, lastActivityAt });
    }

    return conversations;
}

function parsePage(body: string): AgentConversationPage | null {
    const record = parsed(body);
    const title = record?.['title'];
    const composing = record?.['composing'];
    const moreFollows = record?.['moreFollows'];
    const written = record?.['entries'];

    if (
        record === null ||
        !isTitle(title) ||
        typeof composing !== 'boolean' ||
        typeof moreFollows !== 'boolean' ||
        !Array.isArray(written) ||
        written.length > mostEntriesPerRead
    ) {
        return null;
    }

    const events: AgentConversationEntry[] = [];
    for (const value of written) {
        const entry = parseEntry(value);
        if (entry === null) {
            return null;
        }

        events.push(entry);
    }

    return { title: title ?? null, running: composing || moreFollows, events };
}

const outcomes: Readonly<Record<string, AgentAnswerOutcome | undefined>> = {
    Completed: 'completed',
    Stopped: 'stopped',
    Failed: 'failed',
};

const resolutions: Readonly<Record<string, AgentProposalState | undefined>> = {
    Accepted: 'accepted',
    Failed: 'failed',
    Declined: 'declined',
};

const authors: Readonly<Record<string, 'person' | 'agent' | undefined>> = {
    Person: 'person',
    Agent: 'agent',
};

const scopeKinds: Readonly<Record<string, AgentMessageScope['kind'] | undefined>> = {
    Mailbox: 'mailbox',
    Thread: 'thread',
    CalendarEvent: 'calendarEvent',
    DiscoveryRun: 'discoveryRun',
};

function parseEntry(value: unknown): AgentConversationEntry | null {
    const record = asRecord(value);
    const sequence = record?.['sequence'];
    const entry = asRecord(record?.['entry']);

    if (entry === null || typeof sequence !== 'number' || !Number.isSafeInteger(sequence) || sequence < 1) {
        return null;
    }

    const run = identifier(entry['messageId']);

    switch (entry['entry']) {
        case 'message': {
            const author = authors[String(entry['author'])];
            const text = entry['text'];
            const scope = parseScope(entry['scope']);

            return run === null ||
                author === undefined ||
                typeof text !== 'string' ||
                text.length > longestText ||
                scope === undefined
                ? null
                : { kind: 'message', sequence, messageId: run, author, text, scope };
        }

        case 'answerStarted':
            return run === null ? null : { kind: 'answerStarted', sequence, run };

        case 'status': {
            const status = entry['status'];

            return run === null || typeof status !== 'string' || status.length > longestTitle
                ? null
                : { kind: 'status', sequence, run, status };
        }

        case 'citation': {
            const source = parseDeclaredSource(entry['citation']);

            return run === null || source === null ? null : { kind: 'citation', sequence, run, source };
        }

        case 'block':
        case 'proposal': {
            const block = parseBlock(entry['block']);

            return run === null || block === null ? null : { kind: entry['entry'], sequence, run, block };
        }

        case 'answerEnded':
            return run === null
                ? null
                : {
                      kind: 'answerEnded',
                      sequence,
                      run,
                      outcome: outcomes[String(entry['outcome'])] ?? 'failed',
                  };

        case 'resolution': {
            const proposedAt = entry['proposedAt'];
            const state = resolutions[String(entry['state'])];

            return typeof proposedAt !== 'number' || !Number.isSafeInteger(proposedAt) || state === undefined
                ? null
                : { kind: 'resolution', sequence, proposedAt, state };
        }

        default:
            return typeof entry['entry'] === 'string' ? { kind: 'other', sequence } : null;
    }
}

/** The scope a question carries, `null` where it carries none, and `undefined` where what arrived is not one. */
function parseScope(value: unknown): AgentMessageScope | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const kind = scopeKinds[String(record?.['kind'])];
    const subject = record?.['subject'];

    if (kind === undefined || !(subject === undefined || subject === null || identifier(subject) !== null)) {
        return undefined;
    }

    return { kind, subject: typeof subject === 'string' ? subject : null };
}

function identifier(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentifier ? value : null;
}

function instant(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentifier ? value : null;
}

function isTitle(value: unknown): value is string | null | undefined {
    return value === undefined || value === null || (typeof value === 'string' && value.length <= longestTitle);
}
