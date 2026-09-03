// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { NothingOpen } from './NothingOpen';

function renderEmpty(arriving: boolean, onReopenLastRead: (() => void) | null): void {
    render(
        <LocalizationProvider>
            <NothingOpen arriving={arriving} onReopenLastRead={onReopenLastRead} />
        </LocalizationProvider>,
    );
}

describe('NothingOpen', () => {
    it('says nothing is open and what would fill it, rather than drawing an empty column', () => {
        renderEmpty(false, null);

        expect(screen.getByText('Nothing is open')).toBeDefined();
        expect(screen.getByText('Pick a message from the list and it opens as a tab of its own.')).toBeDefined();
    });

    it('offers no way back where nothing has been read yet', () => {
        renderEmpty(false, null);

        expect(screen.queryByRole('button', { name: 'Open the last message read' })).toBeNull();
    });

    it('opens the last message read again from a control that names it', () => {
        const reopen = vi.fn();

        renderEmpty(false, reopen);

        fireEvent.click(screen.getByRole('button', { name: 'Open the last message read' }));

        expect(reopen).toHaveBeenCalledTimes(1);
    });

    it('takes focus where closing the last tab is what put it on the screen', () => {
        renderEmpty(true, null);

        expect(document.activeElement).toBe(screen.getByText('Nothing is open').closest('div[tabindex="-1"]'));
    });

    it('moves nothing where it is what the space opened with, a landing being no navigation', () => {
        renderEmpty(false, null);

        expect(document.activeElement).toBe(document.body);
    });
});
