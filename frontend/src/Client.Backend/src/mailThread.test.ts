// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { longestDrawnThreadPage, mailThreadRoute, readMailThread, threadQueryString } from './mailThread';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const threadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

/** A message the conversation does not hold, for the answers that pair a row with somebody else's head or words. */
const anotherMessage = '7c3e5a91-2d84-4b6f-8e15-40a9c7b2d6e3';

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
    toAddresses: ['user@example.invalid'],
    unread: true,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 84_213,
    preview: 'The figures you asked for are attached.',
    threadMessageCount: 2,
};

const participant = { address: 'auditor@example.invalid', displayName: 'The auditor', messageCount: 2 };

// What a conversation read with its messages carries beside each row: the message as the service describes it, and the
// words it says. Both are the shapes the message and body routes answer with, which is why nothing here parses them a
// second way.
const described = {
    storedEmailId: email.id,
    account: 'work',
    folder: 'INBOX',
    threadId,
    sizeOctets: 84_213,
    headers: {
        subject: 'The quarterly figures',
        sentAt: '2026-08-31T09:40:00+00:00',
        receivedAt: '2026-08-31T09:41:00+00:00',
        participants: [{ role: 'From', address: 'auditor@example.invalid', displayName: 'The auditor' }],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    body: { availability: 'Readable', plainText: true, html: false },
    sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
    attachments: [],
    carried: null,
    unread: true,
    flagged: false,
    answered: false,
};

const said = {
    storedEmailId: email.id,
    availability: 'Readable',
    plainText: { text: 'The figures you asked for are attached.', originalCharacterCount: 38, truncation: 'None' },
    document: null,
    selfContainedHtml: null,
    remoteImagesRequested: false,
};

/** A page of the shape the drawn read answers with: at most ten messages, each carrying its description and words. */
function drawnBodyOf(page: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        threadId,
        messages: [{ position: 0, answeredId: null, email, message: described, body: said }],
        participants: [participant],
        messageCount: 1,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: longestDrawnThreadPage,
        ...page,
    });
}

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

    // A conversation drawn out asks for the messages themselves, and for the smaller page the service composes them
    // at: the whole correspondence a screen shows arrives in that one answer rather than one read per message drawn.
    it('asks for the messages themselves, at the page the service composes them at', () => {
        expect(threadQueryString(null, true)).toBe(`?pageSize=${String(longestDrawnThreadPage)}&content=true`);
    });

    it('reads on from a cursor with the messages still drawn', () => {
        expect(threadQueryString('onwards', true)).toBe(
            `?pageSize=${String(longestDrawnThreadPage)}&cursor=onwards&content=true`,
        );
    });
});

describe('readMailThread', () => {
    it('reads a conversation, its participants, and what is true of the whole of it', async () => {
        const answered = await readMailThread(session, answering({ status: 200, body: bodyOf() }), threadId, null);

        expect(answered).toStrictEqual({
            outcome: 'read',
            value: {
                threadId,
                messages: [
                    { position: 0, answeredId: null, email: { ...email, enrichment: null }, message: null, body: null },
                ],
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

        expect(answered.outcome === 'read' && answered.value.messages[1]).toStrictEqual({
            ...answer,
            email: { ...answer.email, enrichment: null },
            message: null,
            body: null,
        });
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

describe('readMailThread drawing the messages', () => {
    it('reads every message of the conversation with its description and its words', async () => {
        const answered = await readMailThread(
            session,
            answering({ status: 200, body: drawnBodyOf() }),
            threadId,
            null,
            true,
        );

        expect(answered.outcome === 'read' && answered.value.messages[0]?.message).toStrictEqual(described);
        expect(answered.outcome === 'read' && answered.value.messages[0]?.body).toStrictEqual(said);
    });

    it('asks the deployment for the conversation and its messages in one request', async () => {
        const { transport, requests } = recording({ status: 200, body: drawnBodyOf() });

        await readMailThread(session, transport, threadId, null, true);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/threads/${threadId}` +
                `?pageSize=${String(longestDrawnThreadPage)}&content=true`,
        );
    });

    // A message whose local copy the deployment could not open arrives without one. That is a gap in the
    // correspondence for the screen to say, rather than a page to refuse.
    it('reads a message the deployment could not open as one carrying neither description nor words', async () => {
        const answered = await readMailThread(
            session,
            answering({
                status: 200,
                body: drawnBodyOf({ messages: [{ position: 0, answeredId: null, email }] }),
            }),
            threadId,
            null,
            true,
        );

        expect(answered.outcome === 'read' && answered.value.messages[0]?.message).toBeNull();
        expect(answered.outcome === 'read' && answered.value.messages[0]?.body).toBeNull();
    });

    it.each([
        ['a page larger than the one messages are composed at', drawnBodyOf({ pageSize: longestDrawnThreadPage + 1 })],
        [
            'a description that is not one',
            drawnBodyOf({ messages: [{ position: 0, answeredId: null, email, message: {}, body: said }] }),
        ],
        [
            'words that are not words',
            drawnBodyOf({ messages: [{ position: 0, answeredId: null, email, message: described, body: {} }] }),
        ],
        // The row, the head and the words are three identities parsed apart, and a screen handed a mismatched trio
        // would draw one message's row beside another message's words with nothing saying so.
        [
            'a description belonging to another message',
            drawnBodyOf({
                messages: [
                    {
                        position: 0,
                        answeredId: null,
                        email,
                        message: { ...described, storedEmailId: anotherMessage },
                        body: said,
                    },
                ],
            }),
        ],
        [
            'words belonging to another message',
            drawnBodyOf({
                messages: [
                    {
                        position: 0,
                        answeredId: null,
                        email,
                        message: described,
                        body: { ...said, storedEmailId: anotherMessage },
                    },
                ],
            }),
        ],
    ])('refuses %s rather than drawing a conversation with a hole in it', async (_, body) => {
        const answered = await readMailThread(session, answering({ status: 200, body }), threadId, null, true);

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a conversation drawn at the page a read without its messages serves', async () => {
        const answered = await readMailThread(
            session,
            answering({ status: 200, body: drawnBodyOf({ pageSize: 100 }) }),
            threadId,
            null,
            true,
        );

        expect(answered).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
