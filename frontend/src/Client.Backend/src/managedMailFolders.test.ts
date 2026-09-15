// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    createManagedMailFolder,
    deleteManagedMailFolder,
    moveManagedMailFolder,
    readManagedMailFolders,
    renameManagedMailFolder,
    type ManagedMailFolderRefusal,
} from './managedMailFolders';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const account = '11111111-2222-4333-8444-555555555555';

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

function sent(requests: readonly ClientRequest[]): Readonly<Record<string, unknown>> {
    return JSON.parse(requests[0]?.body ?? '{}') as Readonly<Record<string, unknown>>;
}

function refusing(status: number, refusal: string): MailFathomTransport {
    return answering({ status, body: JSON.stringify({ title: 'Refused', refusal }) });
}

const report = {
    allowedActs: ['Create'],
    creatableRoles: ['Trash'],
    folders: [
        { id: 'INBOX', parentId: null, name: 'INBOX', role: 'Inbox', allowedActs: [] },
        { id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: ['Rename', 'Move', 'Delete'] },
    ],
};

const created: Answer = {
    status: 200,
    body: JSON.stringify({
        change: 'Created',
        folder: { id: 'CLIENTS', parentId: null, name: 'Clients', role: null, allowedActs: ['Rename'] },
        mailErasureDeferred: false,
    }),
};

describe('readManagedMailFolders', () => {
    it('asks the managed folders route for the account it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(report) });
        await readManagedMailFolders(session, transport, account);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/managed-folders?account=${account}`);
    });

    it('escapes an account identifier rather than writing it into the query as it stands', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(report) });
        await readManagedMailFolders(session, transport, 'a&b=c');

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/managed-folders?account=a%26b%3Dc');
    });

    it('reads the acts the account allows, the roles it may still be given, and its folders', async () => {
        const answer = await readManagedMailFolders(
            session,
            answering({ status: 200, body: JSON.stringify(report) }),
            account,
        );

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                allowedActs: ['create'],
                creatableRoles: ['Trash'],
                folders: [
                    { id: 'INBOX', parentId: null, name: 'INBOX', role: 'Inbox', allowedActs: [] },
                    {
                        id: 'PROJECTS',
                        parentId: null,
                        name: 'Projects',
                        role: null,
                        allowedActs: ['rename', 'move', 'delete'],
                    },
                ],
            },
        });
    });

    it('leaves out an act it does not know rather than offering one it cannot perform', async () => {
        const answer = await readManagedMailFolders(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    allowedActs: ['Create', 'Archive'],
                    creatableRoles: [],
                    folders: [],
                }),
            }),
            account,
        );

        expect(answer).toEqual({
            outcome: 'read',
            value: { allowedActs: ['create'], creatableRoles: [], folders: [] },
        });
    });

    it('leaves out a role it does not know, which is a deployment ahead of this client', async () => {
        const answer = await readManagedMailFolders(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ allowedActs: [], creatableRoles: ['Trash', 'Ledger'], folders: [] }),
            }),
            account,
        );

        expect(answer).toEqual({
            outcome: 'read',
            value: { allowedActs: [], creatableRoles: ['Trash'], folders: [] },
        });
    });

    it.each([
        ['a body that is not a record', '[]'],
        ['a body that is not JSON at all', 'not json'],
        ['a report naming no acts for the account', JSON.stringify({ creatableRoles: [], folders: [] })],
        [
            'a folder with no identity to act on',
            JSON.stringify({
                allowedActs: [],
                creatableRoles: [],
                folders: [{ parentId: null, name: 'Projects', role: null, allowedActs: [] }],
            }),
        ],
        [
            'a folder whose identity is empty, which names nothing',
            JSON.stringify({
                allowedActs: [],
                creatableRoles: [],
                folders: [{ id: '', parentId: null, name: 'Projects', role: null, allowedActs: [] }],
            }),
        ],
        ['a report whose creatable roles are not a list', JSON.stringify({ allowedActs: [], creatableRoles: 'Trash' })],
        ['a report naming no folders at all', JSON.stringify({ allowedActs: [], creatableRoles: [] })],
        [
            'a folder whose parent is named by nothing, which places it nowhere',
            JSON.stringify({
                allowedActs: [],
                creatableRoles: [],
                folders: [{ id: 'PROJECTS', parentId: '', name: 'Projects', role: null, allowedActs: [] }],
            }),
        ],
        [
            'a folder whose acts are not a list',
            JSON.stringify({
                allowedActs: [],
                creatableRoles: [],
                folders: [{ id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: 'Rename' }],
            }),
        ],
        [
            'a folder playing a role no mailbox has',
            JSON.stringify({
                allowedActs: [],
                creatableRoles: [],
                folders: [{ id: 'PROJECTS', parentId: null, name: 'Projects', role: 'Ledger', allowedActs: [] }],
            }),
        ],
    ])('refuses %s rather than reading a hierarchy with a hole in it', async (_, body) => {
        const answer = await readManagedMailFolders(session, answering({ status: 200, body }), account);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
    ])('reads %i as %s', async (status, reason) => {
        const answer = await readManagedMailFolders(session, answering({ status, body: '' }), account);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment that did not answer as unavailable with no status', async () => {
        const answer = await readManagedMailFolders(
            session,
            () => Promise.reject(new Error('no route to host')),
            account,
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('createManagedMailFolder', () => {
    it('posts the account, the parent, and the name a folder is being given', async () => {
        const { transport, requests } = recording(created);
        await createManagedMailFolder(session, transport, { account, parentId: 'PROJECTS', name: 'Clients' });

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/managed-folders');
        expect(sent(requests)).toEqual({ account, parentId: 'PROJECTS', name: 'Clients' });
    });

    it('posts the role alone where the folder is one the account files by, naming neither a name nor a parent', async () => {
        const { transport, requests } = recording(created);
        await createManagedMailFolder(session, transport, { account, role: 'Trash' });

        expect(sent(requests)).toEqual({ account, role: 'Trash' });
    });

    it('answers the change made and the folder as the act left it', async () => {
        const answer = await createManagedMailFolder(session, answering(created), {
            account,
            parentId: null,
            name: 'Clients',
        });

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                committed: true,
                change: 'created',
                folder: { id: 'CLIENTS', parentId: null, name: 'Clients', role: null, allowedActs: ['rename'] },
                mailErasureDeferred: false,
            },
        });
    });
});

describe('renameManagedMailFolder', () => {
    it('posts the folder it names and the name it is being given', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({
                change: 'Renamed',
                folder: { id: 'PROJECTS', parentId: null, name: 'Work', role: null, allowedActs: [] },
                mailErasureDeferred: false,
            }),
        });
        await renameManagedMailFolder(session, transport, { account, folderId: 'PROJECTS', name: 'Work' });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/managed-folders/renames');
        expect(sent(requests)).toEqual({ account, folderId: 'PROJECTS', name: 'Work' });
    });
});

describe('moveManagedMailFolder', () => {
    it('posts the top of the hierarchy as no parent rather than as a name for one', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({
                change: 'Moved',
                folder: { id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: [] },
                mailErasureDeferred: false,
            }),
        });
        await moveManagedMailFolder(session, transport, { account, folderId: 'PROJECTS', parentId: null });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/managed-folders/moves');
        expect(sent(requests)).toEqual({ account, folderId: 'PROJECTS', parentId: null });
    });
});

describe('deleteManagedMailFolder', () => {
    it('posts the folder alone, the act needing nothing else', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({
                change: 'MovedToTrash',
                folder: { id: 'PROJECTS', parentId: 'TRASH', name: 'Projects', role: null, allowedActs: [] },
                mailErasureDeferred: false,
            }),
        });
        await deleteManagedMailFolder(session, transport, { account, folderId: 'PROJECTS' });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/managed-folders/deletions');
        expect(sent(requests)).toEqual({ account, folderId: 'PROJECTS' });
    });

    it.each([
        ['MovedToTrash', 'movedToTrash'],
        ['Erased', 'erased'],
        ['Deleted', 'deleted'],
        ['MarkedDeleted', 'markedDeleted'],
    ])('reads %s as the change that says what became of the mail', async (stated, change) => {
        const answer = await deleteManagedMailFolder(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    change: stated,
                    folder: { id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: [] },
                    mailErasureDeferred: false,
                }),
            }),
            { account, folderId: 'PROJECTS' },
        );

        expect(answer.outcome === 'read' && answer.value.committed && answer.value.change).toBe(change);
    });

    it('reads a deferred erasure, which says the mail it held is stored a while longer', async () => {
        const answer = await deleteManagedMailFolder(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    change: 'Deleted',
                    folder: { id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: [] },
                    mailErasureDeferred: true,
                }),
            }),
            { account, folderId: 'PROJECTS' },
        );

        expect(answer.outcome === 'read' && answer.value.committed && answer.value.mailErasureDeferred).toBe(true);
    });

    it('refuses an accepted act whose body is not a record, rather than reporting an act nobody can read', async () => {
        const answer = await deleteManagedMailFolder(session, answering({ status: 200, body: 'not json' }), {
            account,
            folderId: 'PROJECTS',
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a change it does not know rather than reporting it as one of the seven it does', async () => {
        const answer = await deleteManagedMailFolder(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    change: 'Vanished',
                    folder: { id: 'PROJECTS', parentId: null, name: 'Projects', role: null, allowedActs: [] },
                    mailErasureDeferred: false,
                }),
            }),
            { account, folderId: 'PROJECTS' },
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('a refused act', () => {
    it.each<[number, string, ManagedMailFolderRefusal]>([
        [400, 'NameInvalid', 'nameInvalid'],
        [400, 'InboxNameAtTopLevel', 'inboxNameAtTopLevel'],
        [400, 'NameTaken', 'nameTaken'],
        [400, 'NestedInItself', 'nestedInItself'],
        [400, 'TooDeep', 'tooDeep'],
        [404, 'AccountMissing', 'accountMissing'],
        [404, 'FolderMissing', 'folderMissing'],
        [404, 'ParentMissing', 'parentMissing'],
        [409, 'AccountNotHeld', 'accountRestoring'],
        [409, 'ProtectedRole', 'protectedRole'],
        [409, 'TooManyFolders', 'tooManyFolders'],
        [409, 'RoleAlreadyPlayed', 'roleAlreadyPlayed'],
        [409, 'NotDeclaredByTheAccount', 'notDeclaredByTheAccount'],
        [500, 'NotRecorded', 'notRecorded'],
        [502, 'ServerRefused', 'serverRefused'],
        [503, 'ServerUnavailable', 'serverUnavailable'],
    ])('reads %i %s as a rule refusing the act rather than as a failed request', async (status, named, refusal) => {
        const answer = await renameManagedMailFolder(session, refusing(status, named), {
            account,
            folderId: 'PROJECTS',
            name: 'Work',
        });

        expect(answer).toEqual({ outcome: 'read', value: { committed: false, refusal } });
    });

    it('reads a refusal this client does not know as one rather than as a body it could not read', async () => {
        const answer = await deleteManagedMailFolder(session, refusing(409, 'MailboxLocked'), {
            account,
            folderId: 'PROJECTS',
        });

        expect(answer).toEqual({ outcome: 'read', value: { committed: false, refusal: 'refusedForAnotherReason' } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
    ])('reads %i as a failure, there being nothing to say about the folder', async (status, reason) => {
        const answer = await deleteManagedMailFolder(session, refusing(status, 'ProtectedRole'), {
            account,
            folderId: 'PROJECTS',
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reads an answer carrying no refusal as the failure its status names, which is something in the way', async () => {
        const answer = await deleteManagedMailFolder(session, answering({ status: 503, body: '' }), {
            account,
            folderId: 'PROJECTS',
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });
    });

    it('reports a deployment that did not answer at all as unavailable with no status', async () => {
        const answer = await createManagedMailFolder(session, () => Promise.reject(new Error('no route to host')), {
            account,
            parentId: null,
            name: 'Clients',
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
