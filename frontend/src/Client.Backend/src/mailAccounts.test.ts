// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailAccounts } from './mailAccounts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const directoryBody = JSON.stringify({
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'work',
            displayName: 'Work',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
            behind: false,
        },
    ],
});

// The transport is the network boundary and the whole of what a test here fakes: it is a function the caller supplies,
// so nothing has to intercept a global or stand up a server to decide what came back.
// This operation reads no header off an answer, so the tests below name none and each helper supplies the empty set.
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

describe('readMailAccounts', () => {
    it('asks for the accounts route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: directoryBody });

        await readMailAccounts(session, transport);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/accounts',
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            },
        ]);
    });

    it('reads the directory a well-formed answer describes', async () => {
        const result = await readMailAccounts(session, answering({ status: 200, body: directoryBody }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                        behind: false,
                    },
                ],
            },
        });
    });

    it('reads an account that has never synchronized as one with no time on it', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: false,
            accounts: [
                { id: 'personal', displayName: 'Personal', synchronizationState: 'NeverSynchronized', behind: false },
            ],
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                synchronizationEnabled: false,
                accounts: [
                    {
                        id: 'personal',
                        displayName: 'Personal',
                        synchronizationState: 'NeverSynchronized',
                        lastSynchronizedAt: null,
                        behind: false,
                    },
                ],
            },
        });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('answers a %i with the failure it stands for rather than throwing', async (status, reason) => {
        const result = await readMailAccounts(session, answering({ status, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await readMailAccounts(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    // Every shape below is an answer the service could not have meant, and each is refused rather than read as a
    // directory with a hole in it: a screen that renders a missing display name as `undefined` is a defect reaching a
    // person, and `unreadable` is the reason that says the body was the problem rather than the deployment.
    it.each([
        ['a body that is not JSON', 'not json at all'],
        ['a body that is not an object', JSON.stringify([])],
        ['a directory with no synchronization switch', JSON.stringify({ accounts: [] })],
        ['a directory whose accounts are not an array', JSON.stringify({ synchronizationEnabled: true, accounts: {} })],
        [
            'an account with no display name',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [{ id: 'work', synchronizationState: 'Synchronized', behind: false }],
            }),
        ],
        [
            'an account whose behind flag is not a boolean',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [{ id: 'work', displayName: 'Work', synchronizationState: 'Synchronized', behind: 'maybe' }],
            }),
        ],
        [
            'an account in a synchronization state this client has no name for',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [{ id: 'work', displayName: 'Work', synchronizationState: 'Rebuilding', behind: false }],
            }),
        ],
        [
            'an account whose last synchronization is not a time',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        lastSynchronizedAt: 1756633260,
                        behind: false,
                    },
                ],
            }),
        ],
    ])('refuses %s as unreadable', async (_, body) => {
        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a directory larger than the ceiling before it walks it', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: Array.from({ length: 257 }, (_, index) => ({
                id: `account-${String(index)}`,
                displayName: `Account ${String(index)}`,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
            })),
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a directory at the ceiling, so the bound refuses more than it rather than the maximum', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: Array.from({ length: 256 }, (_, index) => ({
                id: `account-${String(index)}`,
                displayName: `Account ${String(index)}`,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
            })),
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result.outcome).toBe('read');
    });
});
