// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActControls } from './MailboxActControls';
import type { ActRefusal, MoveDestination } from './mailboxDestinations';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from './useMailboxActs';

const invoice: ActedMessage = { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox' };
const receipt: ActedMessage = { storedEmailId: 'message-2', account: 'work', folder: 'work-inbox' };

const clients: MoveDestination = { alias: 'work-clients', name: 'Projects / Clients' };

function drawControls(
    acts: Partial<MailboxActs> = {},
    messages: readonly ActedMessage[] = [invoice],
    onActed: () => void = () => undefined,
): void {
    render(
        <LocalizationProvider>
            <MailboxActsContext value={{ ...nothingActed, refusalOf: () => null, ...acts }}>
                <MailboxActControls messages={messages} shape="labelled" onActed={onActed} />
            </MailboxActsContext>
        </LocalizationProvider>,
    );
}

describe('MailboxActControls', () => {
    it('draws the five acts in the order the design puts them, one strip’s worth for either strip', () => {
        drawControls();

        expect(
            screen
                .getAllByRole('button')
                .map((control) => control.textContent)
                .filter((said) => said !== ''),
        ).toStrictEqual(['Archive', 'Delete', 'Flag', 'Mark unread', 'Move']);
    });

    // The three that file a message happen on the press and report in a toast that offers the way back, which is the
    // design project's rule: asking about every act is what teaches a reader to agree without reading.
    it.each([
        ['Archive', 'archive'],
        ['Flag', 'flag'],
        ['Mark unread', 'markUnread'],
    ] as const)('performs %s on the press rather than asking about it first', (name, act) => {
        const performed = vi.fn();
        const acted = vi.fn();

        drawControls({ perform: performed }, [invoice, receipt], acted);
        fireEvent.click(screen.getByRole('button', { name }));

        expect(performed).toHaveBeenCalledWith(act, [invoice, receipt]);
        expect(acted).toHaveBeenCalledOnce();
    });

    it('counts the messages in the question about deleting, and says both what it does and how long it can be undone', () => {
        drawControls({}, [invoice, receipt]);
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        const asked = screen.getByRole('dialog');

        expect(within(asked).getByRole('heading', { name: 'Delete 2 messages?' })).toBeDefined();
        expect(within(asked).getByText('Each one is filed in the trash folder of the account it is in.')).toBeDefined();
        expect(within(asked).getByText('You can take this back for 5 seconds afterwards.')).toBeDefined();
    });

    it('deletes nothing where the question was answered with the way back out of it', () => {
        const performed = vi.fn();
        const acted = vi.fn();

        drawControls({ perform: performed }, [invoice], acted);
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(performed).not.toHaveBeenCalled();
        expect(acted).not.toHaveBeenCalled();
    });

    it('offers the folders the messages could go to, asked of the messages rather than of the mailbox', () => {
        const offered = vi.fn(() => [clients]);

        drawControls({ destinationsOf: offered }, [invoice, receipt]);
        fireEvent.click(screen.getByRole('button', { name: 'Move' }));

        expect(offered).toHaveBeenCalledWith([invoice, receipt]);
        expect(screen.getByRole('button', { name: 'Projects / Clients' })).toBeDefined();
    });

    // The record is durable from the press and the account's pass carries it out minutes later, so the control has to
    // say the act is already happening rather than offer a submission the deployment would answer twice.
    it('says an act already asked of every message it is about is on its way, rather than offering it again', () => {
        const performed = vi.fn();

        drawControls({ asked: new Map([['message-1', 'archive']]), perform: performed });

        const control = screen.getByRole('button', {
            name: 'Archive — this is already on its way to your mail server.',
        });

        fireEvent.click(control);

        expect(control.getAttribute('aria-disabled')).toBe('true');
        expect(performed).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Flag' })).toBeDefined();
    });

    it('goes on offering an act asked of only some of the messages, the rest of them not having it yet', () => {
        drawControls({ asked: new Map([['message-1', 'archive']]) }, [invoice, receipt]);

        expect(screen.getByRole('button', { name: 'Archive' })).toBeDefined();
    });

    it.each([
        ['notOffered', 'Archive — this credential may not change mail on your mail server.'],
        ['nothingToActOn', 'Archive — nothing is open or selected for this to be about.'],
        ['noArchiveFolder', 'Archive — this account names no archive folder, so there is nowhere to archive to.'],
        ['severalAccounts', 'Archive — messages from several accounts cannot be filed into one folder.'],
        ['noOtherFolder', 'Archive — this account has no other folder to file into.'],
        ['foldersUnknown', 'Archive — MailFathom has not read your folders, so it cannot say where this would go.'],
    ] as [ActRefusal, string][])(
        'says %s in the control’s own name rather than refusing after the press',
        (refusal, said) => {
            const performed = vi.fn();

            drawControls({ refusalOf: () => refusal, perform: performed });

            const control = screen.getByRole('button', { name: said });

            expect(control.getAttribute('aria-disabled')).toBe('true');

            fireEvent.click(control);

            expect(performed).not.toHaveBeenCalled();
        },
    );
});
