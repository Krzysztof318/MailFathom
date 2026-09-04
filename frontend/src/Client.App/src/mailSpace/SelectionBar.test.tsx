// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { MoveDestination } from '../mailboxActs/mailboxDestinations';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, nothingListed, type ListedMail } from '../messageList/useListedMail';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { SelectionBar } from './SelectionBar';

const drawnRows: readonly ActedMessage[] = [
    { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox' },
    { storedEmailId: 'message-2', account: 'work', folder: 'work-inbox' },
];

const clients: MoveDestination = { alias: 'work-clients', name: 'Projects / Clients' };

function Picks({ selected }: { readonly selected: readonly string[] }) {
    const { workspace, revise } = useWorkspace();

    useEffect(() => {
        revise({ selected });
    }, [revise, selected]);

    return <output>{JSON.stringify(workspace)}</output>;
}

function carried(): Workspace {
    const probe = screen.getAllByRole('status').find((element) => element.textContent.startsWith('{'));

    return JSON.parse(probe?.textContent ?? '') as Workspace;
}

function drawBar(
    selected: readonly string[] = ['message-1', 'message-2'],
    acts: Partial<MailboxActs> = {},
    listed: Partial<ListedMail> = {},
): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListedMailContext
                    value={{
                        ...nothingListed,
                        placeOf: (storedEmailId) =>
                            drawnRows.find((message) => message.storedEmailId === storedEmailId) ?? null,
                        ...listed,
                    }}
                >
                    <MailboxActsContext value={{ ...nothingActed, refusalOf: () => null, ...acts }}>
                        <Picks selected={selected} />
                        <SelectionBar />
                    </MailboxActsContext>
                </ListedMailContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('SelectionBar', () => {
    it('says how many messages are picked out, where the toolbar would otherwise stand', () => {
        drawBar();

        expect(screen.getByRole('toolbar', { name: 'Actions on the messages selected' })).toBeDefined();
        expect(screen.getByText('2 selected')).toBeDefined();
    });

    it('counts what it can act on rather than what was picked, so a row the list never drew is not promised', () => {
        drawBar(['message-1', 'message-nobody-drew']);

        expect(screen.getByText('1 selected')).toBeDefined();
    });

    it('lets the selection go from the close control rather than acting on it', () => {
        drawBar();

        fireEvent.click(screen.getByRole('button', { name: 'Clear the selection' }));

        expect(carried().selected).toStrictEqual([]);
    });

    it('hands taking the listing in at once to the list, which is the only thing that knows what it holds', () => {
        const everything = vi.fn();

        drawBar(['message-1'], {}, { selectAll: everything });

        fireEvent.click(screen.getByRole('button', { name: 'Select all' }));

        expect(everything).toHaveBeenCalledOnce();
    });

    it('acts over everything picked out, and lets the selection go once the act has been asked for', () => {
        const performed = vi.fn();

        drawBar(['message-1', 'message-2'], { perform: performed });

        fireEvent.click(screen.getByRole('button', { name: 'Archive' }));

        expect(performed).toHaveBeenCalledWith('archive', drawnRows);
        expect(carried().selected).toStrictEqual([]);
    });

    it('files everything picked out in the folder somebody chose, rather than in one this client guessed', () => {
        const performed = vi.fn();

        drawBar(['message-1'], { perform: performed, destinationsOf: () => [clients] });

        fireEvent.click(screen.getByRole('button', { name: 'Move' }));
        fireEvent.click(screen.getByRole('button', { name: 'Projects / Clients' }));

        expect(performed).toHaveBeenCalledWith('move', [drawnRows[0]], clients);
    });

    it('asks before deleting, which is the one act the design puts a question in front of', () => {
        const performed = vi.fn();

        drawBar(['message-1'], { perform: performed });

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(performed).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: 'Move to the trash' }));

        expect(performed).toHaveBeenCalledWith('delete', [drawnRows[0]]);
    });

    it('says on the control itself why an act cannot be performed on what is picked out', () => {
        drawBar(['message-1'], { refusalOf: () => 'severalAccounts' });

        expect(
            screen.getByRole('button', {
                name: 'Move — messages from several accounts cannot be filed into one folder.',
            }),
        ).toBeDefined();
    });
});
