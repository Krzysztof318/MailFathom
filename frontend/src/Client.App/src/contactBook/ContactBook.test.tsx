// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ContactBook } from './ContactBook';
import type { ContactBookInForce, ContactBookName } from './useContactBook';

function personCalled(id: string, displayName: string): Contact {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        origin: 'Asserted',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

const anna = personCalled('anna', 'Anna Kowalska');
const bartek = personCalled('bartek', 'Bartek Nowak');

const nothingRead: ContactBookInForce = {
    contacts: [],
    reading: false,
    paging: false,
    failure: null,
    complete: true,
    readMore: () => undefined,
    readAgain: () => undefined,
};

const notWriting: Composing = {
    offered: false,
    drafts: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function drawBook({
    book = 'own',
    reading = { ...nothingRead, contacts: [anna, bartek] },
    opened = null,
    selected = [],
    erasable = true,
    onBook = vi.fn(),
    onOpen = vi.fn(),
    onSelected = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    book?: ContactBookName;
    reading?: ContactBookInForce;
    opened?: string | null;
    selected?: readonly string[];
    erasable?: boolean;
    onBook?: (book: ContactBookName) => void;
    onOpen?: (contact: Contact) => void;
    onSelected?: (selected: readonly string[]) => void;
    onAskErasure?: (contacts: readonly Contact[]) => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ComposingContext value={notWriting}>
                <ContactBook
                    book={book}
                    reading={reading}
                    opened={opened}
                    selected={selected}
                    erasable={erasable}
                    onBook={onBook}
                    onOpen={onOpen}
                    onSelected={onSelected}
                    onAskErasure={onAskErasure}
                />
            </ComposingContext>
        </LocalizationProvider>,
    );
}

function rows(): (string | null)[] {
    return within(screen.getByRole('listbox', { name: 'Contacts' }))
        .getAllByRole('option')
        .map((row) => row.textContent);
}

describe('ContactBook', () => {
    it('draws the people the book answered with, and says how many of them there are', () => {
        drawBook();

        expect(rows()).toHaveLength(2);
        expect(screen.getByText('2 people')).toBeDefined();
    });

    // The two books are a choice rather than a filter over one listing, which is what the design draws and what the
    // surface publishes.
    it('offers the two books as one set of choices, with the book in front the chosen one', () => {
        drawBook({ book: 'collected' });

        expect(screen.getByRole('radio', { name: 'Own', checked: false })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Collected', checked: true })).toBeDefined();
    });

    it('asks for the other book when its choice is pressed', () => {
        const onBook = vi.fn();
        drawBook({ onBook });

        fireEvent.click(screen.getByRole('radio', { name: 'Collected' }));

        expect(onBook).toHaveBeenCalledWith('collected');
    });

    it('opens the person a press lands on where nobody is picked out', () => {
        const onOpen = vi.fn();
        drawBook({ onOpen });

        fireEvent.click(screen.getByRole('option', { name: /Anna Kowalska/u }));

        expect(onOpen).toHaveBeenCalledWith(anna);
    });

    // A list with a selection standing is a list being picked over, so a plain press adds to what is held rather than
    // throwing it away by opening somebody.
    it('adds to the selection rather than opening somebody while people are already picked out', () => {
        const onOpen = vi.fn();
        const onSelected = vi.fn();
        drawBook({ selected: ['bartek'], onOpen, onSelected });

        fireEvent.click(screen.getByRole('option', { name: /Anna Kowalska/u }));

        expect(onSelected).toHaveBeenCalledWith(['bartek', 'anna']);
        expect(onOpen).not.toHaveBeenCalled();
    });

    it('walks the rows from the keyboard and opens the one it is on', () => {
        const onOpen = vi.fn();
        drawBook({ onOpen });

        const listbox = screen.getByRole('listbox', { name: 'Contacts' });
        fireEvent.keyDown(listbox, { key: 'ArrowDown' });
        fireEvent.keyDown(listbox, { key: 'Enter' });

        expect(onOpen).toHaveBeenCalledWith(bartek);
    });

    it('says it is waiting, in the place the people will appear', () => {
        drawBook({ reading: { ...nothingRead, reading: true } });

        expect(screen.getByRole('status').textContent).toBe('Reading the address book…');
    });

    it('says why the book did not answer and offers the way out of it', () => {
        const readAgain = vi.fn();
        drawBook({ reading: { ...nothingRead, failure: 'unavailable', readAgain } });

        expect(screen.getByRole('alert').textContent).toContain('The deployment did not answer');

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(readAgain).toHaveBeenCalledTimes(1);
    });

    it('says why each book is empty in that book’s own terms', () => {
        drawBook({ book: 'collected' });

        expect(screen.queryByText('Nobody has been picked up from your mail yet.')).toBeNull();

        drawBook({ book: 'collected', reading: nothingRead });

        expect(screen.getByText('Nobody has been picked up from your mail yet.')).toBeDefined();
    });
});
