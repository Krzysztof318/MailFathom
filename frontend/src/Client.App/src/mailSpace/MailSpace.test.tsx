// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from '../shell/screenLayers';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { listWidthStep, readListWidth, startingListWidth, storeListWidth } from './listWidth';
import { MailSpace } from './MailSpace';

// Not catalogue entries: each stands for whatever the frame composes for the region, which is the point of the props.
const handedTheFolders = 'The folder tree this space was handed.';
const handedTheTabs = 'The tab strip this space was handed.';
const handedTheList = 'The message list this space was handed.';
const handedToMail = 'The mail this space was handed.';
const handedTheIntent = 'The question this space was handed.';
const handedTheStatus = 'The connection this space was handed.';
const chooseTheInbox = 'Choose the inbox, as the folder tree would.';

// The width is what the composition is decided by, and jsdom has no width — so the media query is answered here, the
// way the theme's own test answers the colour-scheme query.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
const declaredShowModal = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
const declaredClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');

// The three breakpoints answer separately, which is what makes the four compositions distinguishable here at all: the
// space asks whether there is room for two panes, whether the mailboxes have a column, and whether the toolbar is drawn,
// and each is its own width. The query carries that width, so it is read out of the query rather than from a table this
// file would have to keep in step with the stylesheet. A query naming no width is not one of the three and matches
// nothing. The listeners are kept because a composition also changes under a window somebody drags, which is what
// `theWindowBecomes` below is: the same screen, told it has another width.
let watching: (() => void)[] = [];
let room = 0;

function atWidth(pixels: number): void {
    watching = [];
    room = pixels;

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/.exec(query)?.[1];

            return {
                matches: named !== undefined && room >= Number(named) * 16,
                media: query,
                addEventListener: (_: string, watch: () => void) => {
                    watching.push(watch);
                },
                removeEventListener: (_: string, watch: () => void) => {
                    watching = watching.filter((watched) => watched !== watch);
                },
            };
        },
    });
}

/** A window dragged to another width, which is the one thing a composition changes on while a screen stays put. */
function theWindowBecomes(pixels: number): void {
    room = pixels;

    act(() => {
        for (const watch of [...watching]) {
            watch();
        }
    });
}

// The four widths the design project frames its compositions at, named here so a test says which composition it is
// about rather than repeating a number.
const phone = 390;
const fold = 884;
const tablet = 1024;
const desktop = 1440;

// The drawer is the platform's own modal dialog, which jsdom implements none of: opening and closing it are recorded
// as the `open` attribute the platform would set and clear, which is what every assertion below reads.
//
// Closing also fires the `close` event, because that is what the space listens to rather than a value of its own —
// whichever way out was taken, the control inside the drawer, Escape, or the folder that was picked in it. A double
// that only flipped the attribute would leave every one of those paths asserting a screen the component's own state
// had stopped agreeing with.
function withModalDialogs(): void {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
        configurable: true,
        value(this: HTMLDialogElement) {
            this.setAttribute('open', '');
        },
    });
    Object.defineProperty(HTMLDialogElement.prototype, 'close', {
        configurable: true,
        value(this: HTMLDialogElement) {
            if (!this.hasAttribute('open')) {
                return;
            }

            this.removeAttribute('open');
            this.dispatchEvent(new Event('close'));
        },
    });
}

// Stands in for the folder tree: one control that scopes the workspace to the inbox, which is what choosing a folder
// in the tree does.
function ChooseInbox() {
    const { revise } = useWorkspace();

    return (
        <button
            type="button"
            onClick={() => {
                revise({ scope: { kind: 'role', role: 'Inbox' } });
            }}
        >
            {chooseTheInbox}
        </button>
    );
}

function Opening({ change }: { readonly change: Partial<Workspace> }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise(change);
    }, [revise, change]);

    return null;
}

// The toolbar and the corner control both ask whether writing a message is offered. Nothing here is about writing one,
// so nothing offers it and both stand as the planned controls they were.
const nothingBeingWritten = {
    offered: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function renderSpace(
    pixels: number,
    opening: Partial<Workspace> = {},
    person: string | null = 'reader',
    composing: Composing = nothingBeingWritten,
): { readonly current: ScreenLayers } {
    atWidth(pixels);
    withModalDialogs();

    // The shell the space is drawn inside, so what it opens over the screen is recorded where the back gesture would
    // find it. Its three functions are the same ones for the life of the stack, so the value handed to the provider
    // stays current however the count moves.
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <LocalizationProvider>
            <ScreenLayersContext value={result.current}>
                <WorkspaceProvider>
                    <ComposingContext value={composing}>
                        <Opening change={opening} />
                        <MailSpace
                            folders={
                                <>
                                    <p>{handedTheFolders}</p>
                                    <ChooseInbox />
                                </>
                            }
                            list={<p>{handedTheList}</p>}
                            mail={<p>{handedToMail}</p>}
                            tabs={<p>{handedTheTabs}</p>}
                            intent={<p>{handedTheIntent}</p>}
                            status={<p>{handedTheStatus}</p>}
                            person={person}
                        />
                    </ComposingContext>
                </WorkspaceProvider>
            </ScreenLayersContext>
        </LocalizationProvider>,
    );

    return result;
}

// The two things the space does differently once writing a message is on offer: the corner control becomes one that
// works, and the reading column comes forward for what is being written exactly as it does for what is being read.
const writingIsOffered = { ...nothingBeingWritten, offered: true };
const somethingBeingWritten = { ...writingIsOffered, opening: { kind: 'new' } as const };

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    if (declaredShowModal === undefined) {
        Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
    } else {
        Object.defineProperty(HTMLDialogElement.prototype, 'showModal', declaredShowModal);
    }

    if (declaredClose === undefined) {
        Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
    } else {
        Object.defineProperty(HTMLDialogElement.prototype, 'close', declaredClose);
    }

    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.restoreAllMocks();
});

describe('MailSpace, wide', () => {
    it('puts a grip on the boundary, at the width this person last settled on', () => {
        storeListWidth('karolina', 420);

        renderSpace(desktop, {}, 'karolina');

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe('420');
    });

    it('opens at the starting width for somebody who has settled on none', () => {
        renderSpace(desktop, {}, 'marta');

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe(
            String(startingListWidth),
        );
    });

    it('keeps the width a reader moves the grip to, and offers it back on the next start', () => {
        renderSpace(desktop, {}, 'karolina');

        fireEvent.keyDown(screen.getByRole('separator', { name: 'Message list width' }), { key: 'ArrowRight' });

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe(
            String(startingListWidth + listWidthStep),
        );
        expect(readListWidth('karolina')).toBe(startingListWidth + listWidthStep);
    });

    it('moves the boundary while the grip is being dragged, and settles where it was let go', () => {
        renderSpace(desktop, {}, 'karolina');

        const grip = screen.getByRole('separator', { name: 'Message list width' });
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 664 });

        // Read while the pointer is still down: the boundary follows the drag rather than jumping to where it ended.
        expect(grip.getAttribute('aria-valuenow')).toBe(String(startingListWidth + 64));

        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 664 });

        expect(readListWidth('karolina')).toBe(startingListWidth + 64);
    });

    it('draws the three columns side by side, with the toolbar over them', () => {
        renderSpace(desktop);

        expect(screen.getByRole('toolbar', { name: 'Mail actions' })).toBeDefined();
        expect(screen.getByRole('complementary', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Message list' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'What is open' })).toBeDefined();
        expect(screen.getByText(handedTheFolders)).toBeDefined();
        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.getByText(handedToMail)).toBeDefined();
    });

    it('puts the connection at the foot of the mailbox column and the question at the foot of the reading column', () => {
        renderSpace(desktop);

        expect(screen.getByRole('complementary').contains(screen.getByText(handedTheStatus))).toBe(true);
        expect(screen.getByRole('region', { name: 'What is open' }).contains(screen.getByText(handedTheIntent))).toBe(
            true,
        );
    });

    it('folds the mailbox column to a rail and opens it again, from a control named for each', () => {
        renderSpace(desktop);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        expect(screen.queryByRole('button', { name: 'Collapse the mailbox column' })).toBeNull();
        expect(screen.getByRole('complementary').className).toContain('w-mailboxes-folded');

        fireEvent.click(screen.getByRole('button', { name: 'Expand the mailbox column' }));

        expect(screen.queryByRole('button', { name: 'Expand the mailbox column' })).toBeNull();
        expect(screen.getByRole('complementary').className).toContain('w-mailboxes');
    });

    it('keeps the mailboxes in the folded rail, and drops what a rail has no room to say', () => {
        renderSpace(desktop);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        expect(screen.getByText(handedTheFolders)).toBeDefined();
        expect(screen.queryByText('Folders')).toBeNull();
        expect(screen.queryByText(handedTheStatus)).toBeNull();
    });

    it('draws the AI filters as symbols in the folded rail, where their names would not fit', () => {
        renderSpace(desktop);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        const filters = screen.getByRole('region', { name: 'AI filters' });

        expect(filters.querySelectorAll('button[aria-disabled="true"]').length).toBe(3);
        expect(filters.textContent).toBe('');
        expect(screen.getByRole('button', { name: 'Needs a decision — not built yet' })).toBeDefined();
    });

    it('draws every action of the toolbar, each saying in its own name why it cannot act here', () => {
        renderSpace(desktop);

        const toolbar = screen.getByRole('toolbar', { name: 'Mail actions' });

        expect(toolbar.textContent).toBeDefined();

        for (const name of ['New message', 'Reply', 'Reply all', 'Forward']) {
            expect(screen.getByRole('button', { name: `${name} — not built yet` }).getAttribute('aria-disabled')).toBe(
                'true',
            );
        }

        // The five that change a mailbox are refused for a reason of their own rather than for not being built: this
        // space is drawn with no session above it, which is a client that may change nothing.
        for (const name of ['Archive', 'Delete', 'Flag', 'Mark unread', 'Move']) {
            expect(
                screen
                    .getByRole('button', {
                        name: `${name} — this credential may not change mail on your mail server.`,
                    })
                    .getAttribute('aria-disabled'),
            ).toBe('true');
        }
    });

    it('puts the strip of what is open above the toolbar, over the whole width', () => {
        renderSpace(desktop);

        const strip = screen.getByText(handedTheTabs);

        expect(strip.compareDocumentPosition(screen.getByRole('toolbar', { name: 'Mail actions' }))).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
    });

    it('offers the three AI filters as what the product will have rather than as working controls', () => {
        renderSpace(desktop);

        const filters = screen.getByRole('region', { name: 'AI filters' });

        expect(filters.querySelectorAll('button[aria-disabled="true"]').length).toBe(3);
    });
});

describe('MailSpace, narrow', () => {
    it('draws no grip, because one column at a time has no boundary to move', () => {
        renderSpace(phone);

        expect(screen.queryByRole('separator', { name: 'Message list width' })).toBeNull();
    });

    it('draws no strip of what is open, there being no room above one column for a row of tabs', () => {
        renderSpace(phone);

        expect(screen.queryByText(handedTheTabs)).toBeNull();
    });

    it('draws the list alone while nothing is open, with the mailboxes behind a control and no toolbar', () => {
        renderSpace(phone);

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.queryByText(handedToMail)).toBeNull();
        expect(screen.queryByRole('toolbar')).toBeNull();
        expect(screen.getByRole('button', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'New message — not built yet' })).toBeDefined();
    });

    it('draws a corner control that works where writing a message is on offer', () => {
        renderSpace(phone, {}, 'reader', writingIsOffered);

        expect(screen.getByRole('button', { name: 'New message' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'New message — not built yet' })).toBeNull();
    });

    it('brings the reading column in front of the list for a message being written, as it does for one being read', () => {
        renderSpace(phone, {}, 'reader', somethingBeingWritten);

        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByText(handedTheList)).toBeNull();

        // The corner control belongs to the list, so it goes with it rather than standing over what is being written.
        expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
    });

    it('opens the mailboxes in a drawer that closes from its own control', () => {
        const shell = renderSpace(phone);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        const drawer = screen.getByRole('dialog', { name: 'Folders and filters' });

        expect(drawer.hasAttribute('open')).toBe(true);
        expect(drawer.contains(screen.getByText(handedTheFolders))).toBe(true);
        expect(drawer.contains(screen.getByText(handedTheStatus))).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: 'Close the folders' }));

        // The shell's record of it goes with the drawer itself: a step left on the stack is a press of the back
        // gesture spent closing something that is no longer there.
        expect(drawer.hasAttribute('open')).toBe(false);
        expect(shell.current.depth).toBe(0);
    });

    // A window widened past the room for a mailbox column takes the drawer off the screen with it, and nothing it
    // would have closed by fires: the element is simply gone. What must not be left behind is the shell's record of
    // it, which is a step the back gesture would spend on a surface nobody can see.
    it('leaves nothing standing over the screen once the window widens to a mailbox column', () => {
        const shell = renderSpace(tablet);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        expect(shell.current.depth).toBe(1);

        theWindowBecomes(desktop);

        expect(shell.current.depth).toBe(0);
    });

    it('closes the drawer once a scope is chosen in it, because the mail is behind it', () => {
        const shell = renderSpace(phone);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        const drawer = screen.getByRole('dialog', { name: 'Folders and filters' });
        expect(drawer.hasAttribute('open')).toBe(true);
        expect(shell.current.depth).toBe(1);

        fireEvent.click(screen.getByRole('button', { name: chooseTheInbox }));

        expect(drawer.hasAttribute('open')).toBe(false);
        expect(shell.current.depth).toBe(0);
    });

    // The back gesture is the third way out of it, and the only one that reaches the drawer through the shell rather
    // than through a control on the screen. What it closes is the same element by the same method, which is what the
    // registration holds.
    it('closes the drawer when the back gesture reaches it', () => {
        const shell = renderSpace(phone);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        const drawer = screen.getByRole('dialog', { name: 'Folders and filters' });

        act(() => {
            shell.current.closeTop();
        });

        expect(drawer.hasAttribute('open')).toBe(false);
        expect(shell.current.depth).toBe(0);
    });

    it('puts the message in front of the list once one is open, and returns to the list from it', () => {
        renderSpace(phone, { selection: 'stored-1' });

        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByText(handedTheList)).toBeNull();
        expect(screen.getByText(handedTheIntent)).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Back to the list' }));

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.queryByText(handedToMail)).toBeNull();
    });

    it('puts focus at the start of the list on the way back, which is a view change', () => {
        renderSpace(phone, { selection: 'stored-1' });

        fireEvent.click(screen.getByRole('button', { name: 'Back to the list' }));

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Message list' }));
    });
});

// The four compositions the design project frames, each at the width it frames it at. Three separate questions decide
// them rather than one asked three ways, so the fold and the tablet are stated as cases of their own: both carry two
// panes and keep the mailboxes behind a control, which is neither the phone's composition nor the desktop's.
describe('MailSpace, in the four compositions', () => {
    it('shows the list alone at a phone width, with a message coming in front of it rather than beside it', () => {
        renderSpace(phone, { selection: 'stored-1' });

        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByText(handedTheList)).toBeNull();
    });

    it('shows the list and the message together at the fold, which is above the width one pane stops at', () => {
        renderSpace(fold, { selection: 'stored-1' });

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Back to the list' })).toBeNull();
    });

    it('keeps the mailboxes behind a control at the fold, there being no room for a column of them', () => {
        renderSpace(fold);

        expect(screen.getByRole('button', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.queryByRole('complementary', { name: 'Folders and filters' })).toBeNull();
    });

    it('keeps the mailboxes behind a control at a tablet width as well, which is the same composition', () => {
        renderSpace(tablet);

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Folders and filters' })).toBeDefined();
    });

    it('gives the mailboxes a column of their own at a desktop width, rather than a control that opens one', () => {
        renderSpace(desktop);

        expect(screen.getByRole('complementary', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Folders and filters' })).toBeNull();
    });

    it('draws the strip of what is open only in the desktop composition, which is the only one with room above two columns', () => {
        renderSpace(tablet);

        expect(screen.queryByText(handedTheTabs)).toBeNull();
    });
});
