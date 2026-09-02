// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { everything } from '../workspace/mailScope';
import { SearchFilters } from './SearchFilters';
import { askIn, type MailSearchAsk } from './searchAsk';

const work: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const anywhere = askIn(everything, 'invoice');

function renderFilters(ask: MailSearchAsk, onNarrow: (ask: MailSearchAsk) => void = () => undefined): void {
    render(
        <LocalizationProvider>
            <SearchFilters ask={ask} accounts={[work]} onNarrow={onNarrow} />
        </LocalizationProvider>,
    );
}

// The panel is folded away, and everything inside it is reached by opening it first.
function narrowing(): void {
    fireEvent.click(screen.getByText('Narrow this search'));
}

function filtersInForce(): HTMLElement[] {
    return within(screen.getByRole('list', { name: 'Filters this search is under' })).getAllByRole('listitem');
}

describe('SearchFilters', () => {
    it('says what a search with nothing on it covers, which no filter could say', () => {
        renderFilters(anywhere);

        expect(screen.getByText('Searching every mailbox and folder.')).toBeTruthy();
        expect(screen.queryByRole('list', { name: 'Filters this search is under' })).toBeNull();
    });

    it('draws the mailbox a search was started in by the name the reader knows it by', () => {
        renderFilters(askIn({ kind: 'account', accountId: 'work' }, 'invoice'));

        expect(filtersInForce()[0]?.textContent).toContain('Mailbox: Work');
    });

    it('draws every filter in force as an object of its own', () => {
        renderFilters({ ...anywhere, sender: 'somebody@example.invalid', unread: true });

        const drawn = filtersInForce().map((filter) => filter.textContent);

        expect(drawn).toStrictEqual([
            expect.stringContaining('From somebody@example.invalid') as unknown,
            expect.stringContaining('Only unread') as unknown,
        ]);
    });

    it('writes a day as the reader’s language writes one rather than as the control holds it', () => {
        renderFilters({ ...anywhere, receivedFrom: '2026-08-15' });

        const written = new Intl.DateTimeFormat('en', { dateStyle: 'long' }).format(new Date(2026, 7, 15));

        expect(filtersInForce()[0]?.textContent).toContain(written);
    });

    it('takes one filter off without touching the others', () => {
        const narrowed = vi.fn();

        renderFilters({ ...anywhere, sender: 'somebody@example.invalid', unread: true }, narrowed);
        fireEvent.click(screen.getByRole('button', { name: 'Remove the filter Only unread' }));

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, sender: 'somebody@example.invalid', unread: null });
    });

    it('adds a mailbox to search in', () => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('In this mailbox'), { target: { value: 'work' } });

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, account: 'work' });
    });

    it('adds a folder to search in, by the role it plays across every mailbox', () => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('In this folder'), { target: { value: 'role:Sent' } });

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, folder: 'role:Sent' });
    });

    it('offers the folder somebody was looking at back, where it plays no role', () => {
        renderFilters(askIn({ kind: 'folder', accountId: 'work', alias: 'Projects/Nordwind' }, 'invoice'));
        narrowing();

        const chosen = screen.getByLabelText('In this folder');

        expect(chosen).toHaveProperty('value', 'Projects/Nordwind');
    });

    it('adds an address the sender has to carry', () => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('From this address'), {
            target: { value: 'somebody@example.invalid' },
        });
        fireEvent.click(screen.getAllByRole('button', { name: 'Add' })[0] ?? document.body);

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, sender: 'somebody@example.invalid' });
    });

    it('says which field to correct rather than sending something no address could be', () => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('From this address'), { target: { value: 'nordwind' } });
        fireEvent.click(screen.getAllByRole('button', { name: 'Add' })[0] ?? document.body);

        expect(screen.getByRole('alert').textContent).toContain('That is not an address.');
        expect(narrowed).not.toHaveBeenCalled();
    });

    it('adds a day the search reaches back to', () => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('Arrived on or after'), { target: { value: '2026-08-01' } });

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, receivedFrom: '2026-08-01' });
    });

    it('says a range selects nothing rather than searching one that cannot', () => {
        const narrowed = vi.fn();

        renderFilters({ ...anywhere, receivedFrom: '2026-09-01' }, narrowed);
        narrowing();
        fireEvent.change(screen.getByLabelText('Arrived on or before'), { target: { value: '2026-08-31' } });

        expect(screen.getByRole('alert').textContent).toContain('The last day falls before the first one');
        expect(narrowed).not.toHaveBeenCalled();
    });

    it('keeps the refused day in the control, so it is corrected rather than typed again', () => {
        const narrowed = vi.fn();

        renderFilters({ ...anywhere, receivedFrom: '2026-09-01' }, narrowed);
        narrowing();

        const lastDay = screen.getByLabelText('Arrived on or before');

        fireEvent.change(lastDay, { target: { value: '2026-08-31' } });

        expect((lastDay as HTMLInputElement).value).toBe('2026-08-31');

        // The first day is moved back off the refused pair the reader is looking at, rather than off the filter that
        // is still in force — which is what makes the range selectable in one correction rather than two.
        fireEvent.change(screen.getByLabelText('Arrived on or after'), { target: { value: '2026-08-01' } });

        expect(narrowed).toHaveBeenCalledWith({
            ...anywhere,
            receivedFrom: '2026-08-01',
            receivedTo: '2026-08-31',
        });
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it.each([
        ['Only unread', { unread: true }],
        ['Only flagged', { flagged: true }],
        ['Only with attachments', { hasAttachments: true }],
        ['Including junk', { includeJunk: true }],
    ])('narrows a search to %s', (label, narrowing_) => {
        const narrowed = vi.fn();

        renderFilters(anywhere, narrowed);
        narrowing();
        fireEvent.click(screen.getByLabelText(label));

        expect(narrowed).toHaveBeenCalledWith({ ...anywhere, ...narrowing_ });
    });
});
