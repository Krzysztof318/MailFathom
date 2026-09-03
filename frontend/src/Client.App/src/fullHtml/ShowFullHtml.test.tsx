// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ShowFullHtml } from './ShowFullHtml';

// The confirmation is the platform's own modal dialog, and `vitest.setup.ts` stands in for the two methods jsdom does
// not implement: the `open` state, the answer `close()` records, and the event that carries it back to the page — which
// is the whole of what the assertions below read. The focus trap and the restoration afterwards are the two parts
// nothing here models, because jsdom has no top layer to hold or restore focus from. What a test here can hold is that
// both answers leave through `close()`, which is what the platform's own restoration hangs off; the browser suite is
// what has a browser.

function drawing(onShow: () => void): void {
    render(
        <LocalizationProvider>
            <ShowFullHtml onShow={onShow} />
        </LocalizationProvider>,
    );
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

describe('ShowFullHtml', () => {
    it('shows nothing until the reader has been asked, so pressing it opens the question rather than the markup', () => {
        const shown = vi.fn();

        drawing(shown);

        expect(screen.queryByRole('heading', { name: 'Show the full HTML?' })).toBeNull();

        press('Show the full HTML version');

        expect(screen.getByRole('heading', { name: 'Show the full HTML?' })).toBeDefined();
        expect(shown).not.toHaveBeenCalled();
    });

    it('names what a stranger markup can carry and what this client does about it', () => {
        drawing(vi.fn());

        press('Show the full HTML version');

        expect(screen.getByText(/tracking pixels/)).toBeDefined();
        expect(screen.getByText(/Nothing in it can run/)).toBeDefined();
    });

    it('leaves the message exactly as it was when the reader stays with the reduced version', () => {
        const shown = vi.fn();

        drawing(shown);
        press('Show the full HTML version');
        press('Stay with the reduced version');

        expect(shown).not.toHaveBeenCalled();
        expect(screen.queryByRole('heading', { name: 'Show the full HTML?' })).toBeNull();
    });

    it('opens the surface once the reader has answered the question', () => {
        const shown = vi.fn();

        drawing(shown);
        press('Show the full HTML version');
        press('Show the HTML');

        expect(shown).toHaveBeenCalledOnce();
    });

    it('asks again the next time, because neither answer is remembered', () => {
        const shown = vi.fn();

        drawing(shown);
        press('Show the full HTML version');
        press('Stay with the reduced version');
        press('Show the full HTML version');

        expect(screen.getByRole('heading', { name: 'Show the full HTML?' })).toBeDefined();
    });
});
