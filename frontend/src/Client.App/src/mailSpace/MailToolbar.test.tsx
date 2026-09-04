// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Composing } from '../composer/useComposing';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { MailToolbar } from './MailToolbar';

const messageId = '00000000-0000-4000-8000-000000000000';

const place = { storedEmailId: messageId, account: 'work', folder: 'work-inbox' };

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
                            <MailToolbar />
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

    it('asks about no message at all while nothing is open, rather than about the last one that was', () => {
        const asked = vi.fn(() => null);

        drawToolbar(true, null, { ...nothingActed, refusalOf: asked });

        expect(asked).toHaveBeenCalledWith('archive', []);
    });
});
