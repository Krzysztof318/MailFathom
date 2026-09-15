// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientRequest, ClientSession, MailFathomTransport, ManagedMailFolder } from '@mailfathom/client-backend';
import { act, fireEvent, renderHook, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { FolderMaintenanceProvider } from './FolderMaintenance';
import { useFolderMaintenance, type FolderMaintenance, type FolderMailbox } from './useFolderMaintenance';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const acts = ['rename', 'move', 'delete'] as const;

const inbox: ManagedMailFolder = { id: 'in', parentId: null, name: 'INBOX', role: 'Inbox', allowedActs: [] };
const contracts: ManagedMailFolder = {
    id: 'CONTRACTS',
    parentId: null,
    name: 'Contracts',
    role: null,
    allowedActs: [...acts],
};
const projects: ManagedMailFolder = {
    id: 'PROJECTS',
    parentId: null,
    name: 'Projects',
    role: null,
    allowedActs: [...acts],
};

const work: FolderMailbox = {
    accountId: 'work',
    accountName: 'Work',
    folders: [inbox, contracts, projects],
    creatableRoles: ['Trash'],
};

interface Answer {
    readonly status: number;
    readonly body: string;
}

/** What the deployment answers an accepted act with, which is the change it made and the folder it left. */
function changed(change: string, name = 'Contracts', mailErasureDeferred = false): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            change,
            folder: { id: 'CONTRACTS', parentId: null, name, role: null, allowedActs: [] },
            mailErasureDeferred,
        }),
    };
}

/** What it answers an act one of its own rules turned away with. */
function refusing(refusal: string): Answer {
    return { status: 409, body: JSON.stringify({ title: 'Refused', refusal }) };
}

function deployment(answering: (request: ClientRequest) => Answer = () => changed('Created')): {
    requests: ClientRequest[];
    transport: MailFathomTransport;
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...answering(request), headers: {} });
        },
    };
}

function maintaining(
    transport: MailFathomTransport,
    { offered = true, marksRead = true }: { offered?: boolean; marksRead?: boolean } = {},
): { held: () => FolderMaintenance } {
    function Surrounded({ children }: { readonly children: ReactNode }) {
        return (
            <LocalizationProvider>
                <ToastsProvider>
                    <FolderMaintenanceProvider
                        session={session}
                        transport={transport}
                        offered={offered}
                        marksRead={marksRead}
                    >
                        {children}
                    </FolderMaintenanceProvider>
                </ToastsProvider>
            </LocalizationProvider>
        );
    }

    const drawn = renderHook(() => useFolderMaintenance(), { wrapper: Surrounded });

    return { held: () => drawn.result.current };
}

/** What was asked of the managed-folder routes, which is the whole of what this client asked the mailbox to do. */
function asked(requests: readonly ClientRequest[]): { readonly path: string; readonly body: unknown }[] {
    return requests
        .filter((request) => request.method === 'POST')
        .map((request) => ({ path: request.path, body: JSON.parse(request.body ?? '{}') as unknown }));
}

const route = 'https://mail.example.invalid/api/client/managed-folders';

function typed(label: RegExp, value: string): void {
    fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

function chose(label: RegExp, value: string): void {
    fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe('FolderMaintenanceProvider', () => {
    it('offers nothing where the credential may neither change a folder nor mark mail read', () => {
        const { held } = maintaining(deployment().transport, { offered: false, marksRead: false });

        expect(held().offered).toBe(false);
        expect(held().marksRead).toBe(false);
    });

    it('opens the dialog on a folder that does not exist yet, saying which mailbox it is being made in', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().declare(work, null);
        });

        expect(screen.getByRole('heading', { name: 'New folder' })).toBeDefined();
        expect(screen.getByText('Work')).toBeDefined();
    });

    it('makes the folder where somebody named one, and says what was made', async () => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().declare(work, { id: 'PROJECTS', name: 'Projects' });
        });

        typed(/Folder name/, 'Contracts');
        fireEvent.click(screen.getByRole('button', { name: 'Create folder' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([
                { path: route, body: { account: 'work', parentId: 'PROJECTS', name: 'Contracts' } },
            ]);
        });

        expect(await screen.findByText('Created Contracts in Work.')).toBeDefined();
    });

    it('asks for a folder the mailbox files by with the role alone, nobody being asked to name their own trash', async () => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().declare(work, null);
        });

        chose(/Kind of folder/, 'Trash');

        expect(screen.queryByLabelText(/Folder name/)).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Create folder' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([{ path: route, body: { account: 'work', role: 'Trash' } }]);
        });
    });

    it('refuses to save a name a sibling already carries, before the deployment has to say so', () => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().declare(work, null);
        });

        typed(/Folder name/, 'Projects');

        expect(screen.getByText('This mailbox already has a folder of that name in the same place.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Create folder' })).toHaveProperty('disabled', true);
        expect(asked(answering.requests)).toEqual([]);
    });

    it('renames the folder where only the name moved', async () => {
        const answering = deployment(() => changed('Renamed', 'Contracts 2027'));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().revise(work, contracts);
        });

        expect(screen.getByRole('heading', { name: 'Edit folder' })).toBeDefined();

        typed(/Folder name/, 'Contracts 2027');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([
                {
                    path: `${route}/renames`,
                    body: { account: 'work', folderId: 'CONTRACTS', name: 'Contracts 2027' },
                },
            ]);
        });

        expect(await screen.findByText('Renamed to Contracts 2027 in Work.')).toBeDefined();
    });

    it('offers no name for a folder the mailbox files by, and still offers where it sits', async () => {
        const answering = deployment(() => changed('Moved', 'INBOX'));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().revise(work, inbox);
        });

        expect(screen.queryByLabelText(/Folder name/)).toBeNull();

        chose(/Inside/, 'PROJECTS');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([
                { path: `${route}/moves`, body: { account: 'work', folderId: 'in', parentId: 'PROJECTS' } },
            ]);
        });
    });

    it('moves the folder where only its place moved', async () => {
        const answering = deployment(() => changed('Moved'));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().revise(work, contracts);
        });

        chose(/Inside/, 'PROJECTS');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([
                { path: `${route}/moves`, body: { account: 'work', folderId: 'CONTRACTS', parentId: 'PROJECTS' } },
            ]);
        });

        expect(await screen.findByText('Moved Contracts in Work.')).toBeDefined();
    });

    it('asks for the rename first and the move behind it where both moved', async () => {
        const answering = deployment((request) =>
            request.path.endsWith('/renames') ? changed('Renamed', 'Deals') : changed('Moved', 'Deals'),
        );
        const { held } = maintaining(answering.transport);

        act(() => {
            held().revise(work, contracts);
        });

        typed(/Folder name/, 'Deals');
        chose(/Inside/, 'PROJECTS');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        await waitFor(() => {
            expect(asked(answering.requests).map((request) => request.path)).toEqual([
                `${route}/renames`,
                `${route}/moves`,
            ]);
        });

        expect(await screen.findByText('Moved Deals in Work.')).toBeDefined();
    });

    it('says the folder was renamed and not moved where the move behind the rename was refused', async () => {
        const answering = deployment((request) =>
            request.path.endsWith('/renames') ? changed('Renamed', 'Deals') : refusing('TooDeep'),
        );
        const { held } = maintaining(answering.transport);

        act(() => {
            held().revise(work, contracts);
        });

        typed(/Folder name/, 'Deals');
        chose(/Inside/, 'PROJECTS');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        expect(
            await screen.findByText('Renamed Deals, but it could not be moved. It is still where it was.'),
        ).toBeDefined();
    });

    it('refuses a save on a folder nobody changed, there being nothing to ask the deployment for', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().revise(work, contracts);
        });

        expect(screen.getByRole('button', { name: 'Save changes' })).toHaveProperty('disabled', true);
    });

    it('asks before it removes a folder, and says what may become of the mail rather than guessing', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: true });
        });

        expect(screen.getByRole('heading', { name: /Delete Contracts\?/ })).toBeDefined();
        expect(screen.getByText('Contracts leaves Work.')).toBeDefined();
        expect(screen.getByText('The folders inside it go with it.')).toBeDefined();
        expect(screen.getByText(/it may be moved to the trash, or erased here/)).toBeDefined();
    });

    it('asks for nothing where the question was left rather than answered', () => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(asked(answering.requests)).toEqual([]);
    });

    it.each([
        ['MovedToTrash', 'Moved Contracts to the trash, with everything in it.'],
        ['Erased', 'Erased Contracts. The mail it held is being removed.'],
        ['Deleted', 'Deleted Contracts on your mail server. The mail stored from it was removed.'],
        ['MarkedDeleted', 'Removed Contracts from MailFathom. Your mail server still holds it and its mail.'],
    ])('says what became of the mail when a deletion answered %s', async (change, said) => {
        const answering = deployment(() => changed(change));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        await waitFor(() => {
            expect(asked(answering.requests)).toEqual([
                { path: `${route}/deletions`, body: { account: 'work', folderId: 'CONTRACTS' } },
            ]);
        });

        expect(await screen.findByText(said)).toBeDefined();
    });

    it('says the mail is still stored where its erasure found the queue full', async () => {
        const answering = deployment(() => changed('Deleted', 'Contracts', true));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        expect(await screen.findByText(/The mail from Contracts is still stored/)).toBeDefined();
    });

    it('reports a rule that refused the act as a refusal rather than as a request that failed', async () => {
        const answering = deployment(() => refusing('ProtectedRole'));
        const { held } = maintaining(answering.transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        expect(
            await screen.findByText(
                'The change was refused. This is a folder the mailbox files by, and it stays as it is.',
            ),
        ).toBeDefined();
    });

    it('reports a deployment that did not answer as a failure, which is a different sentence', async () => {
        const { held } = maintaining(() => Promise.reject(new Error('no route to host')));

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        expect(await screen.findByText(/The folder could not be changed/)).toBeDefined();
    });
});
