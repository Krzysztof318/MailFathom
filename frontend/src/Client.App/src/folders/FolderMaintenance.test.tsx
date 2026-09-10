// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { act, fireEvent, renderHook, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { FolderMaintenanceProvider } from './FolderMaintenance';
import { useFolderMaintenance, type FolderMaintenance, type FolderMailbox } from './useFolderMaintenance';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const work: FolderMailbox = { accountId: 'work', accountName: 'Work', declaredAliases: ['INBOX'] };

/** A deployment answering the record's version and committing every write, recording what it was asked. */
function deployment(
    committed = true,
    messages: readonly string[] = [],
): {
    requests: ClientRequest[];
    transport: MailFathomTransport;
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const body =
                request.method === 'GET'
                    ? JSON.stringify({ version: 7 })
                    : JSON.stringify({ committed, version: committed ? 8 : 7, messages });

            return Promise.resolve({ status: 200, headers: {}, body });
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

/** What was posted to the record, which is the whole of what the deployment was asked to write down. */
function written(requests: readonly ClientRequest[]): { readonly path: string; readonly body: unknown }[] {
    return requests
        .filter((request) => request.method === 'POST')
        .map((request) => ({ path: request.path, body: JSON.parse(request.body ?? '{}') as unknown }));
}

function typed(label: RegExp, value: string): void {
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

    it('fills the remote path in from the name and from the folder the act was invoked on', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().declare(work, { alias: 'PROJECTS', remotePath: ['INBOX', 'Projects'] });
        });

        typed(/Folder name/, 'Contracts');

        expect(screen.getByLabelText(/Remote path/)).toHaveProperty('value', 'INBOX/Projects/Contracts');
    });

    it('declares the folder over the version the record stands at, and says what was made', async () => {
        const asked = deployment();
        const { held } = maintaining(asked.transport);

        act(() => {
            held().declare(work, null);
        });

        typed(/Folder name/, 'Contracts');
        fireEvent.click(screen.getByRole('button', { name: 'Create folder' }));

        await waitFor(() => {
            expect(written(asked.requests)).toEqual([
                {
                    path: 'https://mail.example.invalid/api/client/record/mail-accounts/folders',
                    body: {
                        version: 7,
                        accountId: 'work',
                        folder: JSON.stringify({
                            Alias: 'Contracts',
                            RemotePath: 'INBOX/Contracts',
                            CreateIfMissing: true,
                        }),
                    },
                },
            ]);
        });

        expect(await screen.findByText('Created INBOX/Contracts in Work.')).toBeDefined();
    });

    it('refuses to save a name the mailbox already has, before the deployment has to say so', () => {
        const asked = deployment();
        const { held } = maintaining(asked.transport);

        act(() => {
            held().declare({ ...work, declaredAliases: ['CONTRACTS'] }, null);
        });

        typed(/Folder name/, 'Contracts');

        expect(screen.getByText('This mailbox already has a folder of that name.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Create folder' })).toHaveProperty('disabled', true);
    });

    it('states a folder afresh in place of the alias it stands under, and says it was renamed', async () => {
        const asked = deployment();
        const { held } = maintaining(asked.transport);

        act(() => {
            held().revise(work, { alias: 'CONTRACTS', remotePath: ['INBOX', 'Contracts'] }, null);
        });

        expect(screen.getByRole('heading', { name: 'Edit folder' })).toBeDefined();

        typed(/Folder name/, 'Contracts 2027');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        await waitFor(() => {
            expect(written(asked.requests)[0]).toEqual({
                path: 'https://mail.example.invalid/api/client/record/mail-accounts/folders/replacement',
                body: {
                    version: 7,
                    accountId: 'work',
                    alias: 'CONTRACTS',
                    folder: JSON.stringify({
                        Alias: 'Contracts 2027',
                        RemotePath: 'INBOX/Contracts',
                        CreateIfMissing: true,
                    }),
                },
            });
        });
    });

    it('asks before it stops reading a folder, and says what stays behind rather than promising it goes', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().withdraw(work, {
                alias: 'CONTRACTS',
                remotePath: ['INBOX', 'Contracts'],
                name: 'Contracts',
                holdsNested: true,
            });
        });

        expect(screen.getByRole('heading', { name: /Delete Contracts\?/ })).toBeDefined();
        expect(screen.getByText('MailFathom stops reading Contracts in Work.')).toBeDefined();
        expect(
            screen.getByText('The mail already stored from it stays here, and the folder stays on your mail server.'),
        ).toBeDefined();
        expect(
            screen.getByText('The folders inside it stay, and are read as folders of their own from now on.'),
        ).toBeDefined();
    });

    it('writes nothing where the question was left rather than answered', () => {
        const asked = deployment();
        const { held } = maintaining(asked.transport);

        act(() => {
            held().withdraw(work, { alias: 'C', remotePath: ['C'], name: 'C', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(written(asked.requests)).toEqual([]);
    });

    it('withdraws the folder once somebody answers, and says the deployment stopped reading it', async () => {
        const asked = deployment();
        const { held } = maintaining(asked.transport);

        act(() => {
            held().withdraw(work, {
                alias: 'CONTRACTS',
                remotePath: ['INBOX', 'Contracts'],
                name: 'Contracts',
                holdsNested: false,
            });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        await waitFor(() => {
            expect(written(asked.requests)[0]).toEqual({
                path: 'https://mail.example.invalid/api/client/record/mail-accounts/folders/removal',
                body: { version: 7, accountId: 'work', alias: 'CONTRACTS' },
            });
        });

        expect(await screen.findByText('Stopped reading Contracts in Work.')).toBeDefined();
    });

    it('reports a refusal in the deployment’s own words rather than as a change that happened', async () => {
        const asked = deployment(false, ['That alias is already declared.']);
        const { held } = maintaining(asked.transport);

        act(() => {
            held().declare(work, null);
        });

        typed(/Folder name/, 'Contracts');
        fireEvent.click(screen.getByRole('button', { name: 'Create folder' }));

        expect(
            await screen.findByText('The deployment refused the change: That alias is already declared.'),
        ).toBeDefined();
    });
});
