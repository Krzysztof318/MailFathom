// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { changeOwnDisplayName, readOwnDisplayName } from './ownDisplayName';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const recordedBody = JSON.stringify({ displayName: 'Ada Lovelace', changeable: true });

// The transport is the network boundary and the whole of what a test here fakes. Neither route reads a header off an
// answer, so each helper supplies the empty set.
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

describe('readOwnDisplayName', () => {
    it('asks for the name route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: recordedBody });

        await readOwnDisplayName(session, transport);

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/display-name');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('reads the name and whether this deployment would take a correction of it', async () => {
        const answer = await readOwnDisplayName(session, answering({ status: 200, body: recordedBody }));

        expect(answer).toStrictEqual({ outcome: 'read', value: { displayName: 'Ada Lovelace', changeable: true } });
    });

    it('reads a name this deployment will not let the caller change as one that may not be changed', async () => {
        const answer = await readOwnDisplayName(
            session,
            answering({ status: 200, body: JSON.stringify({ displayName: 'Ada Lovelace', changeable: false }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: { displayName: 'Ada Lovelace', changeable: false } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await readOwnDisplayName(session, () => Promise.reject(new Error('nothing there')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answer = await readOwnDisplayName(session, answering({ status, body: '' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it.each([
        ['a body that is not JSON at all', 'not json'],
        ['a body that is not an object', '"Ada Lovelace"'],
        ['an answer carrying no name', JSON.stringify({ changeable: true })],
        ['an answer whose name is not a string', JSON.stringify({ displayName: 7, changeable: true })],
        ['an answer that does not say whether it may be changed', JSON.stringify({ displayName: 'Ada Lovelace' })],
    ])('refuses %s as unreadable', async (_, body) => {
        const answer = await readOwnDisplayName(session, answering({ status: 200, body }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('changeOwnDisplayName', () => {
    it('states the name as a document on the name route', async () => {
        const { transport, requests } = recording({ status: 200, body: recordedBody });

        await changeOwnDisplayName(session, transport, 'Ada Lovelace');

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/display-name');
        expect(requests[0]?.headers['Content-Type']).toBe('application/json');
        expect(requests[0]?.body).toBe(JSON.stringify({ displayName: 'Ada Lovelace' }));
    });

    it('answers the name as it was stored rather than as it was sent', async () => {
        const answer = await changeOwnDisplayName(
            session,
            answering({ status: 200, body: recordedBody }),
            '  Ada Lovelace  ',
        );

        expect(answer).toStrictEqual({ outcome: 'recorded', displayName: 'Ada Lovelace' });
    });

    it('separates a name the deployment would not accept from a failure to reach it', async () => {
        const answer = await changeOwnDisplayName(session, answering({ status: 400, body: '{}' }), '');

        expect(answer).toStrictEqual({ outcome: 'notAcceptable' });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [413, 'unavailable'],
    ])('reports %i as a failure to %s', async (status, reason) => {
        const answer = await changeOwnDisplayName(session, answering({ status, body: '' }), 'Ada Lovelace');

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment that did not answer as unavailable rather than throwing', async () => {
        const answer = await changeOwnDisplayName(
            session,
            () => Promise.reject(new Error('nothing there')),
            'Ada Lovelace',
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('refuses an accepted answer it cannot read as unreadable rather than reporting a name it did not get', async () => {
        const answer = await changeOwnDisplayName(session, answering({ status: 200, body: '{}' }), 'Ada Lovelace');

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
