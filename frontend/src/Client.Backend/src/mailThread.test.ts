// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mailThreadRoute, readMailThread, threadQueryString } from './mailThread';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const threadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

const email = {
    id: '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60',
    account: 'work',
    folder: 'INBOX',
    threadId,
    subject: 'The quarterly figures',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: '2026-08-31T09:40:00+00:00',
    senderAddress: 'auditor@example.invalid',
    senderDisplayName: 'The auditor',
    toAddresses: ['owner@example.invalid'],
    unread: true,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 84_213,
    preview: 'The figures you asked for are attached.',
};

const participant = { address: 'auditor@example.invalid', displayName: 'The auditor', messageCount: 2 };

function bodyOf(page: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        threadId,
        messages: [{ position: 0, answeredId: null, email }],
        participants: [participant],
        messageCount: 1,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: 100,
        ...page,
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

describe('mailThreadRoute', () => {
    it('names the conversation in the path', () => {
        expect(mailThreadRoute(threadId)).toBe(`/threads/${threadId}`);
    });

    it('escapes an identifier that is not the shape the route matches', () => {
        expect(mailThreadRoute('../emails')).toBe('/threads/..%2Femails');
    });
});

describe('threadQueryString', () => {
    it('asks for the whole page the surface serves, so a conversation is one read wherever it fits in one', () => {
        expect(threadQueryString(null)).toBe('?pageSize=100');
    });

    it('escapes the cursor a previous page answered with', () => {
        expect(threadQueryString('a+b/c=')).toBe('?pageSize=100&cursor=a%2Bb%2Fc%3D');
    });
});

describe('readMailThread', () => {
    it('reads a conversation, its participants, and what is true of the whole of it', async () => {
        const answered = await readMailThread(session, answering({ status: 200, body: bodyOf() }), threadId, null);

        expect(answered).toStrictEqual({
            outcome: 'read',
            value: {
                threadId,
                messages: [{ position: 0, answeredId: null, email }],
                participants: [participant],
                messageCount: 1,
                moreMessagesNotAssembled: false,
                moreParticipantsNotNamed: false,
                nextCursor: null,
                pageSize: 100,
            },
        });
    });

    it('reaches the conversation on the client surface of the deployment it signed in to', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf() });

        await readMailThread(session, transport, threadId, 'onwards');

        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/threads/${threadId}?pageSize=100&cursor=onwards`,
        );
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('reads a message that answers another one among those shown', async () => {
        const answer = { position: 1, answeredId: email.id, email: { ...email, id: 'a-second-message' } };
        const answered = await readMailThread(
            session,
            answering({ status: 200, body: bodyOf({ messages: [{ position: 0, answeredId: null, email }, answer] }) }),
            threadId,
            null,
        );

        expect(answered.outcome === 'read' && answered.value.messages[1]).toStrictEqual(answer);
    });

    it('reads a conversation that names an author for whom no message carried a display name', async () => {
        const unnamed = { address: 'nobody@example.invalid', displayName: null, messageCount: 1 };
        const answered = await readMailThread(
            session,
            answering({ status: 200, body: bodyOf({ participants: [unnamed] }) }),
            threadId,
            null,
        );

        expect(answered.outcome === 'read' && answered.value.participants).toStrictEqual([unnamed]);
    });

    it('reads a conversation that runs past what one read assembles as one that says so', async () => {
        const answered = await readMailThread(
            session,
            answering({
                status: 200,
                body: bodyOf({ moreMessagesNotAssembled: true, moreParticipantsNotNamed: true, messageCount: 500 }),
            }),
            threadId,
            null,
        );

        expect(answered.outcome === 'read' && answered.value.moreMessagesNotAssembled).toBe(true);
        expect(answered.outcome === 'read' && answered.value.moreParticipantsNotNamed).toBe(true);
    });

    it('reads a deployment that never answered as unavailable', async () => {
        const answered = await readMailThread(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            threadId,
            null,
        );

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [400, 'unavailable'],
        [500, 'unavailable'],
    ])('reads %i as %s', async (status, reason) => {
        const answered = await readMailThread(session, answering({ status, body: '' }), threadId, null);

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it.each([
        ['a body that is not JSON at all', 'not json'],
        ['a body that is not an object', JSON.stringify([])],
        ['a conversation naming no identity', bodyOf({ threadId: null })],
        ['a count that is not a whole number', bodyOf({ messageCount: 1.5 })],
        ['a page size the surface does not serve', bodyOf({ pageSize: 500 })],
        ['a page size below one message', bodyOf({ pageSize: 0 })],
        [
            'more messages than the page it was read under admits',
            bodyOf({
                pageSize: 1,
                messages: [
                    { position: 0, answeredId: null, email },
                    { position: 1, answeredId: null, email },
                ],
            }),
        ],
        ['a flag that is not a flag', bodyOf({ moreMessagesNotAssembled: 'yes' })],
        ['a cursor that is empty rather than absent', bodyOf({ nextCursor: '' })],
        ['messages that are not a list', bodyOf({ messages: {} })],
        ['a message with no place in the conversation', bodyOf({ messages: [{ answeredId: null, email }] })],
        [
            'a message whose row is missing a field',
            bodyOf({ messages: [{ position: 0, answeredId: null, email: {} }] }),
        ],
        ['participants that are not a list', bodyOf({ participants: null })],
        ['an author with no address', bodyOf({ participants: [{ ...participant, address: '' }] })],
        [
            'an author whose share of the conversation is negative',
            bodyOf({ participants: [{ ...participant, messageCount: -1 }] }),
        ],
    ])('refuses %s rather than drawing a conversation with a hole in it', async (_, body) => {
        const answered = await readMailThread(session, answering({ status: 200, body }), threadId, null);

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a page holding more messages than the surface serves at all', async () => {
        const messages = Array.from({ length: 101 }, (_, at) => ({ position: at, answeredId: null, email }));
        const answered = await readMailThread(
            session,
            answering({ status: 200, body: bodyOf({ messages }) }),
            threadId,
            null,
        );

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
