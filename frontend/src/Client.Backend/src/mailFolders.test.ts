// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailFolders } from './mailFolders';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const inbox = {
    alias: 'INBOX',
    role: 'Inbox',
    path: ['INBOX'],
    storedEmailCount: 4213,
    unreadEmailCount: 12,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

function bodyOf(folders: readonly unknown[]): string {
    return JSON.stringify({ synchronizationEnabled: true, accounts: [{ account: workAccount, folders }] });
}

const treeBody = bodyOf([inbox]);

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

describe('readMailFolders', () => {
    it('asks for the folders route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: treeBody });

        await readMailFolders(session, transport);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/folders',
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            },
        ]);
    });

    it('reads the tree a well-formed answer describes', async () => {
        const result = await readMailFolders(session, answering({ status: 200, body: treeBody }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                synchronizationEnabled: true,
                accounts: [
                    {
                        account: {
                            id: 'work',
                            displayName: 'Work',
                            synchronizationState: 'Synchronized',
                            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                            behind: false,
                        },
                        folders: [
                            {
                                alias: 'INBOX',
                                role: 'Inbox',
                                path: ['INBOX'],
                                storedEmailCount: 4213,
                                unreadEmailCount: 12,
                                synchronizationState: 'Synchronized',
                                lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                                behind: false,
                            },
                        ],
                    },
                ],
            },
        });
    });

    it('reads a folder the deployment has not bound to a remote folder as one with no place on its server', async () => {
        const unbound = {
            ...inbox,
            role: null,
            path: [],
            synchronizationState: 'NeverSynchronized',
            lastSynchronizedAt: null,
            storedEmailCount: 0,
            unreadEmailCount: 0,
        };

        const result = await readMailFolders(session, answering({ status: 200, body: bodyOf([unbound]) }));

        expect(result.outcome === 'read' ? result.value.accounts[0]?.folders : null).toEqual([
            {
                alias: 'INBOX',
                role: null,
                path: [],
                storedEmailCount: 0,
                unreadEmailCount: 0,
                synchronizationState: 'NeverSynchronized',
                lastSynchronizedAt: null,
                behind: false,
            },
        ]);
    });

    it('reads an owner with no mail account as a tree with nothing in it', async () => {
        const empty = JSON.stringify({ synchronizationEnabled: false, accounts: [] });

        const result = await readMailFolders(session, answering({ status: 200, body: empty }));

        expect(result).toEqual({ outcome: 'read', value: { synchronizationEnabled: false, accounts: [] } });
    });

    it.each([
        { status: 401, reason: 'unauthenticated' },
        { status: 403, reason: 'unauthorized' },
        { status: 500, reason: 'unavailable' },
        { status: 503, reason: 'unavailable' },
    ])('reports $status as $reason rather than as a tree', async ({ status, reason }) => {
        const result = await readMailFolders(session, answering({ status, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await readMailFolders(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        { shape: 'a body that is not JSON', body: 'folders' },
        { shape: 'a body that is not an object', body: '[]' },
        { shape: 'an answer carrying no accounts array', body: JSON.stringify({ synchronizationEnabled: true }) },
        {
            shape: 'an account entry that is not an object',
            body: JSON.stringify({ synchronizationEnabled: true, accounts: ['work'] }),
        },
        {
            shape: 'an account entry carrying no account',
            body: JSON.stringify({ synchronizationEnabled: true, accounts: [{ folders: [] }] }),
        },
        { shape: 'a folder that is not an object', body: bodyOf(['INBOX']) },
        {
            shape: 'an account entry carrying no folders array',
            body: JSON.stringify({ synchronizationEnabled: true, accounts: [{ account: workAccount }] }),
        },
        { shape: 'a folder with no alias', body: bodyOf([{ ...inbox, alias: 42 }]) },
        { shape: 'a folder carrying a role this client does not know', body: bodyOf([{ ...inbox, role: 'Spam' }]) },
        { shape: 'a folder whose path is not levels', body: bodyOf([{ ...inbox, path: 'INBOX/2026' }]) },
        { shape: 'a folder whose path holds something other than a name', body: bodyOf([{ ...inbox, path: [7] }]) },
        { shape: 'a folder counting a fraction of a message', body: bodyOf([{ ...inbox, unreadEmailCount: 1.5 }]) },
        { shape: 'a folder counting fewer than none', body: bodyOf([{ ...inbox, storedEmailCount: -1 }]) },
        {
            shape: 'a folder in a state this client does not know',
            body: bodyOf([{ ...inbox, synchronizationState: 'Paused' }]),
        },
        {
            shape: 'a folder timed by something other than an instant',
            body: bodyOf([{ ...inbox, lastSynchronizedAt: 0 }]),
        },
    ])('refuses $shape rather than drawing a tree with a hole in it', async ({ body }) => {
        const result = await readMailFolders(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer carrying more accounts than this surface serves one owner', async () => {
        const accounts = Array.from({ length: 257 }, (_, index) => ({
            account: { ...workAccount, id: `account-${String(index)}` },
            folders: [],
        }));

        const result = await readMailFolders(
            session,
            answering({ status: 200, body: JSON.stringify({ synchronizationEnabled: true, accounts }) }),
        );

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an account carrying more folders than a mailbox has', async () => {
        const folders = Array.from({ length: 1025 }, (_, index) => ({ ...inbox, alias: `FOLDER-${String(index)}` }));

        const result = await readMailFolders(session, answering({ status: 200, body: bodyOf(folders) }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a folder nested deeper than a mail server nests one', async () => {
        const deep = { ...inbox, path: Array.from({ length: 33 }, (_, index) => `level-${String(index)}`) };

        const result = await readMailFolders(session, answering({ status: 200, body: bodyOf([deep]) }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
