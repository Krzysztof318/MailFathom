// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ComposingContext } from '../composer/useComposing';
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
        expect(screen.getByText(/^Pick a message from the list, write a new one, or ask/u)).toBeDefined();
    });

    it('offers no way back where nothing has been read yet', () => {
        renderEmpty(false, null);

        expect(screen.queryByRole('button', { name: 'Open the last message' })).toBeNull();
    });

    it('offers no way to write where the frame composes nothing, and asking about the history either way', () => {
        renderEmpty(false, null);

        expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Ask about the history' })).toBeDefined();
    });

    it('offers to write a message where the frame composes, as the design draws it first', () => {
        const composed = vi.fn();

        render(
            <LocalizationProvider>
                <ComposingContext
                    value={{ offered: true, drafts: false, opening: null, compose: composed, close: () => undefined }}
                >
                    <NothingOpen arriving={false} onReopenLastRead={null} />
                </ComposingContext>
            </LocalizationProvider>,
        );
        fireEvent.click(screen.getByRole('button', { name: 'New message' }));

        expect(composed).toHaveBeenCalledWith({ kind: 'new' });
    });

    it('opens the last message read again from a control that names it', () => {
        const reopen = vi.fn();

        renderEmpty(false, reopen);

        fireEvent.click(screen.getByRole('button', { name: 'Open the last message' }));

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
