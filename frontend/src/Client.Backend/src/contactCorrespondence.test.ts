// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostCorrespondenceEntries, readContactCorrespondence } from './contactCorrespondence';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const thread = {
    threadId: '0198f4c2-7d8e-7b2f-a041-52c637d8e9fa',
    latestMessageId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
    subject: 'Renewal terms',
    lastCorrespondedAt: '2026-03-04T09:01:12+00:00',
};

const document = {
    messageId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
    position: 1,
    fileName: 'renewal.pdf',
    mediaType: 'application/pdf',
    receivedAt: '2026-03-04T09:01:12+00:00',
};

const correspondenceBody = JSON.stringify({
    contactId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    threads: [thread],
    documents: [document],
});

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

describe('readContactCorrespondence', () => {
    it('asks for the correlation of the contact it names', async () => {
        const { transport, requests } = recording({ status: 200, body: correspondenceBody });

        await readContactCorrespondence(session, transport, '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90');

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/correspondence',
        );
    });

    it('reads the conversations and the documents a well-formed answer describes', async () => {
        const result = await readContactCorrespondence(
            session,
            answering({ status: 200, body: correspondenceBody }),
            'anna',
        );

        expect(result).toEqual({
            outcome: 'read',
            value: {
                contactId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                threads: [thread],
                documents: [document],
            },
        });
    });

    it('reads a contact the window holds no mail of as two empty lists', async () => {
        const body = JSON.stringify({ contactId: 'anna', threads: [], documents: [] });

        const result = await readContactCorrespondence(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: { contactId: 'anna', threads: [], documents: [] } });
    });

    it('reads a message that carried no subject and a part that carried no name', async () => {
        const body = JSON.stringify({
            contactId: 'anna',
            threads: [{ ...thread, subject: null }],
            documents: [{ ...document, fileName: null }],
        });

        const result = await readContactCorrespondence(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({
            outcome: 'read',
            value: {
                contactId: 'anna',
                threads: [{ ...thread, subject: null }],
                documents: [{ ...document, fileName: null }],
            },
        });
    });

    it('refuses an answer carrying more conversations than the route can report', async () => {
        const body = JSON.stringify({
            contactId: 'anna',
            threads: Array.from({ length: mostCorrespondenceEntries + 1 }, () => thread),
            documents: [],
        });

        const result = await readContactCorrespondence(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a body that is not a record', '[]'],
        ['a conversation naming no message', JSON.stringify({ contactId: 'a', threads: [{}], documents: [] })],
        [
            'a document whose position is not a whole number',
            JSON.stringify({ contactId: 'a', threads: [], documents: [{ ...document, position: 1.5 }] }),
        ],
        [
            'a document declaring no media type',
            JSON.stringify({ contactId: 'a', threads: [], documents: [{ ...document, mediaType: null }] }),
        ],
    ])('refuses %s', async (_, body) => {
        const result = await readContactCorrespondence(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a contact no book in this scope holds as something to let go of', async () => {
        const result = await readContactCorrespondence(session, answering({ status: 404, body: '' }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it('reads a grant that carries neither mail nor contacts as a refusal', async () => {
        const result = await readContactCorrespondence(session, answering({ status: 403, body: '' }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reports a deployment that never answered as one to retry', async () => {
        const result = await readContactCorrespondence(session, () => Promise.reject(new Error('refused')), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
