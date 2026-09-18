// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    eraseContact,
    promoteContact,
    readCollectedContacts,
    readContact,
    readOwnContacts,
    recordContact,
    type ContactRecord,
} from './contacts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asserted = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    displayName: 'Anna Kowalska',
    addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
    preferredAddress: 'anna@example.invalid',
    note: 'Renewal owner',
    origin: 'Asserted',
    recordedAt: '2026-03-01T08:00:00+00:00',
    amendedAt: '2026-03-04T09:01:12+00:00',
};

const pageBody = JSON.stringify({ contacts: [asserted], nextCursor: 'page-two' });

// The transport is the network boundary and the whole of what a test here fakes. No operation in this module reads a
// header off an answer, so every helper supplies the empty set.
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

const record: ContactRecord = {
    displayName: 'Anna Kowalska',
    addresses: ['anna@example.invalid'],
    preferredAddress: 'anna@example.invalid',
    note: null,
};

describe('readOwnContacts', () => {
    it('asks for the asserted book with the window it will render', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts?pageSize=50');
    });

    it('carries the cursor a previous page answered with', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { pageSize: 25, cursor: 'page two/3' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts?pageSize=25&cursor=page%20two%2F3',
        );
    });

    it('asks for no more than the page the route will serve', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { pageSize: 5000 });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts?pageSize=200');
    });

    it('reads the page a well-formed answer describes', async () => {
        const result = await readOwnContacts(session, answering({ status: 200, body: pageBody }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                contacts: [
                    {
                        id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                        displayName: 'Anna Kowalska',
                        addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
                        preferredAddress: 'anna@example.invalid',
                        note: 'Renewal owner',
                        origin: 'Asserted',
                        recordedAt: '2026-03-01T08:00:00+00:00',
                        amendedAt: '2026-03-04T09:01:12+00:00',
                    },
                ],
                nextCursor: 'page-two',
            },
        });
    });

    it('reads the end of the book as a page carrying no cursor', async () => {
        const body = JSON.stringify({ contacts: [], nextCursor: null });

        const result = await readOwnContacts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'read', value: { contacts: [], nextCursor: null } });
    });

    it('refuses an answer carrying more contacts than the page it asked for', async () => {
        const body = JSON.stringify({ contacts: [asserted, asserted], nextCursor: null });

        const result = await readOwnContacts(session, answering({ status: 200, body }), { pageSize: 1 });

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a body that is not a record', '[]'],
        ['a contact carrying no addresses at all', JSON.stringify({ contacts: [{ ...asserted, addresses: [] }] })],
        ['an origin this surface does not publish', JSON.stringify({ contacts: [{ ...asserted, origin: 'Guessed' }] })],
        ['an address that is not text', JSON.stringify({ contacts: [{ ...asserted, addresses: [7] }] })],
        ['a cursor that is not text', JSON.stringify({ contacts: [], nextCursor: 3 })],
    ])('refuses %s', async (_, body) => {
        const result = await readOwnContacts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that never answered as one to retry', async () => {
        const result = await readOwnContacts(session, () => Promise.reject(new Error('refused')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reads a %i as %s', async (status, reason) => {
        const result = await readOwnContacts(session, answering({ status, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });
});

describe('readCollectedContacts', () => {
    it('asks for the collected book rather than the asserted one', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readCollectedContacts(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts/collected?pageSize=50');
    });
});

describe('readContact', () => {
    it('asks for the contact the identity names', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await readContact(session, transport, '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90');

        expect(requests[0]).toEqual({
            method: 'GET',
            path: 'https://mail.example.invalid/api/client/contacts/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
            headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 32768,
        });
    });

    it('reads a contact the books no longer hold as something to let go of', async () => {
        const result = await readContact(session, answering({ status: 404, body: '' }), 'gone');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('recordContact', () => {
    it('states the whole record on the asserted book', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null }),
        });

        await recordContact(session, transport, record);

        expect(requests[0]).toEqual({
            method: 'POST',
            path: 'https://mail.example.invalid/api/client/contacts',
            headers: {
                Accept: 'application/json',
                Authorization: 'Basic dGVzdA==',
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(record),
            longestAnswer: 32768,
        });
    });

    it('reads a write the book performed as the record it settled', async () => {
        const body = JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({
            outcome: 'read',
            value: {
                outcome: 'Written',
                contact: {
                    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                    displayName: 'Anna Kowalska',
                    addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
                    preferredAddress: 'anna@example.invalid',
                    note: 'Renewal owner',
                    origin: 'Asserted',
                    recordedAt: '2026-03-01T08:00:00+00:00',
                    amendedAt: '2026-03-04T09:01:12+00:00',
                },
                addressHolder: null,
            },
        });
    });

    it('reads an address another contact already holds as an outcome rather than a failure', async () => {
        const body = JSON.stringify({
            outcome: 'AddressHeldByAnotherContact',
            contact: null,
            addressHolder: '0198f4c2-7d8e-7b2f-a041-52c637d8e9fa',
        });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({
            outcome: 'read',
            value: {
                outcome: 'AddressHeldByAnotherContact',
                contact: null,
                addressHolder: '0198f4c2-7d8e-7b2f-a041-52c637d8e9fa',
            },
        });
    });

    it('refuses an outcome this surface does not publish', async () => {
        const body = JSON.stringify({ outcome: 'Maybe', contact: null, addressHolder: null });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a record the request itself broke as a refusal naming the rule', async () => {
        const result = await recordContact(session, answering({ status: 400, body: '' }), record);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 400 } });
    });
});

describe('promoteContact', () => {
    it('asks the promotion route for the contact it names, stating no record', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: null, addressHolder: null }),
        });

        await promoteContact(session, transport, '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90');

        expect(requests[0]).toEqual({
            method: 'POST',
            path: 'https://mail.example.invalid/api/client/contacts/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/promotion',
            headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 32768,
        });
    });

    it('reads a promotion of somebody already asserted as an outcome rather than a failure', async () => {
        const body = JSON.stringify({ outcome: 'AlreadyAsserted', contact: null, addressHolder: null });

        const result = await promoteContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({
            outcome: 'read',
            value: { outcome: 'AlreadyAsserted', contact: null, addressHolder: null },
        });
    });
});

describe('eraseContact', () => {
    it('asks the contact route to erase the person it names', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ contact: 'anna', wasHeld: true, addressesErased: 2 }),
        });

        await eraseContact(session, transport, 'anna');

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts/anna');
    });

    it('reads what the erasure removed', async () => {
        const body = JSON.stringify({ contact: 'anna', wasHeld: true, addressesErased: 2 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: { contact: 'anna', wasHeld: true, addressesErased: 2 } });
    });

    it('reads a book that held nobody of that identity as an erasure that removed nothing', async () => {
        const body = JSON.stringify({ contact: 'anna', wasHeld: false, addressesErased: 0 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: { contact: 'anna', wasHeld: false, addressesErased: 0 } });
    });

    it('refuses an answer that does not say whether the book held them', async () => {
        const body = JSON.stringify({ contact: 'anna', addressesErased: 0 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
