// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ThemeProvider } from '../theme/Theme';
import { AccountMenu } from './AccountMenu';

function renderMenu({
    deploymentVersion = '0.9.0',
    readingFrom = null,
    onPointSomewhereElse = () => undefined,
    onSignOut = () => undefined,
}: {
    readonly deploymentVersion?: string | null;
    readonly readingFrom?: string | null;
    readonly onPointSomewhereElse?: () => void;
    readonly onSignOut?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ThemeProvider>
                <AccountMenu
                    deploymentVersion={deploymentVersion}
                    readingFrom={readingFrom}
                    onPointSomewhereElse={onPointSomewhereElse}
                    onSignOut={onSignOut}
                />
            </ThemeProvider>
        </LocalizationProvider>,
    );
}

// jsdom draws a popover closed and never opens one — it implements neither the invoker nor `showPopover` — so what is
// inside the menu is read as hidden, which is what a browser reports of a closed popover too. The tests below read
// through that rather than pretending it is open, because opening it is the platform's and not this component's.
describe('AccountMenu', () => {
    it('is opened by a control named for what it holds, which is the platform’s own popover', () => {
        renderMenu();

        const control = screen.getByRole('button', { name: 'Account and preferences' });

        expect(control.getAttribute('popovertarget')).toBe('account-menu');
        expect(document.getElementById('account-menu')?.getAttribute('popover')).toBe('auto');
    });

    it('holds the two preferences and the way out', () => {
        renderMenu();

        expect(screen.getByRole('combobox', { name: 'Theme', hidden: true })).toBeDefined();
        expect(screen.getByRole('combobox', { name: 'Language', hidden: true })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Sign out', hidden: true })).toBeDefined();
    });

    it('says what the client and the deployment are running, beside each other', () => {
        renderMenu({ deploymentVersion: '0.9.0' });

        expect(screen.getByText(/deployment 0\.9\.0/u)).toBeDefined();
    });

    it('says what the client alone is running while the deployment has not answered', () => {
        renderMenu({ deploymentVersion: null });

        expect(screen.queryByText(/deployment/u)).toBeNull();
        expect(screen.getByText(/^Client /u)).toBeDefined();
    });

    it('offers to be pointed elsewhere only where somebody named the deployment themselves', () => {
        renderMenu({ readingFrom: null });

        expect(screen.queryByRole('button', { name: 'Point somewhere else', hidden: true })).toBeNull();
    });

    it('names the deployment it is reading from, and hands pointing elsewhere to the frame', () => {
        const onPointSomewhereElse = vi.fn();
        renderMenu({ readingFrom: 'https://mail.example.invalid', onPointSomewhereElse });

        expect(screen.getByText(/https:\/\/mail\.example\.invalid/u)).toBeDefined();
        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));

        expect(onPointSomewhereElse).toHaveBeenCalledOnce();
    });

    it('hands signing out to the frame rather than doing anything itself', () => {
        const onSignOut = vi.fn();
        renderMenu({ onSignOut });

        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));

        expect(onSignOut).toHaveBeenCalledOnce();
    });
});
