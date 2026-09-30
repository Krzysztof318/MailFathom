// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientRequest, ClientSession, MailFathomTransport, ManagedMailFolder } from '@mailfathom/client-backend';
import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { flagsRecorded } from '../../../../tests/fixtures/changes';
import { emptyFolderPage, mailboxSize, rowsPerPage, timelinePage } from '../../../../tests/fixtures/mail';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { FolderMaintenanceProvider } from './FolderMaintenance';
import { mostPagesMarkedRead } from './markingEverythingRead';
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

// A mailbox whose deepest folder already sits at the ceiling the column is drawn to.
const nested: FolderMailbox = {
    ...work,
    folders: [
        inbox,
        projects,
        { id: '2026', parentId: 'PROJECTS', name: '2026', role: null, allowedActs: [...acts] },
        { id: 'Q3', parentId: '2026', name: 'Q3', role: null, allowedActs: [...acts] },
    ],
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

/** A deployment that never answers, so what is asserted is the card standing while an act runs. */
const unanswered: MailFathomTransport = () => new Promise(() => undefined);

/** A deployment answering each page of unread mail from the cursor it was asked at, and recording every flag change. */
function mailAnswering(pages: (cursor: string | null) => unknown): MailFathomTransport {
    return (request) =>
        Promise.resolve({
            status: 200,
            headers: {},
            body: JSON.stringify(
                request.method === 'GET' ? pages(new URL(request.path).searchParams.get('cursor')) : recording(request),
            ),
        });
}

// The corpus's recorded flag change, answered for each message the request named, as the route answers one per message.
function recording(request: ClientRequest): unknown {
    const written = JSON.parse(request.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

    return {
        results: written.changes.map(({ storedEmailId }) => ({ ...flagsRecorded.results[0], storedEmailId })),
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

    it('says what it is creating while a role is being asked for, there being no typed name to say', async () => {
        // A deployment that never answers, so what is asserted is the card standing while the act runs rather than
        // whatever it settles to.
        const { held } = maintaining(() => new Promise(() => undefined));

        act(() => {
            held().declare(work, { id: 'PROJECTS', name: 'Projects' });
        });

        expect(screen.getByText('Work — inside “Projects”')).toBeDefined();

        chose(/Kind of folder/, 'Trash');

        // The folder goes where the service puts such a folder rather than inside the row the dialog was opened on,
        // so the dialog stops naming that row the moment the role is chosen.
        expect(screen.queryByText('Work — inside “Projects”')).toBeNull();
        expect(screen.getByText('Work')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Create folder' }));

        expect(await screen.findByText('Creating Trash in Work…')).toBeDefined();
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

        // One save is one change however many requests it took: counting each of them would read the whole tree, and
        // every account's report beside it, twice for one press.
        expect(held().changed).toBe(1);
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

        // Counted all the same: the rename committed, so a column still drawing the old name is a column reading
        // something that is no longer true.
        expect(held().changed).toBe(1);
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

    it('says nothing under a name nobody has typed, and draws the create button flat instead', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().declare(work, null);
        });

        typed(/Folder name/, 'Contracts');
        typed(/Folder name/, '   ');

        expect(screen.getByRole('button', { name: 'Create folder' })).toHaveProperty('disabled', true);
        expect(screen.queryByRole('alert')).toBeNull();
        expect(screen.queryByText('A folder needs a name.')).toBeNull();
    });

    it('says a folder would nest too deep inside a folder already at the ceiling, and refuses to save it', () => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().declare(nested, { id: 'Q3', name: 'Q3' });
        });

        typed(/Folder name/, 'Invoices');

        expect(screen.getByRole('alert').textContent).toBe(
            'A folder nests three levels deep at most, and this one would be deeper.',
        );
        expect(screen.getByRole('button', { name: 'Create folder' })).toHaveProperty('disabled', true);
        expect(asked(answering.requests)).toEqual([]);
    });

    it('says a folder cannot be put inside itself where the report places it beneath its own subfolder', () => {
        const left: ManagedMailFolder = { id: 'LEFT', parentId: 'RIGHT', name: 'Left', role: null, allowedActs: [] };
        const right: ManagedMailFolder = { id: 'RIGHT', parentId: 'LEFT', name: 'Right', role: null, allowedActs: [] };
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().revise({ ...work, folders: [inbox, left, right] }, left);
        });

        expect(screen.getByRole('alert').textContent).toBe('A folder cannot be put inside itself.');
        expect(screen.getByRole('button', { name: 'Save changes' })).toHaveProperty('disabled', true);
    });

    it('suggests what a folder might be called in the empty name field', () => {
        const { held } = maintaining(deployment().transport);

        act(() => {
            held().declare(work, null);
        });

        expect(screen.getByRole('textbox', { name: 'Folder name' })).toHaveProperty(
            'placeholder',
            'e.g. Contracts 2027',
        );
    });

    it.each(['Cancel', 'Close'])('asks for nothing where the dialog was left with %s', (way) => {
        const answering = deployment();
        const { held } = maintaining(answering.transport);

        act(() => {
            held().declare(work, null);
        });

        typed(/Folder name/, 'Contracts');
        fireEvent.click(screen.getByRole('button', { name: way }));

        expect(screen.queryByRole('heading', { name: 'New folder' })).toBeNull();
        expect(asked(answering.requests)).toEqual([]);
        expect(screen.queryByText(/Creating Contracts/)).toBeNull();
    });

    it('says which folder it is changing, and in which mailbox, while the change is on its way', async () => {
        const { held } = maintaining(unanswered);

        act(() => {
            held().revise(work, contracts);
        });

        typed(/Folder name/, 'Contracts 2027');
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        expect(await screen.findByText('Changing Contracts in Work…')).toBeDefined();
    });

    it('says which folder it is deleting, and from which mailbox, while the deletion is on its way', async () => {
        const { held } = maintaining(unanswered);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        expect(await screen.findByText('Deleting Contracts from Work…')).toBeDefined();
    });

    it.each([
        ['AccountMissing', 'This mailbox no longer has that folder. Open the mailboxes again to see what it has.'],
        ['FolderMissing', 'This mailbox no longer has that folder. Open the mailboxes again to see what it has.'],
        ['ParentMissing', 'This mailbox no longer has that folder. Open the mailboxes again to see what it has.'],
        [
            'AccountNotHeld',
            'This mailbox is being restored to your mail server, and its folders cannot be changed until that is done.',
        ],
        ['ProtectedRole', 'This is a folder the mailbox files by, and it stays as it is.'],
        ['NameInvalid', 'That name has a character a folder cannot carry, or it is too long.'],
        ['InboxNameAtTopLevel', 'A folder at the top of the mailbox cannot be called after the inbox.'],
        ['TooManyFolders', 'This mailbox already has as many folders as it takes.'],
        ['ServerRefused', 'Your mail server refused the change.'],
        ['ServerUnavailable', 'Your mail server did not answer. Nothing was changed, so try again.'],
        ['RoleAlreadyPlayed', 'This mailbox already has a folder for that.'],
        [
            'NotDeclaredByTheAccount',
            'Whoever administers this deployment set that folder up, so it is theirs to change.',
        ],
        [
            'NotRecorded',
            'Your mail server made the change and MailFathom could not write it down. Open the mailboxes again before asking for anything else.',
        ],
        ['SomethingThisClientHasNotHeardOf', 'The deployment refused it.'],
    ])('says what the refusal %s means', async (refusal, said) => {
        const { held } = maintaining(deployment(() => refusing(refusal)).transport);

        act(() => {
            held().remove(work, { id: 'CONTRACTS', name: 'Contracts', holdsNested: false });
        });

        fireEvent.click(screen.getByRole('button', { name: 'Delete folder' }));

        expect(await screen.findByText(`The change was refused. ${said}`)).toBeDefined();
    });
});

describe('FolderMaintenanceProvider, marking everything read', () => {
    it('says what it is marking read while the mail is being read', async () => {
        const { held } = maintaining(unanswered);

        act(() => {
            held().markAllRead('work', 'INBOX', 'Inbox');
        });

        expect(await screen.findByText('Marking everything read in Inbox…')).toBeDefined();
    });

    it('says everything is read once every unread message was marked', async () => {
        const { held } = maintaining(mailAnswering(() => timelinePage(mailboxSize - 2)));

        act(() => {
            held().markAllRead('work', 'INBOX', 'Inbox');
        });

        expect(await screen.findByText('All read in Inbox.')).toBeDefined();
    });

    it('says nothing was unread where there was nothing to mark', async () => {
        const { held } = maintaining(mailAnswering(() => emptyFolderPage));

        act(() => {
            held().markAllRead('work', null, 'Work');
        });

        expect(await screen.findByText('Nothing was unread in Work.')).toBeDefined();
    });

    it.each([
        {
            count: 1,
            pages: (cursor: string | null) =>
                cursor === null
                    ? { ...timelinePage(0), emails: timelinePage(0).emails.slice(0, 1) }
                    : { ...emptyFolderPage, nextCursor: 'more' },
            said: (counted: string) =>
                `The first ${counted} unread message in Inbox was marked read. Ask again for the rest.`,
        },
        {
            count: mostPagesMarkedRead * rowsPerPage,
            pages: (cursor: string | null) => timelinePage(Number(cursor ?? '0')),
            said: (counted: string) =>
                `The first ${counted} unread messages in Inbox were marked read. Ask again for the rest.`,
        },
    ])(
        'says how many unread messages it marked where it stopped short, $count of them',
        async ({ count, pages, said }) => {
            const { held } = maintaining(mailAnswering(pages));

            act(() => {
                held().markAllRead('work', 'INBOX', 'Inbox');
            });

            expect(await screen.findByText(said(new Intl.NumberFormat('en').format(count)))).toBeDefined();
        },
    );

    it('says not everything could be marked read where the deployment did not answer', async () => {
        const { held } = maintaining(() => Promise.reject(new Error('no route to host')));

        act(() => {
            held().markAllRead('work', 'INBOX', 'Inbox');
        });

        expect(await screen.findByText('Not everything could be marked read in Inbox: unavailable.')).toBeDefined();
    });

    it('stops when asked to, and says what marking stopped halfway leaves behind', async () => {
        const { held } = maintaining(unanswered);

        act(() => {
            held().markAllRead('work', 'INBOX', 'Inbox');
        });

        fireEvent.click(await screen.findByRole('button', { name: 'Stop the operation' }));
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Stop the operation' }));

        expect(await screen.findByText('Stopped')).toBeDefined();
        expect(await screen.findByText('The messages already marked read stay marked read.')).toBeDefined();
    });
});
