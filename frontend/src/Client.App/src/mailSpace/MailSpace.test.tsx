// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
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

function atWidth(wide: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            matches: wide,
            media: query,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

// The drawer is the platform's own modal dialog, which jsdom implements none of: opening and closing it are recorded
// as the `open` attribute the platform would set and clear, which is what every assertion below reads.
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
            this.removeAttribute('open');
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
    wide: boolean,
    opening: Partial<Workspace> = {},
    person: string | null = 'reader',
    composing: Composing = nothingBeingWritten,
): void {
    atWidth(wide);
    withModalDialogs();

    render(
        <LocalizationProvider>
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
        </LocalizationProvider>,
    );
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

        renderSpace(true, {}, 'karolina');

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe('420');
    });

    it('opens at the starting width for somebody who has settled on none', () => {
        renderSpace(true, {}, 'marta');

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe(
            String(startingListWidth),
        );
    });

    it('keeps the width a reader moves the grip to, and offers it back on the next start', () => {
        renderSpace(true, {}, 'karolina');

        fireEvent.keyDown(screen.getByRole('separator', { name: 'Message list width' }), { key: 'ArrowRight' });

        expect(screen.getByRole('separator', { name: 'Message list width' }).getAttribute('aria-valuenow')).toBe(
            String(startingListWidth + listWidthStep),
        );
        expect(readListWidth('karolina')).toBe(startingListWidth + listWidthStep);
    });

    it('moves the boundary while the grip is being dragged, and settles where it was let go', () => {
        renderSpace(true, {}, 'karolina');

        const grip = screen.getByRole('separator', { name: 'Message list width' });
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 664 });

        // Read while the pointer is still down: the boundary follows the drag rather than jumping to where it ended.
        expect(grip.getAttribute('aria-valuenow')).toBe(String(startingListWidth + 64));

        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 664 });

        expect(readListWidth('karolina')).toBe(startingListWidth + 64);
    });

    it('draws the three columns side by side, with the toolbar over them', () => {
        renderSpace(true);

        expect(screen.getByRole('toolbar', { name: 'Mail actions' })).toBeDefined();
        expect(screen.getByRole('complementary', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Message list' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'What is open' })).toBeDefined();
        expect(screen.getByText(handedTheFolders)).toBeDefined();
        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.getByText(handedToMail)).toBeDefined();
    });

    it('puts the connection at the foot of the mailbox column and the question at the foot of the reading column', () => {
        renderSpace(true);

        expect(screen.getByRole('complementary').contains(screen.getByText(handedTheStatus))).toBe(true);
        expect(screen.getByRole('region', { name: 'What is open' }).contains(screen.getByText(handedTheIntent))).toBe(
            true,
        );
    });

    it('folds the mailbox column to a rail and opens it again, from a control named for each', () => {
        renderSpace(true);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        expect(screen.queryByRole('button', { name: 'Collapse the mailbox column' })).toBeNull();
        expect(screen.getByRole('complementary').className).toContain('w-mailboxes-folded');

        fireEvent.click(screen.getByRole('button', { name: 'Expand the mailbox column' }));

        expect(screen.queryByRole('button', { name: 'Expand the mailbox column' })).toBeNull();
        expect(screen.getByRole('complementary').className).toContain('w-mailboxes');
    });

    it('keeps the mailboxes in the folded rail, and drops what a rail has no room to say', () => {
        renderSpace(true);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        expect(screen.getByText(handedTheFolders)).toBeDefined();
        expect(screen.queryByText('Folders')).toBeNull();
        expect(screen.queryByText(handedTheStatus)).toBeNull();
    });

    it('draws the AI filters as symbols in the folded rail, where their names would not fit', () => {
        renderSpace(true);

        fireEvent.click(screen.getByRole('button', { name: 'Collapse the mailbox column' }));

        const filters = screen.getByRole('region', { name: 'AI filters' });

        expect(filters.querySelectorAll('button[aria-disabled="true"]').length).toBe(3);
        expect(filters.textContent).toBe('');
        expect(screen.getByRole('button', { name: 'Needs a decision — not built yet' })).toBeDefined();
    });

    it('draws every unbuilt action of the toolbar as one that says so in its own name', () => {
        renderSpace(true);

        const toolbar = screen.getByRole('toolbar', { name: 'Mail actions' });
        const names = [
            'New message',
            'Reply',
            'Reply all',
            'Forward',
            'Archive',
            'Delete',
            'Flag',
            'Mark unread',
            'Move',
        ];

        for (const name of names) {
            expect(toolbar.textContent).toBeDefined();
            expect(screen.getByRole('button', { name: `${name} — not built yet` }).getAttribute('aria-disabled')).toBe(
                'true',
            );
        }
    });

    it('puts the strip of what is open above the toolbar, over the whole width', () => {
        renderSpace(true);

        const strip = screen.getByText(handedTheTabs);

        expect(strip.compareDocumentPosition(screen.getByRole('toolbar', { name: 'Mail actions' }))).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
    });

    it('offers the three AI filters as what the product will have rather than as working controls', () => {
        renderSpace(true);

        const filters = screen.getByRole('region', { name: 'AI filters' });

        expect(filters.querySelectorAll('button[aria-disabled="true"]').length).toBe(3);
    });
});

describe('MailSpace, narrow', () => {
    it('draws no grip, because one column at a time has no boundary to move', () => {
        renderSpace(false);

        expect(screen.queryByRole('separator', { name: 'Message list width' })).toBeNull();
    });

    it('draws no strip of what is open, there being no room above one column for a row of tabs', () => {
        renderSpace(false);

        expect(screen.queryByText(handedTheTabs)).toBeNull();
    });

    it('draws the list alone while nothing is open, with the mailboxes behind a control and no toolbar', () => {
        renderSpace(false);

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.queryByText(handedToMail)).toBeNull();
        expect(screen.queryByRole('toolbar')).toBeNull();
        expect(screen.getByRole('button', { name: 'Folders and filters' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'New message — not built yet' })).toBeDefined();
    });

    it('draws a corner control that works where writing a message is on offer', () => {
        renderSpace(false, {}, 'reader', writingIsOffered);

        expect(screen.getByRole('button', { name: 'New message' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'New message — not built yet' })).toBeNull();
    });

    it('brings the reading column in front of the list for a message being written, as it does for one being read', () => {
        renderSpace(false, {}, 'reader', somethingBeingWritten);

        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByText(handedTheList)).toBeNull();

        // The corner control belongs to the list, so it goes with it rather than standing over what is being written.
        expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
    });

    it('opens the mailboxes in a drawer that closes from its own control', () => {
        renderSpace(false);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        const drawer = screen.getByRole('dialog', { name: 'Folders and filters' });

        expect(drawer.hasAttribute('open')).toBe(true);
        expect(drawer.contains(screen.getByText(handedTheFolders))).toBe(true);
        expect(drawer.contains(screen.getByText(handedTheStatus))).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: 'Close the folders' }));

        expect(drawer.hasAttribute('open')).toBe(false);
    });

    it('closes the drawer once a scope is chosen in it, because the mail is behind it', () => {
        renderSpace(false);

        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));
        const drawer = screen.getByRole('dialog', { name: 'Folders and filters' });
        expect(drawer.hasAttribute('open')).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: chooseTheInbox }));

        expect(drawer.hasAttribute('open')).toBe(false);
    });

    it('puts the message in front of the list once one is open, and returns to the list from it', () => {
        renderSpace(false, { selection: 'stored-1' });

        expect(screen.getByText(handedToMail)).toBeDefined();
        expect(screen.queryByText(handedTheList)).toBeNull();
        expect(screen.getByText(handedTheIntent)).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Back to the list' }));

        expect(screen.getByText(handedTheList)).toBeDefined();
        expect(screen.queryByText(handedToMail)).toBeNull();
    });

    it('puts focus at the start of the list on the way back, which is a view change', () => {
        renderSpace(false, { selection: 'stored-1' });

        fireEvent.click(screen.getByRole('button', { name: 'Back to the list' }));

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Message list' }));
    });
});
