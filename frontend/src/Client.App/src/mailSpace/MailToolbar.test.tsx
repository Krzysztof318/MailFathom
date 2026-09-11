// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Composing } from '../composer/useComposing';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { MailToolbar } from './MailToolbar';
import type { StripFit } from './useStripFit';

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

const messageId = '00000000-0000-4000-8000-000000000000';

const place = { storedEmailId: messageId, account: 'work', folder: 'work-inbox', unread: false };

function Opens({ selection }: { readonly selection: string | null }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise({ selection });
    }, [revise, selection]);

    return null;
}

function drawToolbar(
    offered: boolean,
    selection: string | null = null,
    acts: MailboxActs = nothingActed,
    fit: StripFit = 'labelled',
): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = { offered, opening: null, compose: composed, close: () => undefined };

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListedMailContext value={{ ...nothingListed, placeOf: () => place }}>
                    <MailboxActsContext value={acts}>
                        <ComposingContext value={composing}>
                            <Opens selection={selection} />
                            <MailToolbar strip={() => undefined} fit={fit} />
                        </ComposingContext>
                    </MailboxActsContext>
                </ListedMailContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return { composed };
}

function actsOffering(performed: MailboxActs['perform']): MailboxActs {
    return { ...nothingActed, refusalOf: () => null, perform: performed };
}

// jsdom evaluates no media query, so the setup answers every one of them with `false` and this strip is drawn in the
// single-pane composition unless a test says otherwise. Stating a width is stating what `useTwoPanes` reads, which is
// a `min-width` query built from the token — so what is answered here is that query and nothing else.
function theCompositionHasTwoPanes(): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('MailToolbar', () => {
    it('opens a message of its own from the control the design puts first', () => {
        const { composed } = drawToolbar(true);

        fireEvent.click(screen.getByRole('button', { name: 'New message' }));

        expect(composed).toHaveBeenCalledWith({ kind: 'new' });
    });

    it.each([
        ['Reply', 'senderOnly'],
        ['Reply all', 'everyone'],
        ['Forward', 'forward'],
    ])('answers what is open with %s', (name, answers) => {
        const { composed } = drawToolbar(true, messageId);

        fireEvent.click(screen.getByRole('button', { name }));

        expect(composed).toHaveBeenCalledWith({ kind: 'answer', answers, storedEmailId: messageId });
    });

    it('draws answering as what it will be while nothing is open, rather than moving under the cursor', () => {
        const { composed } = drawToolbar(true);

        fireEvent.click(screen.getByRole('button', { name: 'Reply — not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('offers no writing at all to a credential that may not file a draft', () => {
        const { composed } = drawToolbar(false, messageId);

        fireEvent.click(screen.getByRole('button', { name: 'New message — not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('says in each control’s own name why a credential without the grant cannot change a mailbox', () => {
        drawToolbar(true, messageId);

        for (const action of ['Archive', 'Delete', 'Flag', 'Mark unread', 'Move']) {
            expect(
                screen.getByRole('button', {
                    name: `${action} — this credential may not change mail on your mail server.`,
                }),
            ).toBeDefined();
        }
    });

    it.each([
        ['Archive', 'archive'],
        ['Flag', 'flag'],
        ['Mark unread', 'markUnread'],
    ])('acts on what is open when %s is pressed', (name, act) => {
        const performed = vi.fn();

        drawToolbar(true, messageId, actsOffering(performed));

        fireEvent.click(screen.getByRole('button', { name }));

        expect(performed).toHaveBeenCalledWith(act, [place]);
    });

    it('asks before deleting rather than deleting on the press, which is the one act that is asked about', () => {
        const performed = vi.fn();

        drawToolbar(true, messageId, actsOffering(performed));

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(performed).not.toHaveBeenCalled();
        expect(screen.getByRole('heading', { name: 'Delete 1 message?' })).toBeDefined();
    });

    it('draws each control as its symbol alone once the names no longer fit the strip', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'symbols');

        for (const name of ['Reply', 'Archive']) {
            expect(screen.getByRole('button', { name }).textContent).toBe('');
        }
    });

    it('gives every control its name in words while the names fit the strip', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        for (const name of ['New message', 'Reply', 'Archive']) {
            expect(screen.getByRole('button', { name }).textContent).toBe(name);
        }
    });

    it('keeps hiding the panels apart at the far edge as a symbol alone, whatever fits the strip', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        const hiding = screen.getByRole('button', {
            name: 'Hide the panels — the correspondence alone',
        });

        expect(hiding.textContent).toBe('');
        expect(hiding.className).toContain('ms-auto');
    });

    it('hides the panels when pressed and says so, so the state is readable rather than only visible', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        const hiding = screen.getByRole('button', { name: 'Hide the panels — the correspondence alone' });
        expect(hiding.getAttribute('aria-pressed')).toBe('false');

        fireEvent.click(hiding);

        const showing = screen.getByRole('button', { name: "Show the thread's panels" });
        expect(showing.getAttribute('aria-pressed')).toBe('true');
    });

    it('leaves the panel control out of a single-pane composition, which the design gives it nothing to do in', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        expect(screen.queryByRole('button', { name: 'Hide the panels — the correspondence alone' })).toBeNull();
    });

    it('gives composing up to the floating control first, keeping the other names in words', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'floating');

        expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Reply' }).textContent).toBe('Reply');
    });

    it('asks about no message at all while nothing is open, rather than about the last one that was', () => {
        const asked = vi.fn(() => null);

        drawToolbar(true, null, { ...nothingActed, refusalOf: asked });

        expect(asked).toHaveBeenCalledWith('archive', []);
    });
});
