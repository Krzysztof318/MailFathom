// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { act, fireEvent, render, screen, waitFor, within, type RenderResult } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailAccount, MailFathomTransport } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { swipeDistance } from '../controls/swipeAcross';
import { MailboxActsContext, nothingActed, type AskedAct, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { LocalizationProvider } from '../localization/Localization';
import {
    SignalledChangesContext,
    nothingSignalled,
    type SignalListener,
    type SignalledChange,
    type SignalledChanges,
} from '../signals/signalledChanges';
import { everything, type MailScope } from '../workspace/mailScope';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { estimatedRowHeight } from '../messageRows/rowWindow';
import { openingListing, rowsPerPage } from './listing';
import { MessageList } from './MessageList';
import { rememberedListing, rememberListing } from './rememberedListings';
import { ListedMailContext, nothingListed, type ListedMailbox } from './useListedMail';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const work: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

function message(at: number, carried: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        id: `message-${String(at)}`,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: `Message ${String(at)}`,
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: `writer-${String(at)}@nordwind.example`,
        senderDisplayName: `Writer ${String(at)}`,
        toAddresses: ['user@example.invalid'],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 1_024,
        preview: `The opening of message ${String(at)}.`,
        ...carried,
    };
}

// What a derivation concluded about a message, as the deployment publishes it: two readings, so the row proves it
// draws the most actionable one rather than the first one answered.
const derived = {
    derivedAt: '2026-08-31T09:41:00+00:00',
    marks: [
        {
            aspect: 'Sense',
            text: 'An invoice for August.',
            reason: 'The message attaches a document the sender calls an invoice.',
            dueAt: null,
            source: 'DeterministicRule',
            origin: 'rules/invoice',
            evidence: [],
        },
        {
            aspect: 'Commitment',
            text: 'An answer is owed by Friday.',
            reason: 'The sender asks for confirmation before the end of the week.',
            dueAt: '2026-09-04T16:00:00+00:00',
            source: 'Model',
            origin: 'agents/reader',
            evidence: ['fragment-1'],
        },
    ],
};

function pageOf(rows: readonly unknown[], cursors: Record<string, unknown> = {}): string {
    return JSON.stringify({
        emails: rows,
        nextCursor: null,
        previousCursor: null,
        pageSize: rowsPerPage,
        ...cursors,
    });
}

const wholeFolder = pageOf(Array.from({ length: rowsPerPage }, (_, at) => message(at)));

function answering(body: string, status = 200): MailFathomTransport {
    return () => Promise.resolve({ status, body, headers: {} });
}

function recording(body: string): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

// What the list wrote, read back the way the rest of the client will read it: out of the workspace rather than out of
// the component that wrote it.
function SelectionProbe() {
    const { workspace } = useWorkspace();

    return <output>{JSON.stringify(workspace)}</output>;
}

function carried(): Workspace {
    const probe = screen.getAllByRole('status').find((element) => element.textContent.startsWith('{'));

    return JSON.parse(probe?.textContent ?? '') as Workspace;
}

// Opening is the frame's decision rather than the list's, so the list asks and this stands in for what the frame does
// with the ask: it writes the workspace, which is the pane composition and what every assertion below reads back.
function ListOpeningIntoTheWorkspace(drawn: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly scope: MailScope;
    readonly accounts: readonly MailAccount[];
    readonly online: boolean;
}) {
    const { revise } = useWorkspace();

    return (
        <MessageList
            {...drawn}
            onOpen={(storedEmailId) => {
                revise({ selection: storedEmailId });
            }}
        />
    );
}

// Writing a message is offered from the row's own menu, so the list is drawn under something that answers for it. It
// records rather than composes: what a menu item does is ask, and what the composer does with the ask is its own test.
const composing: Composing = { offered: true, drafts: false, opening: null, compose: vi.fn(), close: vi.fn() };

function listUnder(
    transport: MailFathomTransport,
    {
        scope = everything,
        accounts = [work],
        online = true,
        changes = nothingSignalled,
        acts = nothingActed,
    }: Partial<Drawn> = {},
): ReactElement {
    return (
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <MailboxActsContext value={acts}>
                    <WorkspaceProvider>
                        <SignalledChangesContext value={changes}>
                            <ListOpeningIntoTheWorkspace
                                session={session}
                                transport={transport}
                                scope={scope}
                                accounts={accounts}
                                online={online}
                            />
                        </SignalledChangesContext>

                        <SelectionProbe />
                    </WorkspaceProvider>
                </MailboxActsContext>
            </ComposingContext>
        </LocalizationProvider>
    );
}

interface Drawn {
    readonly scope: MailScope;
    readonly accounts: readonly MailAccount[];
    readonly online: boolean;
    readonly changes: SignalledChanges;
    readonly acts: MailboxActs;
}

/** A deployment a test speaks for, so what a signal does to the list is asserted rather than waited for. */
function deploymentSaying(): { changes: SignalledChanges; say: (signal: SignalledChange) => void } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            refresh: () => undefined,
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

function renderList(transport: MailFathomTransport, drawn: Partial<Drawn> = {}): RenderResult {
    return render(listUnder(transport, drawn));
}

// The menu is the way into a selection now that no control stands over the column, so a test that picks a message out
// goes through it exactly as a reader does.
function chooseFromMenu(pressed: HTMLElement, item: string): void {
    fireEvent.contextMenu(pressed);
    fireEvent.click(screen.getByRole('menuitem', { name: item }));
}

// Inside the list rather than in the document, because the list's own rows are the only options on the screen and a
// query across the document would have to be told so.
// Every narrowing is folded away behind one control, so a test reaching for one opens the disclosure first.
function openFilters(): void {
    fireEvent.click(screen.getByText('Filters'));
}

async function rows(): Promise<HTMLElement[]> {
    const list = await screen.findByRole('listbox', { name: 'Messages' });

    return within(list).getAllByRole('option');
}

// The row for message two while it is going, which is the one row `row` below cannot reach: a row on its way out says
// so in its own line, so its name no longer ends with what the message is about.
function going2(): HTMLElement | null {
    const list = screen.getByRole('listbox', { name: 'Messages' });

    return within(list).queryByRole('option', { name: /Message 2 Archiving/ });
}

// By what it is about, which is the last part of a row's name: the name runs its parts together, so the subject is
// matched to the end of it — which is what keeps the row for message one from also being the row for message ten.
function row(at: number): HTMLElement {
    const list = screen.getByRole('listbox', { name: 'Messages' });

    return within(list).getByRole('option', { name: new RegExp(`Message ${String(at)}$`) });
}

// The end of a row's own animation, dispatched under the name React is actually listening for: it binds the
// vendor-prefixed name wherever the environment declares no `AnimationEvent` constructor, which jsdom does not.
function animationEnded(element: Element): void {
    const named = 'AnimationEvent' in window ? 'animationend' : 'webkitAnimationEnd';

    element.dispatchEvent(new Event(named, { bubbles: true }));
}

afterEach(() => {
    window.sessionStorage.clear();
});

describe('MessageList', () => {
    // An act that files a message elsewhere is the reader's, so the row goes at the press rather than when the
    // deployment next agrees. It goes out under the animation that says which act took it, and it is drawn until that
    // animation has run — then it is gone, and it comes back where it stood the moment the act is let go of, which is
    // all a refusal leaves behind.
    it('holds a row asked to be filed elsewhere until it has gone, then draws it again once the act is let go of', async () => {
        const transport = answering(wholeFolder);
        const leaving: MailboxActs = {
            ...nothingActed,
            asked: new Map([['message-2', { act: 'archive', from: 'INBOX', leaves: true, destroys: false }]]),
        };
        const drawn = renderList(transport, { acts: leaving });

        await rows();

        const going = going2();

        expect(going).not.toBeNull();
        expect([...(going?.classList ?? [])]).toContain('animate-row-going');

        act(() => {
            animationEnded(going as Element);
        });

        await waitFor(() => {
            expect(going2()).toBeNull();
        });

        drawn.rerender(listUnder(transport, { acts: nothingActed }));

        expect(row(2)).toBeDefined();
    });

    // The other half of the same rule. An act performed on the selection reaches every row the list holds, and most of
    // them are past the window's edges, where nothing is mounted and nothing reports an animation ending. Held for an
    // animation they cannot play, those rows would stand in the length of the list until a scroll swept the lot — and
    // that sweep is a page of rows taken out from under the reader's cursor mid-gesture.
    it('takes a whole selection out at the press rather than holding rows no reader could watch go', async () => {
        const filed = Array.from({ length: 30 }, (_, at): [string, AskedAct] => [
            `message-${String(at)}`,
            { act: 'archive', from: 'INBOX', leaves: true, destroys: false },
        ]);

        renderList(answering(wholeFolder), { acts: { ...nothingActed, asked: new Map(filed) } });

        const drawn = await rows();

        expect(drawn.some((option) => option.textContent.includes('Archiving'))).toBe(false);
        expect(row(30)).toBeDefined();
        expect(screen.queryByRole('option', { name: /Message 0$/ })).toBeNull();
    });

    it('says it is reading from the moment the read starts, where the mail will appear', () => {
        renderList(() => new Promise(() => undefined));

        expect(screen.getByText('Reading your mail…')).toBeDefined();
    });

    // What the skeleton standing in that space looks like is held against the design as images rather than asserted
    // here: it is `aria-hidden` by construction, and jsdom computes no layout, so what this suite can say about the
    // wait is what a person meets — the sentence while it lasts, the mail once it lands, and neither of them twice.
    it('says it is reading until the mail is there, and says nothing of the sort once it is', async () => {
        renderList(answering(wholeFolder));

        expect(screen.getByText('Reading your mail…')).toBeDefined();
        expect(screen.queryByRole('listbox', { name: 'Messages' })).toBeNull();

        expect(await rows()).not.toHaveLength(0);
        expect(screen.queryByText('Reading your mail…')).toBeNull();
    });

    it('draws a row carrying who wrote, what about, and when, and not the opening the design leaves off', async () => {
        renderList(answering(wholeFolder));

        const drawn = await rows();

        expect(drawn[0]?.textContent).toContain('Writer 0');
        expect(drawn[0]?.textContent).toContain('Message 0');
        expect(drawn[0]?.textContent).not.toContain('The opening of message 0.');
        expect(drawn[0]?.querySelector('time')?.getAttribute('datetime')).toBe('2026-08-31T09:41:00+00:00');
    });

    it('says what the mail server last reported about a message, in words rather than in a mark alone', async () => {
        const marked = pageOf([
            message(0, { unread: true, flagged: true, answered: true, hasAttachments: true, attachmentCount: 3 }),
        ]);

        renderList(answering(marked));

        const drawn = await rows();

        expect(drawn[0]?.textContent).toContain('Unread');
        expect(drawn[0]?.textContent).toContain('Flagged');
        expect(drawn[0]?.textContent).toContain('Answered');
        expect(drawn[0]?.textContent).toContain('3 attached');
    });

    it('holds far fewer rows in the document than the page it read', async () => {
        renderList(answering(wholeFolder));

        expect((await rows()).length).toBeLessThan(rowsPerPage / 2);
    });

    it('asks for the folder the scope names rather than for every folder', async () => {
        const { transport, requests } = recording(wholeFolder);

        renderList(transport, { scope: { kind: 'folder', accountId: 'work', alias: 'ARCHIVE-2024' } });
        await rows();

        expect(requests[0]?.path).toContain('account=work');
        expect(requests[0]?.path).toContain('folder=ARCHIVE-2024');
    });

    it('continues from the cursor the folder was left at rather than from its leading end', async () => {
        rememberListing(session.baseAddress, everything, {
            order: 'newestFirst',
            filters: openingListing.filters,
            readingsShown: true,
            cursor: 'where-they-were',
            readAs: 'forward',
            rowInPage: 3,
        });

        const { transport, requests } = recording(wholeFolder);

        renderList(transport);
        await rows();

        expect(requests[0]?.path).toContain('cursor=where-they-were');
    });

    it('reads nothing above the page it continued into, which nobody scrolled to', async () => {
        rememberListing(session.baseAddress, everything, {
            order: 'newestFirst',
            filters: openingListing.filters,
            readingsShown: true,
            cursor: 'where-they-were',
            readAs: 'forward',
            rowInPage: rowsPerPage - 1,
        });

        const { transport, requests } = recording(
            pageOf(
                Array.from({ length: rowsPerPage }, (_, at) => message(at)),
                { previousCursor: 'the-page-above' },
            ),
        );

        renderList(transport);
        await rows();

        expect(requests.map((asked) => asked.path).filter((path) => path.includes('direction=backward'))).toStrictEqual(
            [],
        );
    });

    it('keeps reading past a page that answered with no rows and a cursor onward', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve({
                status: 200,
                headers: {},
                body:
                    requests.length === 1
                        ? pageOf([], { nextCursor: 'the-page-after' })
                        : pageOf([message(0)], { previousCursor: 'the-page-above' }),
            });
        };

        renderList(transport);
        await rows();

        expect(requests[1]?.path).toContain('cursor=the-page-after');
    });

    it('says what failed and offers the way out of the one failure a second attempt answers', async () => {
        renderList(answering('', 503));

        expect(await screen.findByText('This folder could not be read: unavailable.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('offers no second attempt for a failure that repeats identically', async () => {
        renderList(answering('', 403));

        expect(await screen.findByText('This folder could not be read: unauthorized.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('says an empty folder is empty', async () => {
        renderList(answering(pageOf([])));

        expect(await screen.findByText('There is no mail in this folder.')).toBeDefined();
    });

    // A folder every row of which has gone is empty from where the reader stands, and the act landing changes nothing
    // the list holds — the read it would take to see it is not asked again — so it says so then rather than standing
    // in a state no act ends. It says it once every row has *gone* rather than at the press: the rows are still on the
    // screen while they go, and a folder that called itself empty over two rows going out would be answering the act
    // before the act had been drawn.
    it('says a folder is empty once every message drawn in it has gone', async () => {
        renderList(answering(pageOf([message(0), message(1)])), {
            acts: {
                ...nothingActed,
                asked: new Map([
                    ['message-0', { act: 'archive', from: 'INBOX', leaves: true, destroys: false }],
                    ['message-1', { act: 'archive', from: 'INBOX', leaves: true, destroys: false }],
                ]),
            },
        });

        const going = await rows();

        expect(going).toHaveLength(2);
        expect(screen.queryByText('There is no mail in this folder.')).toBeNull();

        act(() => {
            for (const element of going) {
                animationEnded(element);
            }
        });

        expect(await screen.findByText('There is no mail in this folder.')).toBeDefined();
        expect(screen.queryByRole('listbox', { name: 'Messages' })).toBeNull();
    });

    it('tells a folder nothing has been taken into yet apart from an empty one', async () => {
        renderList(answering(pageOf([])), { accounts: [{ ...work, synchronizationState: 'NeverSynchronized' }] });

        expect(
            await screen.findByText(
                'Nothing has been taken into this deployment from this mailbox yet, so there is nothing to show. The folder is not empty — it has not been read.',
            ),
        ).toBeDefined();
    });

    it('says a mailbox that stopped synchronizing may be holding less than the mail server does', async () => {
        renderList(answering(pageOf([])), { accounts: [{ ...work, synchronizationState: 'Unreachable' }] });

        expect(
            await screen.findByText(
                'This mailbox stopped synchronizing, so what is here may be less than the mail server holds.',
            ),
        ).toBeDefined();
    });

    it('says nothing matched where the reader narrowed the list themselves', async () => {
        renderList(answering(pageOf([])));

        await screen.findByText('There is no mail in this folder.');
        openFilters();
        fireEvent.click(screen.getByLabelText('Only unread'));

        expect(
            await screen.findByText('No message in this folder matches what the list is narrowed to.'),
        ).toBeDefined();
    });

    it('says a folder narrowed by a reading may simply not have been read, rather than saying nothing matched', async () => {
        renderList(answering(pageOf([])));

        await screen.findByText('There is no mail in this folder.');
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: 'Why this may matter' }));

        expect(
            await screen.findByText(
                'No message in this folder carries the reading the list is narrowed to. Either none of them does, or MailFathom has not read this folder yet.',
            ),
        ).toBeDefined();
    });

    it('says the machine is offline rather than reporting a deployment that did not answer', () => {
        renderList(answering(wholeFolder), { online: false });

        expect(
            screen.getByText('This machine is offline. The client reconnects on its own when the network comes back.'),
        ).toBeDefined();
    });

    it('reads the folder again under a filter the reader turned on, from its leading end', async () => {
        const { transport, requests } = recording(wholeFolder);

        renderList(transport);
        await rows();
        openFilters();
        fireEvent.click(screen.getByLabelText('Only unread'));
        await rows();

        expect(requests.at(-1)?.path).toContain('unread=true');
        expect(requests.at(-1)?.path).not.toContain('cursor=');
    });

    it('reads the folder again in the order the reader chose', async () => {
        const { transport, requests } = recording(wholeFolder);

        renderList(transport);
        await rows();
        openFilters();
        fireEvent.click(screen.getByRole('radio', { name: 'Oldest first' }));
        await rows();

        expect(requests.at(-1)?.path).toContain('order=oldestFirst');
    });

    it('keeps the order and the filters with the folder, so returning to it reads it the same way', async () => {
        renderList(answering(wholeFolder));

        await rows();
        openFilters();
        fireEvent.click(screen.getByRole('radio', { name: 'Oldest first' }));
        await rows();

        expect(rememberedListing(session.baseAddress, everything).order).toBe('oldestFirst');
    });

    it('offers no junk control for a folder the reader has already pointed at', async () => {
        renderList(answering(wholeFolder), { scope: { kind: 'folder', accountId: 'work', alias: 'Spam' } });

        await rows();

        expect(screen.queryByLabelText('Include junk')).toBeNull();
    });

    it('opens a message when it is pointed at, and picks nothing out, so the toolbar stays over the list', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(2));

        expect(carried().selected).toStrictEqual([]);
        expect(carried().selection).toBe('message-2');
    });

    // A drafts folder holds what somebody was writing rather than what arrived, so the reading pane is the wrong
    // surface for one: what a reader asked for is the words back under their own cursor.
    it('opens a draft in the composer rather than in the reading pane, and leaves the pane where it was', async () => {
        renderList(answering(wholeFolder), { acts: { ...nothingActed, folderRoleOf: () => 'Drafts' } });

        await rows();
        fireEvent.pointerDown(row(2));

        expect(composing.compose).toHaveBeenCalledWith({ kind: 'draft', storedEmailId: 'message-2' });
        expect(carried().selection).toBeNull();
    });

    it('picks a message out rather than opening it while others are already picked out', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1), { ctrlKey: true });
        fireEvent.pointerDown(row(3));

        expect(carried().selected).toStrictEqual(['message-1', 'message-3']);
        expect(carried().selection).toBeNull();
    });

    it('adds a message to the selection when the pointer holds the modifier key', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1), { ctrlKey: true });
        fireEvent.pointerDown(row(3), { ctrlKey: true });

        expect(carried().selected).toStrictEqual(['message-1', 'message-3']);
    });

    it('takes a message out of the selection when the modifier key picks it a second time', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1), { ctrlKey: true });
        fireEvent.pointerDown(row(3), { ctrlKey: true });
        fireEvent.pointerDown(row(3), { ctrlKey: true });

        expect(carried().selected).toStrictEqual(['message-1']);
    });

    it('selects the run between the anchor and the message shift reached', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1));
        fireEvent.pointerDown(row(4), { shiftKey: true });

        expect(carried().selected).toStrictEqual(['message-1', 'message-2', 'message-3', 'message-4']);
    });

    it('selects the run a pointer dragged over', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1));
        fireEvent.pointerEnter(row(3));

        expect(carried().selected).toStrictEqual(['message-1', 'message-2', 'message-3']);
    });

    it('stops selecting once the pointer is let go, wherever that happened', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.pointerDown(row(1));
        fireEvent.pointerUp(window);
        fireEvent.pointerEnter(row(3));

        expect(carried().selected).toStrictEqual([]);
    });

    it('answers a right-click on a row with its menu, headed by what the row is about', async () => {
        renderList(answering(wholeFolder));

        await rows();
        fireEvent.contextMenu(row(1));

        expect(screen.getByRole('menu', { name: 'Message 1' })).toBeTruthy();
    });

    it('picks messages out one at a time from their own menus, which is how a finger reaches a selection', async () => {
        renderList(answering(wholeFolder));

        await rows();
        chooseFromMenu(row(1), 'Select messages');
        chooseFromMenu(row(3), 'Select messages');

        expect(carried().selected).toStrictEqual(['message-1', 'message-3']);
    });

    it('leaves what is open alone while messages are being picked out for a question', async () => {
        renderList(answering(wholeFolder));

        await rows();
        chooseFromMenu(row(1), 'Select messages');

        expect(carried().selection).toBeNull();
    });

    it('opens that menu from the keyboard, on the row focus is on', async () => {
        renderList(answering(wholeFolder));

        const listed = await screen.findByRole('listbox', { name: 'Messages' });

        fireEvent.keyDown(listed, { key: 'ArrowDown' });
        fireEvent.keyDown(listed, { key: 'ContextMenu' });

        expect(screen.getByRole('menu', { name: 'Message 1' })).toBeTruthy();
    });

    it('takes the menu off the screen once an item has been chosen', async () => {
        renderList(answering(wholeFolder));

        await rows();
        chooseFromMenu(row(1), 'Select messages');

        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('says where the mail it drew belongs, which is what an act on a selection needs and the workspace never keeps', async () => {
        const listed = { ...nothingListed, drew: vi.fn() };

        render(<ListedMailContext value={listed}>{listUnder(answering(wholeFolder))}</ListedMailContext>);

        await rows();

        expect(listed.drew).toHaveBeenCalledWith(
            expect.arrayContaining([expect.objectContaining({ id: 'message-1', account: 'work', folder: 'INBOX' })]),
        );
    });

    it('selects everything it is showing when a surface outside it asks, and stops offering that as it leaves', async () => {
        const asked: (ListedMailbox | null)[] = [];
        const listed = {
            ...nothingListed,
            listing: (list: ListedMailbox | null) => {
                asked.push(list);
            },
        };

        const drawn = render(<ListedMailContext value={listed}>{listUnder(answering(wholeFolder))}</ListedMailContext>);

        await rows();
        act(() => {
            asked.at(-1)?.selectAll();
        });

        expect(carried().selected).toHaveLength(rowsPerPage);

        drawn.unmount();

        expect(asked.at(-1)).toBeNull();
    });

    // The tree draws the standing views and the list is what holds a folder's filters, so pressing one there is an ask
    // rather than a second way of reading mail: what arrives here is the same narrowing the panel writes.
    it('reads the folder again narrowed to what a standing view stands on when the tree asks for one', async () => {
        const asked: (ListedMailbox | null)[] = [];
        const listed = {
            ...nothingListed,
            listing: (list: ListedMailbox | null) => {
                asked.push(list);
            },
        };
        const { transport, requests } = recording(wholeFolder);

        render(<ListedMailContext value={listed}>{listUnder(transport)}</ListedMailContext>);

        await rows();
        act(() => {
            asked.at(-1)?.stand('needsDecision');
        });

        await waitFor(() => {
            expect(requests.at(-1)?.path).toContain('carriesMark=Significance');
        });
    });

    // The bar above the list hands focus back before it clears the selection and disappears, and the row the keyboard
    // was left on is where a reader was before they picked anything out.
    it('puts focus back on the row it left the keyboard on when a surface outside it hands focus over', async () => {
        const asked: (ListedMailbox | null)[] = [];
        const listed = {
            ...nothingListed,
            listing: (list: ListedMailbox | null) => {
                asked.push(list);
            },
        };

        render(<ListedMailContext value={listed}>{listUnder(answering(wholeFolder))}</ListedMailContext>);

        const drawn = await rows();

        act(() => {
            asked.at(-1)?.takeFocus();
        });

        expect(document.activeElement).toBe(drawn[0]);
    });

    it('moves through the list from the keyboard without picking out what it moves onto', async () => {
        renderList(answering(wholeFolder));

        const drawn = await rows();

        fireEvent.keyDown(screen.getByRole('listbox', { name: 'Messages' }), { key: 'ArrowDown' });

        expect(carried().selected).toStrictEqual([]);
        expect(carried().selection).toBeNull();
        expect(drawn[1]?.getAttribute('tabindex')).toBe('0');
    });

    it('opens the message the keyboard is on', async () => {
        renderList(answering(wholeFolder));

        await rows();
        const list = screen.getByRole('listbox', { name: 'Messages' });

        fireEvent.keyDown(list, { key: 'ArrowDown' });
        fireEvent.keyDown(list, { key: 'Enter' });

        expect(carried().selection).toBe('message-1');
    });

    it('picks a message out from the keyboard without opening it', async () => {
        renderList(answering(wholeFolder));

        await rows();
        const list = screen.getByRole('listbox', { name: 'Messages' });

        fireEvent.keyDown(list, { key: 'ArrowDown', ctrlKey: true });
        fireEvent.keyDown(list, { key: ' ' });

        expect(carried().selected).toStrictEqual(['message-1']);
        expect(carried().selection).toBeNull();
    });

    it('extends the selection when the keyboard moves with shift held', async () => {
        renderList(answering(wholeFolder));

        await rows();
        const list = screen.getByRole('listbox', { name: 'Messages' });

        fireEvent.keyDown(list, { key: 'ArrowDown' });
        fireEvent.keyDown(list, { key: 'ArrowDown', shiftKey: true });

        expect(carried().selected).toStrictEqual(['message-1', 'message-2']);
    });

    it('reports the list as one whose length no page answers, rather than as one the length of what is held', async () => {
        renderList(answering(wholeFolder));

        expect((await rows())[0]?.getAttribute('aria-setsize')).toBe('-1');
    });

    it.each([
        ['Only flagged', 'flagged=true'],
        ['Only with attachments', 'hasAttachments=true'],
        ['Include junk', 'includeJunk=true'],
    ])('reads the folder again under %s', async (control, asked) => {
        const { transport, requests } = recording(wholeFolder);

        renderList(transport);
        await rows();
        openFilters();
        fireEvent.click(screen.getByLabelText(control));
        await rows();

        expect(requests.at(-1)?.path).toContain(asked);
    });

    it('draws a message carrying no subject as one with no subject rather than as a blank line', async () => {
        renderList(answering(pageOf([message(0, { subject: null })])));

        expect((await rows())[0]?.textContent).toContain('No subject');
    });

    it('draws a message whose sender could not be read as one with no sender', async () => {
        renderList(answering(pageOf([message(0, { senderDisplayName: null, senderAddress: null, toAddresses: [] })])));

        expect((await rows())[0]?.textContent).toContain('No sender');
    });

    it('falls back to who a message was written to where it carries no sender at all', async () => {
        renderList(answering(pageOf([message(0, { senderDisplayName: null, senderAddress: null })])));

        expect((await rows())[0]?.textContent).toContain('user@example.invalid');
    });

    it('draws no time for a message no header carried a usable date on', async () => {
        renderList(answering(pageOf([message(0, { receivedAt: null })])));

        expect((await rows())[0]?.querySelector('time')).toBeNull();
    });

    it('draws no time where the date is one no clock produced, rather than the words for an invalid one', async () => {
        renderList(answering(pageOf([message(0, { receivedAt: 'the day before yesterday' })])));

        expect((await rows())[0]?.querySelector('time')).toBeNull();
    });

    it('says the whole of a folder has been read once both its cursors are spent', async () => {
        renderList(answering(wholeFolder));

        await rows();

        expect(screen.getByText('That is the whole of this folder.')).toBeDefined();
    });
    it('reads the leading page again when the deployment says mail arrived in what it is showing', async () => {
        const deployment = deploymentSaying();
        const asked = recording(wholeFolder);

        renderList(asked.transport, { changes: deployment.changes });
        await rows();

        const before = asked.requests.length;

        act(() => {
            deployment.say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 2 });
        });

        await rows();
        expect(asked.requests.length).toBe(before + 1);
    });

    it('keeps the rows a reader is looking at while the read it asked for is in flight', async () => {
        const deployment = deploymentSaying();
        let reads = 0;

        // The first read answers and the one the signal asks for never does, which is the frame the reader would see
        // a list emptied in: what they were looking at stays on the screen until something replaces it.
        renderList(
            () => {
                reads += 1;

                return reads === 1
                    ? Promise.resolve({ status: 200, body: wholeFolder, headers: {} })
                    : new Promise(() => undefined);
            },
            { changes: deployment.changes },
        );

        const drawn = (await rows()).length;

        act(() => {
            deployment.say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 2 });
        });

        expect((await rows()).length).toBe(drawn);
        expect(screen.queryByText('Reading your mail…')).toBeNull();
    });

    it('reads the page on the screen again on a refresh, drawing its rows the whole time', async () => {
        const deployment = deploymentSaying();
        let reads = 0;

        renderList(
            () => {
                reads += 1;

                return reads === 1
                    ? Promise.resolve({ status: 200, body: wholeFolder, headers: {} })
                    : new Promise(() => undefined);
            },
            { changes: deployment.changes },
        );

        const drawn = (await rows()).length;

        act(() => {
            deployment.say({ kind: 'refresh' });
        });

        await waitFor(() => {
            expect(reads).toBe(2);
        });

        // The rows themselves rather than their number: a page dropped and read again would still count as many rows,
        // drawn as the skeleton of a page arriving.
        expect((await rows()).length).toBe(drawn);
        expect(row(0)).toBeDefined();
    });

    it('keeps its rows and says nothing when a refresh is not answered', async () => {
        const deployment = deploymentSaying();
        let reads = 0;

        renderList(
            () => {
                reads += 1;

                return Promise.resolve(
                    reads === 1
                        ? { status: 200, body: wholeFolder, headers: {} }
                        : { status: 503, body: '', headers: {} },
                );
            },
            { changes: deployment.changes },
        );

        await rows();

        act(() => {
            deployment.say({ kind: 'refresh' });
        });

        await waitFor(() => {
            expect(reads).toBe(2);
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(row(0)).toBeDefined();
        expect(screen.queryByRole('alert')).toBeNull();
        expect(reads).toBe(2);
    });

    it('reads again for mail the deployment named as changed', async () => {
        const deployment = deploymentSaying();
        const asked = recording(wholeFolder);

        renderList(asked.transport, { changes: deployment.changes });
        await rows();

        const before = asked.requests.length;

        act(() => {
            deployment.say({
                kind: 'mail.changed',
                account: 'work',
                folder: 'INBOX',
                emails: ['message-0'],
            });
        });

        await rows();
        expect(asked.requests.length).toBe(before + 1);
    });

    // The standing rule for every list in this client: it is drawn as a skeleton once, and a change after that reaches
    // the rows it touched rather than the list. So what is asserted here is which rows carry the design's animation,
    // not that the list read something again — that is the pair of cases above.
    //
    // And the row that carries it is decided by the answer rather than by the signal. What a signal names is mail the
    // deployment wrote down, which is a different question from whether a reader would see the row differently: the
    // page it makes the list read again is what says that, one row at a time.
    it('moves only the rows a page read again draws differently', async () => {
        const deployment = deploymentSaying();
        // The same folder, answered a second time with one row's read mark moved — which is a difference a reader sees.
        const moved = pageOf([
            message(0),
            message(1, { unread: true }),
            ...Array.from({ length: rowsPerPage - 2 }, (_, at) => message(at + 2)),
        ]);
        let reads = 0;

        renderList(
            () => {
                reads += 1;

                return Promise.resolve({ status: 200, body: reads === 1 ? wholeFolder : moved, headers: {} });
            },
            { changes: deployment.changes },
        );
        await rows();

        act(() => {
            deployment.say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['message-1'] });
        });

        await waitFor(() => {
            expect([...row(1).classList]).toContain('animate-row-changed');
        });

        expect([...row(0).classList]).not.toContain('animate-row-changed');
    });

    // The defect this whole mechanism exists against: a deployment that re-derived a preview, re-counted a passage, or
    // merely wrote the record down again names the mail it touched, and a list reading the signal rather than the
    // answer washed every row of the page it was on — which is the reload a reader sees as the list reappearing.
    it('moves no row where the page it read again carries the same mail', async () => {
        const deployment = deploymentSaying();
        const asked = recording(wholeFolder);

        renderList(asked.transport, { changes: deployment.changes });
        await rows();

        const before = asked.requests.length;

        act(() => {
            deployment.say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['message-1'] });
        });

        await waitFor(() => {
            expect(asked.requests.length).toBe(before + 1);
        });
        await act(async () => {
            await Promise.resolve();
        });

        for (const drawn of await rows()) {
            expect([...drawn.classList]).not.toContain('animate-row-changed');
            expect([...drawn.classList]).not.toContain('animate-row-landing');
        }
    });

    it('lands the rows a later page brought, and none of the ones it had already drawn', async () => {
        const deployment = deploymentSaying();
        // Named past the end of the folder the first read answered, so it is a row this list has genuinely not drawn
        // before rather than one already on the page under another number.
        const arriving = [message(1000), ...Array.from({ length: rowsPerPage - 1 }, (_, at) => message(at))];
        let reads = 0;

        renderList(
            () => {
                reads += 1;

                return Promise.resolve({
                    status: 200,
                    body: reads === 1 ? wholeFolder : pageOf(arriving),
                    headers: {},
                });
            },
            { changes: deployment.changes },
        );

        await rows();

        act(() => {
            deployment.say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 1 });
        });

        await waitFor(() => {
            expect([...row(1000).classList]).toContain('animate-row-landing');
        });

        expect([...row(0).classList]).not.toContain('animate-row-landing');
    });

    // A row carried out of the window is unmounted, and an unmounted row never reports its animation ending — so the
    // scroll is what has to let go of it. Without that, coming back to it draws the animation again for something that
    // changed a folder's length of scrolling ago.
    it('stops holding a row that changed once a scroll has carried it out of the list', async () => {
        const deployment = deploymentSaying();
        const moved = pageOf([
            message(0, { unread: true }),
            ...Array.from({ length: rowsPerPage - 1 }, (_, at) => message(at + 1)),
        ]);
        let reads = 0;

        renderList(
            () => {
                reads += 1;

                return Promise.resolve({ status: 200, body: reads === 1 ? wholeFolder : moved, headers: {} });
            },
            { changes: deployment.changes },
        );
        await rows();

        act(() => {
            deployment.say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['message-0'] });
        });

        await waitFor(() => {
            expect([...row(0).classList]).toContain('animate-row-changed');
        });

        const scroller = screen.getByRole('listbox', { name: 'Messages' }).parentElement;

        if (scroller === null) {
            throw new Error('The list draws no scroller around its rows.');
        }

        fireEvent.scroll(scroller, { target: { scrollTop: 40 * estimatedRowHeight } });

        expect(screen.queryByRole('option', { name: /Message 0$/ })).toBeNull();

        fireEvent.scroll(scroller, { target: { scrollTop: 0 } });

        expect([...row(0).classList]).not.toContain('animate-row-changed');
    });

    it('redraws a row from a stated flag without reading the page it is on again', async () => {
        const deployment = deploymentSaying();
        const asked = recording(pageOf([message(0), message(1)]));

        renderList(asked.transport, { changes: deployment.changes });
        await rows();

        const before = asked.requests.length;

        act(() => {
            deployment.say({
                kind: 'mail.flags.changed',
                account: 'work',
                folder: 'INBOX',
                flags: [{ email: 'message-0', isSeen: false, isFlagged: true }],
            });
        });

        const drawn = await rows();

        expect(drawn[0]?.textContent).toContain('Unread');
        expect(drawn[0]?.textContent).toContain('Flagged');
        expect(drawn[1]?.textContent).not.toContain('Unread');
        expect(asked.requests.length).toBe(before);
        expect([...row(0).classList]).toContain('animate-row-changed');
    });

    it('draws the most actionable reading on a row that carries one, without displacing the mail', async () => {
        renderList(answering(pageOf([message(0, { enrichment: derived }), message(1)])));
        await rows();

        const enriched = screen.getByRole('option', { name: /An answer is owed by Friday\.$/ });

        expect(enriched.textContent).toContain('An answer is owed by Friday.');
        expect(enriched.textContent).toContain('Writer 0');
        expect(enriched.textContent).toContain('Message 0');
    });

    it('draws an ordinary row for a message no derivation reached', async () => {
        renderList(answering(pageOf([message(0, { enrichment: derived }), message(1)])));
        await rows();

        const plain = row(1);

        expect(plain.textContent).toContain('Message 1');
        expect(plain.textContent).not.toContain('An answer is owed by Friday.');
        expect(within(plain).queryByRole('status')).toBeNull();
    });

    it('restores the plain row when the readings are turned off for the view', async () => {
        renderList(answering(pageOf([message(0, { enrichment: derived })])));
        await rows();

        openFilters();
        fireEvent.click(screen.getByRole('switch', { name: 'Show a reading on each row' }));

        const plain = row(0);

        expect(plain.textContent).not.toContain('An answer is owed by Friday.');
        expect(plain.textContent).toContain('Writer 0');
        expect(plain.textContent).toContain('Message 0');
    });

    it('keeps the switch with the view it was turned off in, so returning to the folder finds it off', async () => {
        renderList(answering(pageOf([message(0, { enrichment: derived })])));
        await rows();

        openFilters();
        fireEvent.click(screen.getByRole('switch', { name: 'Show a reading on each row' }));

        expect(rememberedListing(session.baseAddress, everything).readingsShown).toBe(false);
    });

    it('offers checking a reading from the row that carries one, and from no other row', async () => {
        renderList(answering(pageOf([message(0, { enrichment: derived }), message(1)])));
        await rows();

        fireEvent.contextMenu(screen.getByRole('option', { name: /An answer is owed by Friday\.$/ }));
        expect(screen.getByRole('menuitem', { name: 'AI summary' })).toBeTruthy();

        fireEvent.keyDown(document, { key: 'Escape' });
        fireEvent.contextMenu(row(1));

        expect(screen.queryByRole('menuitem', { name: 'AI summary' })).toBeNull();
    });

    it('reads nothing again for a change in another account than the one it is showing', async () => {
        const deployment = deploymentSaying();
        const asked = recording(wholeFolder);

        renderList(asked.transport, { scope: { kind: 'account', accountId: 'work' }, changes: deployment.changes });
        await rows();

        const before = asked.requests.length;

        act(() => {
            deployment.say({ kind: 'mail.arrived', account: 'personal', folder: 'INBOX', count: 2 });
        });

        await rows();
        expect(asked.requests.length).toBe(before);
    });
});

// What a finger carrying a row aside reaches. The gesture itself — the threshold, the cancellation, the suppression —
// belongs to the row and is proven there; what is proven here is that the list hands the row the two acts it already
// performs from the same row's menu, rather than a second way of archiving mail.
describe('MessageList, under a finger carried across a row', () => {
    // The places the list has drawn, which is what every act names and which a list under no provider holds none of.
    const listed = {
        ...nothingListed,
        placeOf: (id: string) => ({
            storedEmailId: id,
            account: 'work',
            folder: 'INBOX',
            unread: false,
            flagged: false,
        }),
    };

    function listWithPlaces(drawn: Partial<Drawn> = {}): RenderResult {
        return render(<ListedMailContext value={listed}>{listUnder(answering(wholeFolder), drawn)}</ListedMailContext>);
    }

    /** A finger landing on the row, travelling far enough to have asked, and lifting there. */
    function carry(swiped: HTMLElement, across: number): void {
        const landed = { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 };
        const travelled = { pointerId: 1, pointerType: 'touch', clientX: across, clientY: 0 };

        fireEvent.pointerDown(swiped, landed);
        fireEvent.pointerMove(swiped, travelled);
        fireEvent.pointerUp(swiped, travelled);
    }

    it('files the message away when the finger goes right, through the act every other surface performs', async () => {
        const performed = vi.fn();

        listWithPlaces({ acts: { ...nothingActed, refusalOf: () => null, perform: performed } });
        await rows();

        carry(row(0), swipeDistance);

        expect(performed).toHaveBeenCalledWith('archive', [
            { storedEmailId: 'message-0', account: 'work', folder: 'INBOX', unread: false, flagged: false },
        ]);
    });

    it('opens the message and starts an answer to it when the finger goes left', async () => {
        listWithPlaces();
        await rows();

        carry(row(0), -swipeDistance);

        expect(composing.compose).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: 'message-0',
        });

        expect(carried().selection).toBe('message-0');
    });

    it('files nothing away where the deployment refuses to, the row springing back instead', async () => {
        const performed = vi.fn();

        listWithPlaces({ acts: { ...nothingActed, refusalOf: () => 'noArchiveFolder', perform: performed } });
        await rows();

        carry(row(0), swipeDistance);

        expect(performed).not.toHaveBeenCalled();
    });
});
