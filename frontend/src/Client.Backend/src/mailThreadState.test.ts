// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mailThreadStateRoute, readMailThreadState } from './mailThreadState';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const threadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

const firstMessage = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';

const secondMessage = '3a8e5039-7d2f-4f1b-8b30-2c1daf9e8071';

const agreement = {
    aspect: 'Agreement',
    text: 'The response time stays at two hours.',
    owedBy: null,
    dueAt: null,
    sources: [{ kind: 'email', email: firstMessage }],
};

function bodyOf(state: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        threadId,
        coverage: 'WholeThread',
        derivedAt: '2026-09-08T09:00:00+00:00',
        entries: [agreement],
        ...state,
    });
}

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

describe('mailThreadStateRoute', () => {
    it('names the conversation in the path', () => {
        expect(mailThreadStateRoute(threadId)).toBe(`/threads/${threadId}/state`);
    });

    it('escapes an identifier that is not the shape the route matches', () => {
        expect(mailThreadStateRoute('../emails')).toBe('/threads/..%2Femails/state');
    });
});

describe('readMailThreadState', () => {
    it('reads the statements, in the order the deployment put them', async () => {
        const commitment = {
            aspect: 'Commitment',
            text: 'Karolina sends the revised figures.',
            owedBy: 'Karolina',
            dueAt: '2026-09-12T00:00:00+00:00',
            sources: [{ kind: 'email', email: secondMessage }],
        };
        const answered = await readMailThreadState(
            session,
            answering({ status: 200, body: bodyOf({ entries: [agreement, commitment] }) }),
            threadId,
        );

        expect(answered).toStrictEqual({
            outcome: 'read',
            value: {
                threadId,
                coverage: 'WholeThread',
                derivedAt: '2026-09-08T09:00:00+00:00',
                entries: [agreement, commitment],
            },
        });
    });

    it('reaches the state on the client surface of the deployment it signed in to', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf() });

        await readMailThreadState(session, transport, threadId);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/threads/${threadId}/state`);
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    // The state a screen draws where nothing has been derived: a deployment that never turned it on, one that has not
    // reached this conversation, and a conversation this user does not hold all answer the same way.
    it('reads a conversation this deployment has no state for as an absence rather than as a failure', async () => {
        const answered = await readMailThreadState(session, answering({ status: 404, body: '' }), threadId);

        expect(answered).toStrictEqual({ outcome: 'read', value: null });
    });

    it('reads a conversation too long to derive as the coverage that says so', async () => {
        const answered = await readMailThreadState(
            session,
            answering({ status: 200, body: bodyOf({ coverage: 'ThreadTooLarge', entries: [] }) }),
            threadId,
        );

        expect(answered.outcome === 'read' && answered.value?.coverage).toBe('ThreadTooLarge');
        expect(answered.outcome === 'read' && answered.value?.entries).toStrictEqual([]);
    });

    it('reads a commitment the conversation named nobody and no date for', async () => {
        const unowned = { ...agreement, aspect: 'Commitment' };
        const answered = await readMailThreadState(
            session,
            answering({ status: 200, body: bodyOf({ entries: [unowned] }) }),
            threadId,
        );

        expect(answered.outcome === 'read' && answered.value?.entries).toStrictEqual([unowned]);
    });

    it('reports a deployment that could not be reached', async () => {
        const answered = await readMailThreadState(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            threadId,
        );

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('reports a caller the deployment holds no grant for', async () => {
        const answered = await readMailThreadState(session, answering({ status: 403, body: '' }), threadId);

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    // A statement with no message behind it is the one thing the record exists to rule out, so the block is refused
    // rather than drawn without its evidence.
    it.each([
        ['a statement resting on no message', { entries: [{ ...agreement, sources: [] }] }],
        [
            'a statement resting on a source of another kind',
            { entries: [{ ...agreement, sources: [{ kind: 'fragment', email: firstMessage }] }] },
        ],
        ['a statement of an aspect this client cannot draw', { entries: [{ ...agreement, aspect: 'Mood' }] }],
        ['a statement saying nothing', { entries: [{ ...agreement, text: '' }] }],
        ['a coverage this client cannot draw', { coverage: 'Partial' }],
        ['a derivation instant nothing can date', { derivedAt: 'the other day' }],
        ['a due date nothing can date', { entries: [{ ...agreement, aspect: 'Commitment', dueAt: 'soon' }] }],
        ['a statement that is not a record at all', { entries: ['settled'] }],
        ['an owner that is not a name', { entries: [{ ...agreement, aspect: 'Commitment', owedBy: 7 }] }],
        ['sources that are not a list', { entries: [{ ...agreement, sources: 'the first one' }] }],
        ['statements that are not a list', { entries: 'three' }],
        ['no conversation identity at all', { threadId: '' }],
    ])('refuses a block carrying %s', async (_, overrides) => {
        const answered = await readMailThreadState(
            session,
            answering({ status: 200, body: bodyOf(overrides) }),
            threadId,
        );

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['nothing that parses at all', 'not json at all'],
        ['a document rather than an object', '[]'],
    ])('refuses an answer that is %s', async (_, body) => {
        const answered = await readMailThreadState(session, answering({ status: 200, body }), threadId);

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
