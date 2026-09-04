// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import type { ActRefusal } from '../mailboxActs/mailboxDestinations';
import {
    MailboxActsContext,
    nothingActed,
    type ActedMessage,
    type MailboxAct,
    type MailboxActs,
} from '../mailboxActs/useMailboxActs';
import { MessageRowMenu } from './MessageRowMenu';

const email = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'Contract annex — signatures',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'anna@contoso.example',
    senderDisplayName: 'Anna Kowalska',
    toAddresses: ['owner@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
} as unknown as MailTimelineEntry;

const messages: readonly ActedMessage[] = [{ storedEmailId: email.id, account: 'work', folder: 'INBOX' }];

function actsWhere(refusalOf: (act: MailboxAct) => ActRefusal | null, perform = vi.fn()): MailboxActs {
    return { ...nothingActed, refusalOf, perform };
}

const writing: Composing = { offered: true, opening: null, compose: vi.fn(), close: vi.fn() };

function menuUnder({
    acts = actsWhere(() => null),
    composing = writing,
    onSelect = vi.fn(),
    onAsk = vi.fn(),
}: {
    acts?: MailboxActs;
    composing?: Composing;
    onSelect?: () => void;
    onAsk?: (act: 'delete' | 'move', messages: readonly ActedMessage[]) => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <MailboxActsContext value={acts}>
                    <MessageRowMenu
                        email={email}
                        messages={messages}
                        at={{ x: 20, y: 30 }}
                        onSelect={onSelect}
                        onAsk={onAsk}
                        onClose={vi.fn()}
                    />
                </MailboxActsContext>
            </ComposingContext>
        </LocalizationProvider>,
    );
}

function drawn(): (string | null)[] {
    return screen.getAllByRole('menuitem').map((item) => item.textContent);
}

describe('MessageRowMenu', () => {
    it('draws the row’s acts in the order the design project draws them', () => {
        menuUnder();

        expect(drawn()).toStrictEqual([
            'Select messages',
            'Reply',
            'Forward',
            'Archive',
            'Flag',
            'Mark unread',
            'Move',
            'Delete',
        ]);
    });

    it('names the menu by what the row is about', () => {
        menuUnder();

        expect(screen.getByRole('menu', { name: 'Contract annex — signatures' })).toBeTruthy();
    });

    it('leaves out an act this account cannot perform rather than drawing it inert', () => {
        menuUnder({ acts: actsWhere((act) => (act === 'archive' ? 'noArchiveFolder' : null)) });

        expect(screen.queryByRole('menuitem', { name: 'Archive' })).toBeNull();
    });

    it('leaves out answering the message where this credential may not write one', () => {
        menuUnder({ composing: { ...writing, offered: false } });

        expect(screen.queryByRole('menuitem', { name: 'Reply' })).toBeNull();
        expect(screen.queryByRole('menuitem', { name: 'Forward' })).toBeNull();
    });

    it('puts the row into the selection from its first item, which is how a finger reaches one', () => {
        const selected = vi.fn();

        menuUnder({ onSelect: selected });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select messages' }));

        expect(selected).toHaveBeenCalledOnce();
    });

    it('performs an act that needs no question through the same call the toolbar presses', () => {
        const performed = vi.fn();

        menuUnder({ acts: actsWhere(() => null, performed) });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(performed).toHaveBeenCalledWith('archive', messages);
    });

    it('raises the question rather than deleting, because the question outlives the menu', () => {
        const performed = vi.fn();
        const asked = vi.fn();

        menuUnder({ acts: actsWhere(() => null, performed), onAsk: asked });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));

        expect(asked).toHaveBeenCalledWith('delete', messages);
        expect(performed).not.toHaveBeenCalled();
    });

    it('asks where a message is being filed rather than filing it', () => {
        const asked = vi.fn();

        menuUnder({ onAsk: asked });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Move' }));

        expect(asked).toHaveBeenCalledWith('move', messages);
    });

    it('starts a conversation about the message where one was asked for', () => {
        const composing: Composing = { ...writing, compose: vi.fn() };

        menuUnder({ composing });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Reply' }));

        expect(composing.compose).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: 'message-1',
        });
    });
});
