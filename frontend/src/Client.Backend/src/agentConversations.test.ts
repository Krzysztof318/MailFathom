// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    agentConversationRoute,
    askAgent,
    deleteAgentConversation,
    listAgentConversations,
    readAgentConversation,
    steerAgentRun,
    stopAgentRun,
} from './agentConversations';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const conversation = '0198f4a1-0000-7000-8000-00000000a9e1';
const question = '0198f4a1-0000-7000-8000-00000000a9f1';
const base = 'https://mail.example.invalid/api/client/agent/conversations';

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

function pageOf(entries: readonly unknown[], composing = false, moreFollows = false): string {
    return JSON.stringify({ title: 'Bays', startedAt: '2026-08-31T09:40:00+00:00', composing, moreFollows, entries });
}

function entryOf(sequence: number, entry: Readonly<Record<string, unknown>>): unknown {
    return { sequence, entry };
}

const answerBlock = {
    type: 'answer',
    evidence: {
        support: 'Supported',
        citations: ['c-1'],
        freshness: { staleness: 'Current', observedAt: '2026-08-31T09:42:00+00:00' },
    },
    text: 'Four bays were confirmed.',
    confidence: 'High',
};

describe('agentConversationRoute', () => {
    it('escapes an identifier that is not the shape the route matches', () => {
        expect(agentConversationRoute('../x')).toBe('/agent/conversations/..%2Fx');
    });
});

describe('listAgentConversations', () => {
    it('reads each conversation the history lists, and a title not yet given as none', async () => {
        const answered = await listAgentConversations(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    conversations: [
                        {
                            id: conversation,
                            title: null,
                            startedAt: '2026-08-31T09:40:00+00:00',
                            lastActivityAt: '2026-08-31T09:42:00+00:00',
                        },
                    ],
                }),
            }),
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: [
                {
                    id: conversation,
                    title: null,
                    startedAt: '2026-08-31T09:40:00+00:00',
                    lastActivityAt: '2026-08-31T09:42:00+00:00',
                },
            ],
        });
    });

    it('refuses a listing longer than the service ever answers with', async () => {
        const listed = Array.from({ length: 101 }, (_, index) => ({
            id: `c-${String(index)}`,
            title: null,
            startedAt: '2026-08-31T09:40:00+00:00',
            lastActivityAt: '2026-08-31T09:42:00+00:00',
        }));

        const answered = await listAgentConversations(
            session,
            answering({ status: 200, body: JSON.stringify({ conversations: listed }) }),
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('refuses a conversation that names no identifier', async () => {
        const answered = await listAgentConversations(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    conversations: [{ title: 'x', startedAt: 'a', lastActivityAt: 'b' }],
                }),
            }),
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it.each([
        ['a body that is not JSON', '<html>'],
        ['a body that lists nothing', '{}'],
    ])('refuses %s', async (_, body) => {
        const answered = await listAgentConversations(session, answering({ status: 200, body }));

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('reads a refused grant as the reason it failed', async () => {
        const answered = await listAgentConversations(session, answering({ status: 403, body: '' }));

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unauthorized' } });
    });
});

describe('readAgentConversation', () => {
    it('reads from the beginning without naming a cursor, and past one with it', async () => {
        const first = recording({ status: 200, body: pageOf([]) });
        const later = recording({ status: 200, body: pageOf([]) });

        await readAgentConversation(session, first.transport, conversation, 0);
        await readAgentConversation(session, later.transport, conversation, 7);

        expect(first.requests[0]?.path).toBe(`${base}/${conversation}`);
        expect(later.requests[0]?.path).toBe(`${base}/${conversation}?since=7`);
    });

    it('reads every kind of entry a conversation is written as', async () => {
        const answered = await readAgentConversation(
            session,
            answering({
                status: 200,
                body: pageOf([
                    entryOf(1, {
                        entry: 'message',
                        messageId: question,
                        author: 'Person',
                        text: 'How many bays?',
                        scope: { kind: 'CalendarEvent', subject: 'event-1' },
                    }),
                    entryOf(2, { entry: 'answerStarted', messageId: question }),
                    entryOf(3, { entry: 'status', messageId: question, status: 'Reading' }),
                    entryOf(4, { entry: 'block', messageId: question, block: answerBlock }),
                    entryOf(5, { entry: 'proposal', messageId: question, block: answerBlock, act: {} }),
                    entryOf(6, { entry: 'answerEnded', messageId: question, outcome: 'Stopped' }),
                    entryOf(7, { entry: 'resolution', proposedAt: 5, state: 'Declined' }),
                    entryOf(8, {
                        entry: 'citation',
                        messageId: question,
                        citation: {
                            id: 'c-1',
                            target: { kind: 'email', email: 'email-1' },
                            label: 'The confirmation',
                            medium: 'Written',
                        },
                    }),
                ]),
            }),
            conversation,
            0,
        );

        expect(answered.outcome).toBe('read');
        expect(answered.outcome === 'read' ? answered.value.events : null).toMatchObject([
            {
                kind: 'message',
                sequence: 1,
                author: 'person',
                text: 'How many bays?',
                scope: { kind: 'calendarEvent', subject: 'event-1' },
            },
            { kind: 'answerStarted', sequence: 2, run: question },
            { kind: 'status', sequence: 3, run: question, status: 'Reading' },
            { kind: 'block', sequence: 4, run: question, block: { type: 'answer' } },
            { kind: 'proposal', sequence: 5, run: question, block: { type: 'answer' } },
            { kind: 'answerEnded', sequence: 6, run: question, outcome: 'stopped' },
            { kind: 'resolution', sequence: 7, proposedAt: 5, state: 'declined' },
            {
                kind: 'citation',
                sequence: 8,
                run: question,
                source: { id: 'c-1', target: { kind: 'email', email: 'email-1' }, label: 'The confirmation' },
            },
        ]);
    });

    it('reads an ending this build has no name for as a failed one', async () => {
        const answered = await readAgentConversation(
            session,
            answering({
                status: 200,
                body: pageOf([entryOf(1, { entry: 'answerEnded', messageId: question, outcome: 'Exploded' })]),
            }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ value: { events: [{ kind: 'answerEnded', outcome: 'failed' }] } });
    });

    it('moves past an entry kind this build does not read rather than refusing the conversation', async () => {
        const answered = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf([entryOf(4, { entry: 'somethingNew' })]) }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ value: { events: [{ kind: 'other', sequence: 4 }] } });
    });

    it('reads a conversation as still coming while an answer is composed or while more entries wait', async () => {
        const composing = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf([], true, false) }),
            conversation,
            0,
        );
        const paged = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf([], false, true) }),
            conversation,
            0,
        );
        const settled = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf([]) }),
            conversation,
            0,
        );

        expect(
            [composing, paged, settled].map((page) => (page.outcome === 'read' ? page.value.running : null)),
        ).toEqual([true, true, false]);
    });

    it('refuses an entry whose sequence is not a positive whole number', async () => {
        const answered = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf([entryOf(0, { entry: 'answerStarted', messageId: question })]) }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('refuses a message whose author is neither a person nor the agent', async () => {
        const answered = await readAgentConversation(
            session,
            answering({
                status: 200,
                body: pageOf([entryOf(1, { entry: 'message', messageId: question, author: 'Robot', text: 'x' })]),
            }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it.each([
        ['a body that is not JSON', '<html>'],
        ['a page that carries no entries', JSON.stringify({ composing: false, moreFollows: false })],
    ])('refuses %s', async (_, body) => {
        const answered = await readAgentConversation(session, answering({ status: 200, body }), conversation, 0);

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('refuses a citation whose source does not read', async () => {
        const answered = await readAgentConversation(
            session,
            answering({
                status: 200,
                body: pageOf([entryOf(1, { entry: 'citation', messageId: question, citation: { id: 'c-1' } })]),
            }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('refuses a page longer than the service ever answers with', async () => {
        const entries = Array.from({ length: 251 }, (_, index) =>
            entryOf(index + 1, { entry: 'answerStarted', messageId: question }),
        );

        const answered = await readAgentConversation(
            session,
            answering({ status: 200, body: pageOf(entries) }),
            conversation,
            0,
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('reads a conversation this person does not hold as missing', async () => {
        const answered = await readAgentConversation(session, answering({ status: 404, body: '' }), conversation, 0);

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'missing' } });
    });
});

describe('askAgent', () => {
    it('posts the question under its own identifier and answers with the answer it opened', async () => {
        const { transport, requests } = recording({
            status: 202,
            body: JSON.stringify({ messageId: question, runId: question, sequence: 1 }),
        });

        const answered = await askAgent(session, transport, conversation, question, 'How many bays?');

        expect(answered).toEqual({ outcome: 'read', value: { run: question } });
        expect(requests[0]).toMatchObject({
            method: 'POST',
            path: `${base}/${conversation}/messages`,
            body: JSON.stringify({ messageId: question, text: 'How many bays?' }),
        });
    });

    it('refuses an acceptance that names no answer', async () => {
        const answered = await askAgent(
            session,
            answering({ status: 202, body: '{}' }),
            conversation,
            question,
            'How many bays?',
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('reads anything but an acceptance as the reason it was not asked', async () => {
        const answered = await askAgent(
            session,
            answering({ status: 200, body: '{}' }),
            conversation,
            question,
            'How many bays?',
        );

        expect(answered.outcome).toBe('failed');
    });
});

describe('steerAgentRun', () => {
    it('posts the instruction into the answer being composed', async () => {
        const { transport, requests } = recording({ status: 202, body: JSON.stringify({ runId: question }) });

        const answered = await steerAgentRun(session, transport, conversation, question, 'm-2', 'Only this week');

        expect(answered).toEqual({ outcome: 'read', value: { run: question } });
        expect(requests[0]?.path).toBe(`${base}/${conversation}/runs/${question}/messages`);
    });

    it('reads an answer that is no longer there as missing', async () => {
        const answered = await steerAgentRun(
            session,
            answering({ status: 404, body: '' }),
            conversation,
            question,
            'm-2',
            'Only this week',
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'missing' } });
    });
});

describe('stopAgentRun', () => {
    it('deletes the answer being composed and reads a recorded stop as done', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        const answered = await stopAgentRun(session, transport, conversation, question);

        expect(answered).toEqual({ outcome: 'read', value: undefined });
        expect(requests[0]).toMatchObject({ method: 'DELETE', path: `${base}/${conversation}/runs/${question}` });
    });
});

describe('deleteAgentConversation', () => {
    it('deletes the conversation', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        const answered = await deleteAgentConversation(session, transport, conversation);

        expect(answered).toEqual({ outcome: 'read', value: undefined });
        expect(requests[0]).toMatchObject({ method: 'DELETE', path: `${base}/${conversation}` });
    });

    it('reads a conversation already gone as missing', async () => {
        const answered = await deleteAgentConversation(session, answering({ status: 404, body: '' }), conversation);

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'missing' } });
    });
});
