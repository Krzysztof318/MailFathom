// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { act, fireEvent, render, screen, type RenderResult } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientSession, ClientSignal, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadMarkingContext, nothingMarkedRead, type MarkedIn, type ReadMarking } from '../readMarking/useReadMarking';
import {
    SignalledChangesContext,
    nothingSignalled,
    type SignalListener,
    type SignalledChanges,
} from '../signals/signalledChanges';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { FolderTree } from './FolderTree';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// Two mailboxes, one of them nesting a folder its server nests and one of them unreachable, which is what the rows
// below are read against. The counts differ per row deliberately: two mailboxes both have an inbox, so what tells the
// row spanning them apart from either of theirs is what it says it holds.
const tree = {
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
                    path: ['Odebrane'],
                    storedEmailCount: 4213,
                    unreadEmailCount: 12,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                    behind: false,
                },
                {
                    alias: 'ARCHIVE-2024',
                    role: null,
                    path: ['Archiwum', '2024'],
                    storedEmailCount: 980,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: '2026-08-31T09:00:00+00:00',
                    behind: true,
                },
            ],
        },
        {
            account: {
                id: 'personal',
                displayName: 'Personal',
                synchronizationState: 'Unreachable',
                lastSynchronizedAt: '2026-08-30T21:00:00+00:00',
                behind: false,
            },
            folders: [
                {
                    alias: 'INBOX',
                    role: 'Inbox',
                    path: ['INBOX'],
                    storedEmailCount: 50,
                    unreadEmailCount: 3,
                    synchronizationState: 'Unreachable',
                    lastSynchronizedAt: '2026-08-30T21:00:00+00:00',
                    behind: false,
                },
            ],
        },
    ],
};

function answering(body: string, status = 200): MailFathomTransport {
    return () => Promise.resolve({ status, body, headers: {} });
}

// What the tree wrote, read back the way every other screen will read it: out of the workspace rather than out of the
// component that wrote it.
function ScopeProbe() {
    const { workspace } = useWorkspace();

    return <output>{JSON.stringify(workspace)}</output>;
}

const foldTheColumn = 'Fold the column, as the composition around the tree would.';

// Stands in for the control the mail space draws over this column: the tree reads the folded state out of the
// workspace rather than being handed it, so what the test needs is something that writes it there.
function FoldTheColumn() {
    const { workspace, revise } = useWorkspace();

    return (
        <button
            type="button"
            onClick={() => {
                revise({ mailboxesFolded: !workspace.mailboxesFolded });
            }}
        >
            {foldTheColumn}
        </button>
    );
}

function foldTheColumnAway(): void {
    fireEvent.click(screen.getByRole('button', { name: foldTheColumn }));
}

/** The part of a row that carries its name, its state, and its count — drawn beside the symbol, or said and not drawn. */
function saidOn(named: HTMLElement): HTMLElement {
    const said = named.querySelector<HTMLElement>('span:not([aria-hidden])');

    if (said === null) {
        throw new Error(`The row "${named.textContent}" says nothing about what it is.`);
    }

    return said;
}

// An `output` reports itself as a status region, exactly as the tree's own waiting line does, so the probe is picked
// out by being the one carrying a workspace rather than a sentence.
function carried(): Workspace {
    const probe = screen.getAllByRole('status').find((element) => element.textContent.startsWith('{'));

    return JSON.parse(probe?.textContent ?? '') as Workspace;
}

function treeUnder(
    transport: MailFathomTransport,
    online: boolean,
    marking: ReadMarking = nothingMarkedRead,
    changes: SignalledChanges = nothingSignalled,
): ReactElement {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <SignalledChangesContext value={changes}>
                    <ReadMarkingContext value={marking}>
                        <FolderTree session={session} transport={transport} online={online} />
                    </ReadMarkingContext>
                </SignalledChangesContext>
                <FoldTheColumn />
                <ScopeProbe />
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

function renderTree(
    transport: MailFathomTransport,
    online = true,
    marking?: ReadMarking,
    changes?: SignalledChanges,
): RenderResult {
    return render(treeUnder(transport, online, marking, changes));
}

/** A deployment a test speaks for, so what a signal does to the tree is asserted rather than waited for. */
function deploymentSaying(): { changes: SignalledChanges; say: (signal: ClientSignal) => void } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
        },
        say: (signal) => {
            for (const listener of [...listeners]) {
                listener(signal);
            }
        },
    };
}

/** A client that has marked the named messages read, each in the folder the list counted it in. */
function marked(...places: readonly MarkedIn[]): ReadMarking {
    return {
        marked: new Map(places.map((place, at) => [`message-${String(at)}`, place])),
        markRead: () => undefined,
    };
}

function row(name: RegExp): HTMLElement {
    return screen.getByRole('treeitem', { name });
}

async function drawn(): Promise<HTMLElement> {
    return screen.findByRole('tree', { name: 'Mailboxes and folders' });
}

afterEach(() => {
    window.sessionStorage.clear();
});

describe('FolderTree', () => {
    it('says it is reading from the moment the read starts, where the tree will appear', () => {
        renderTree(() => new Promise(() => undefined));

        expect(screen.getByText('Reading mailboxes and folders…')).toBeDefined();
    });

    it('says it is reading again when the network comes back, rather than swapping the tree in silence', async () => {
        let reads = 0;

        // One transport across all three renders, so what starts the second read is the network coming back rather
        // than a changed dependency, and that read never answers — which is the wait the note has to report.
        const transport: MailFathomTransport = () => {
            reads += 1;

            return reads === 1
                ? Promise.resolve({ status: 200, body: JSON.stringify(tree), headers: {} })
                : new Promise<never>(() => undefined);
        };

        const view = renderTree(transport);

        await drawn();
        view.rerender(treeUnder(transport, false));
        view.rerender(treeUnder(transport, true));

        expect(screen.getByText('Reading mailboxes and folders…')).toBeDefined();
        expect(screen.queryByRole('tree')).toBeNull();
    });

    it('draws the mailboxes and their folders as one tree', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        expect(row(/^All mailboxes/)).toBeDefined();
        expect(row(/^Work/)).toBeDefined();
        expect(row(/^Personal/)).toBeDefined();
        expect(row(/^Archiwum/).getAttribute('aria-level')).toBe('2');
        expect(row(/^2024/).getAttribute('aria-level')).toBe('3');
    });

    it('names a folder by the role the deployment gave it rather than by what its server calls the folder', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        expect(row(/^Inbox12 unread/)).toBeDefined();
        expect(screen.queryByRole('treeitem', { name: /Odebrane/ })).toBeNull();
    });

    it('reports what is unread in the words a reader hears, and not what is held, which the design leaves off', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        expect(row(/^Inbox12 unread/).textContent).toContain('12 unread');
        expect(row(/^Inbox12 unread/).textContent).not.toContain('4,213');
    });

    // A count that still named a message the reader has just opened would disagree with the row drawing that message
    // read, which is the one thing about an unread count somebody notices. Every level of the tree is a sum over the
    // same folders, so the correction is applied once to the folders and each of them answers for it.
    it('takes what this client has marked read off the folder, its mailbox, and the role across mailboxes', async () => {
        renderTree(
            answering(JSON.stringify(tree)),
            true,
            marked({ account: 'work', folder: 'INBOX' }, { account: 'work', folder: 'INBOX' }),
        );

        await drawn();

        expect(row(/^Inbox10 unread/).textContent).toContain('10 unread');
        expect(row(/^Work10 unread/).textContent).toContain('10 unread');
        expect(row(/^Inbox13 unread/).textContent).toContain('13 unread');
        expect(row(/^All mailboxes13 unread/).textContent).toContain('13 unread');
    });

    it('leaves a mailbox this client has marked nothing in exactly as the deployment counted it', async () => {
        renderTree(answering(JSON.stringify(tree)), true, marked({ account: 'work', folder: 'INBOX' }));

        await drawn();

        expect(row(/^Personal/).textContent).toContain('3 unread');
    });

    it('offers every mailbox at once as the scope everything else is read under', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        fireEvent.click(row(/^All mailboxes/));

        expect(carried().scope).toEqual({ kind: 'everything' });
        expect(row(/^All mailboxes/).getAttribute('aria-selected')).toBe('true');
    });

    it('offers a special-use folder across every mailbox playing that role, counting all of them', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const acrossMailboxes = row(/^Inbox15 unread/);

        expect(acrossMailboxes.textContent).toContain('15 unread');

        fireEvent.click(acrossMailboxes);

        expect(carried().scope).toEqual({ kind: 'role', role: 'Inbox' });
    });

    it('scopes to one folder of one mailbox, named by what the client surface names it by', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        fireEvent.click(row(/^Inbox12 unread/));

        expect(carried().scope).toEqual({ kind: 'folder', accountId: 'work', alias: 'INBOX' });
    });

    it('says which folder is behind and which one nothing could reach, rather than showing either as waiting', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        expect(row(/^2024/).textContent).toContain('Catching up');
        expect(row(/^InboxThe mail server did not answer/)).toBeDefined();
        expect(screen.queryByRole('progressbar')).toBeNull();
    });

    it('moves through the rows a reader can see from the keyboard', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const first = row(/^All mailboxes/);

        first.focus();
        fireEvent.keyDown(first, { key: 'ArrowDown' });

        expect(document.activeElement).toBe(row(/^Inbox15 unread/));

        fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'ArrowUp' });

        expect(document.activeElement).toBe(first);
    });

    it('folds a mailbox away from the keyboard, and everything under it with it', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const work = row(/^Work/);

        work.focus();
        fireEvent.keyDown(work, { key: 'ArrowLeft' });

        expect(screen.queryByRole('treeitem', { name: /^Archiwum/ })).toBeNull();
        expect(row(/^Work/).getAttribute('aria-expanded')).toBe('false');

        fireEvent.keyDown(row(/^Work/), { key: 'ArrowRight' });

        expect(row(/^Work/).getAttribute('aria-expanded')).toBe('true');
    });

    it('chooses the row the keyboard is on, so a tree is usable without a pointer at all', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const inbox = row(/^Inbox12 unread/);

        inbox.focus();
        fireEvent.keyDown(inbox, { key: 'Enter' });

        expect(carried().scope).toEqual({ kind: 'folder', accountId: 'work', alias: 'INBOX' });
    });

    it('steps into what a row holds where it is already open, rather than opening it twice', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const work = row(/^Work/);

        work.focus();
        fireEvent.keyDown(work, { key: 'ArrowRight' });

        expect(document.activeElement).toBe(row(/^Inbox12 unread/));
    });

    it('leaves a key it has no answer for to the browser', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const first = row(/^All mailboxes/);

        first.focus();
        fireEvent.keyDown(first, { key: 'a' });

        expect(document.activeElement).toBe(first);
    });

    it('moves to the first and the last row a reader can see', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const first = row(/^All mailboxes/);

        first.focus();
        fireEvent.keyDown(first, { key: 'End' });

        expect(document.activeElement).toBe(row(/^InboxThe mail server did not answer/));

        fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'Home' });

        expect(document.activeElement).toBe(first);
    });

    it('moves out to what a folder sits in, where there is nothing under it to shut', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const nested = row(/^2024/);

        nested.focus();
        fireEvent.keyDown(nested, { key: 'ArrowLeft' });

        expect(document.activeElement).toBe(row(/^Archiwum/));
    });

    it('opens and shuts a row with the pointer as well', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        // The control that opens a row is hidden from the accessibility tree deliberately — a tree says whether a row
        // is open and opens one from the keyboard — so this is the one thing here that cannot be found by its role.
        fireEvent.click(row(/^Work/).lastElementChild as HTMLElement);

        expect(row(/^Work/).getAttribute('aria-expanded')).toBe('false');
        expect(carried().scope).toEqual({ kind: 'everything' });
    });

    it('leaves the tab stop on the row the pointer opened, so tabbing out leaves the tree from it', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        fireEvent.click(row(/^Work/).lastElementChild as HTMLElement);

        expect(row(/^Work/).getAttribute('tabindex')).toBe('0');
    });

    it('keeps what has been folded away in the workspace, so it survives moving between the spaces', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const work = row(/^Work/);

        work.focus();
        fireEvent.keyDown(work, { key: 'ArrowLeft' });

        expect(carried().collapsed).toEqual(['account:work']);
    });

    it('says an owner with no mailbox has none, and what would give them one', async () => {
        renderTree(answering(JSON.stringify({ synchronizationEnabled: false, accounts: [] })));

        expect(await screen.findByText(/No mail account is configured for this owner yet\./)).toBeDefined();
        expect(screen.getByText(/none is declared yet\./)).toBeDefined();
    });

    it('says a deployment that did not answer did not answer, and offers the one way out of it', async () => {
        const transport = vi.fn(answering('', 503));

        renderTree(transport);

        expect(await screen.findByText('The mailboxes and folders could not be read: unavailable.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(transport).toHaveBeenCalledTimes(2);
    });

    it('offers no second attempt at a failure a second attempt would repeat', async () => {
        renderTree(answering('', 403));

        expect(await screen.findByText('The mailboxes and folders could not be read: unauthorized.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('reads nothing without a network, and says so rather than waiting in silence', () => {
        const transport = vi.fn(answering(JSON.stringify(tree)));

        renderTree(transport, false);

        expect(screen.getByText(/This machine is offline\./)).toBeDefined();
        expect(transport).not.toHaveBeenCalled();
    });
});

describe('FolderTree, in a folded column', () => {
    it('draws a folder as its symbol alone, and says the name it no longer draws', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        foldTheColumnAway();

        const inbox = row(/^Inbox12 unread/);

        expect(saidOn(inbox).className).toContain('sr-only');
        expect(inbox.querySelector('svg')).not.toBeNull();
        expect(inbox.textContent).toContain('12 unread');
    });

    it('draws a folder symbol larger than the column draws one, a narrower target having to be hit as reliably', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        expect(
            row(/^Inbox12 unread/)
                .querySelector('svg')
                ?.getAttribute('class'),
        ).toContain('size-4.5');

        foldTheColumnAway();

        expect(
            row(/^Inbox12 unread/)
                .querySelector('svg')
                ?.getAttribute('class'),
        ).toContain('size-6');
    });

    it('marks a mailbox by its colour where the column named it, so the mailboxes stay tellable apart', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        foldTheColumnAway();

        const work = row(/^Work/);

        expect(saidOn(work).className).toContain('sr-only');
        expect(work.querySelector('[aria-hidden="true"].rounded-full')).not.toBeNull();
        expect(work.textContent).toContain('Work');
    });

    it('offers no per-row control in the rail, the arrow keys being what folds a row there', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();

        const drawnControls = row(/^Work/).querySelectorAll('svg').length;

        foldTheColumnAway();

        expect(row(/^Work/).querySelectorAll('svg').length).toBe(drawnControls - 1);
    });

    it('leaves what a reader folded away folded, folding the column being the other question', async () => {
        renderTree(answering(JSON.stringify(tree)));

        await drawn();
        const work = row(/^Work/);

        work.focus();
        fireEvent.keyDown(work, { key: 'ArrowLeft' });

        expect(screen.queryByRole('treeitem', { name: /^Archiwum/ })).toBeNull();

        foldTheColumnAway();

        expect(carried().collapsed).toEqual(['account:work']);
        expect(screen.queryByRole('treeitem', { name: /^Archiwum/ })).toBeNull();
        expect(row(/^Work/).getAttribute('aria-expanded')).toBe('false');

        foldTheColumnAway();

        expect(carried().collapsed).toEqual(['account:work']);
        expect(screen.queryByRole('treeitem', { name: /^Archiwum/ })).toBeNull();
    });
    it('reads the tree again when the deployment says a count moved, without taking the tree off the screen', async () => {
        const deployment = deploymentSaying();
        let reads = 0;

        renderTree(
            () => {
                reads += 1;

                return Promise.resolve({ status: 200, headers: {}, body: JSON.stringify(tree) });
            },
            true,
            undefined,
            deployment.changes,
        );

        await drawn();
        expect(reads).toBe(1);

        act(() => {
            deployment.say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 2 });
        });

        // The tree is still the tree while the read it asked for is in flight: a reader whose pointer is on a row
        // keeps the row, which is the whole reason this is not the counter that says a read is in flight.
        expect(screen.queryByText('Reading mailboxes and folders…')).toBeNull();
        expect(row(/^Work/)).toBeDefined();

        await drawn();
        expect(reads).toBe(2);
    });

    it.each<{ signal: ClientSignal; named: string }>([
        { signal: { kind: 'folders.changed', account: 'work' }, named: 'the mapping moving' },
        { signal: { kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['m-1'] }, named: 'mail changing' },
    ])('reads the tree again on $named', async ({ signal }) => {
        const deployment = deploymentSaying();
        let reads = 0;

        renderTree(
            () => {
                reads += 1;

                return Promise.resolve({ status: 200, headers: {}, body: JSON.stringify(tree) });
            },
            true,
            undefined,
            deployment.changes,
        );

        await drawn();

        act(() => {
            deployment.say(signal);
        });

        await drawn();
        expect(reads).toBe(2);
    });

    it('reads nothing again for a signal about something the tree does not draw', async () => {
        const deployment = deploymentSaying();
        let reads = 0;

        renderTree(
            () => {
                reads += 1;

                return Promise.resolve({ status: 200, headers: {}, body: JSON.stringify(tree) });
            },
            true,
            undefined,
            deployment.changes,
        );

        await drawn();

        act(() => {
            deployment.say({ kind: 'account.state', account: 'work' });
        });

        await drawn();
        expect(reads).toBe(1);
    });
});
