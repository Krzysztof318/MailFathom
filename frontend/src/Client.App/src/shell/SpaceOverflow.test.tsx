// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { Space } from '../routing/spaces';
import { SpaceOverflow } from './SpaceOverflow';

// jsdom draws a closed popover hidden and opens none — it implements neither the invoker nor `showPopover` — so what
// is inside the sheet is read through `hidden: true`, which is what a browser reports of a closed popover too. That is
// the same reading `AccountMenu.test.tsx` takes, and for the same reason: opening one is the platform's rather than
// this component's, and the browser suite is where that is proven.

const handedTheAccount = 'The account control this sheet was handed.';

function renderOverflow(spaces: readonly Space[] = ['tasks', 'cases'], current: Space | null = null): void {
    render(
        <LocalizationProvider>
            <SpaceOverflow spaces={spaces} current={current} account={<button>{handedTheAccount}</button>} />
        </LocalizationProvider>,
    );
}

describe('SpaceOverflow', () => {
    it('is opened by a control named for what it holds, which is the platform’s own popover', () => {
        renderOverflow();

        const control = screen.getByRole('button', { name: 'More' });

        expect(control.getAttribute('popovertarget')).toBe('space-overflow');
        expect(document.getElementById('space-overflow')?.getAttribute('popover')).toBe('auto');
    });

    it('offers each space it was handed as a link, at the address the bar would have used', () => {
        renderOverflow(['tasks', 'cases', 'calendar', 'people']);

        expect(
            screen.getAllByRole('link', { hidden: true }).map((row) => [row.textContent, row.getAttribute('href')]),
        ).toEqual([
            ['Tasks', '#/tasks'],
            ['Cases', '#/cases'],
            ['Calendar', '#/calendar'],
            ['People', '#/people'],
        ]);
    });

    it('says in a placeholder row’s own name that there is nothing behind it yet', () => {
        renderOverflow();

        expect(screen.getByRole('link', { name: 'Tasks — not built yet', hidden: true })).toBeDefined();
    });

    it('marks the row the reader is on, so the sheet says where they are rather than only where they may go', () => {
        renderOverflow(['tasks', 'cases'], 'cases');

        expect(screen.getByRole('link', { current: 'page', hidden: true }).textContent).toBe('Cases');
    });

    it('marks its own control while what is on the screen is behind it, the bar drawing no row for that space', () => {
        renderOverflow(['tasks', 'cases'], 'cases');

        expect(screen.getByRole('button', { name: 'More' }).getAttribute('aria-current')).toBe('true');
    });

    it('leaves its own control unmarked while the reader is on a space the bar draws itself', () => {
        renderOverflow(['tasks', 'cases'], 'mail');

        expect(screen.getByRole('button', { name: 'More' }).getAttribute('aria-current')).toBeNull();
    });

    it('stands the account at the foot of the sheet, after every place there is to go', () => {
        renderOverflow();

        const account = screen.getByRole('button', { name: handedTheAccount, hidden: true });
        const lastRow = screen.getAllByRole('link', { hidden: true }).at(-1);

        expect(document.getElementById('space-overflow')?.contains(account)).toBe(true);
        expect(lastRow?.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });
});
