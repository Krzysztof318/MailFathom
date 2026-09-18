// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Contact } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ContactRow } from './ContactRow';

const anna: Contact = {
    id: 'anna',
    displayName: 'Anna Kowalska',
    addresses: ['anna@contoso.example'],
    preferredAddress: 'anna@contoso.example',
    note: null,
    origin: 'Asserted',
    recordedAt: '2026-08-31T09:41:00+00:00',
    amendedAt: '2026-08-31T09:41:00+00:00',
};

function drawRow({
    selected = false,
    open = false,
    onOpen = vi.fn(),
    onToggle = vi.fn(),
}: {
    selected?: boolean;
    open?: boolean;
    onOpen?: () => void;
    onToggle?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ul>
                <ContactRow
                    contact={anna}
                    selected={selected}
                    open={open}
                    focusable={true}
                    position={4}
                    onOpen={onOpen}
                    onToggle={onToggle}
                    onPress={undefined}
                    onPoint={vi.fn()}
                    onElement={vi.fn()}
                />
            </ul>
        </LocalizationProvider>,
    );
}

describe('ContactRow', () => {
    it('draws the record and nothing derived: the name the book holds and the address to write to', () => {
        drawRow();

        expect(screen.getByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('anna@contoso.example')).toBeDefined();
    });

    // The book is keyset-paged, so nothing knows how many people it holds and the option says so rather than claiming
    // the number of rows read so far.
    it('says where the person sits in the book and that how long the book is is unknown', () => {
        drawRow();

        const row = screen.getByRole('option');

        expect(row.getAttribute('aria-posinset')).toBe('4');
        expect(row.getAttribute('aria-setsize')).toBe('-1');
    });

    it('opens the person on a plain press', () => {
        const onOpen = vi.fn();
        const onToggle = vi.fn();
        drawRow({ onOpen, onToggle });

        fireEvent.click(screen.getByRole('option'));

        expect(onOpen).toHaveBeenCalledTimes(1);
        expect(onToggle).not.toHaveBeenCalled();
    });

    it('picks the person out rather than opening them where the press was modifier-held', () => {
        const onOpen = vi.fn();
        const onToggle = vi.fn();
        drawRow({ onOpen, onToggle });

        fireEvent.click(screen.getByRole('option'), { ctrlKey: true });

        expect(onToggle).toHaveBeenCalledTimes(1);
        expect(onOpen).not.toHaveBeenCalled();
    });

    it('reports being picked out and being the person open as two separate things', () => {
        drawRow({ selected: true, open: true });

        const row = screen.getByRole('option');

        expect(row.getAttribute('aria-selected')).toBe('true');
        expect(row.getAttribute('aria-current')).toBe('true');
    });
});
