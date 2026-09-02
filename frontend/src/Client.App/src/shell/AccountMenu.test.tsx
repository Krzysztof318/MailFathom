// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import { ThemeProvider } from '../theme/Theme';
import { AccountMenu } from './AccountMenu';

function mailbox(id: string, displayName: string): MailAccount {
    return {
        id,
        displayName,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
        behind: false,
    };
}

const settings: ClientPreferencesInForce = {
    openMailInTabs: false,
    notStated: false,
    chooseTheme: () => undefined,
    chooseTabMode: () => undefined,
};

// The width the tab mode needs is the one thing on this menu jsdom answers for, and it answers `false` to every query
// unless a test says otherwise — so a test about the wide case states the width it is about rather than inheriting one.
function atTabWidth(wideEnough: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: wideEnough,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function renderMenu({
    accounts = [],
    deploymentVersion = '0.9.0',
    readingFrom = null,
    preferences = settings,
    onPointSomewhereElse = () => undefined,
    onSignOut = () => undefined,
}: {
    readonly accounts?: readonly MailAccount[];
    readonly deploymentVersion?: string | null;
    readonly readingFrom?: string | null;
    readonly preferences?: ClientPreferencesInForce;
    readonly onPointSomewhereElse?: () => void;
    readonly onSignOut?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ThemeProvider>
                <AccountMenu
                    accounts={accounts}
                    deploymentVersion={deploymentVersion}
                    readingFrom={readingFrom}
                    preferences={preferences}
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
    afterEach(() => {
        window.localStorage.clear();
    });

    it('is opened by a control named for what it holds, which is the platform’s own popover', () => {
        renderMenu();

        const control = screen.getByRole('button', { name: 'Account and preferences' });

        expect(control.getAttribute('popovertarget')).toBe('account-menu');
        expect(document.getElementById('account-menu')?.getAttribute('popover')).toBe('auto');
    });

    it('holds the three settings and the way out', () => {
        renderMenu();

        expect(screen.getByRole('switch', { name: /Tab mode/u, hidden: true })).toBeDefined();
        expect(screen.getByRole('group', { name: 'Theme', hidden: true })).toBeDefined();
        expect(screen.getByRole('combobox', { name: 'Language', hidden: true })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Sign out', hidden: true })).toBeDefined();
    });

    it('lists the mailboxes this deployment reads for the person', () => {
        renderMenu({ accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')] });

        expect(screen.getByRole('heading', { name: 'Mailboxes', hidden: true })).toBeDefined();
        expect(screen.getAllByRole('listitem', { hidden: true }).map((row) => row.textContent)).toStrictEqual([
            'Work',
            'Board',
        ]);
    });

    it('leaves the rest of the menu working where no account answered', () => {
        renderMenu({ accounts: [] });

        expect(screen.queryByRole('heading', { name: 'Mailboxes', hidden: true })).toBeNull();
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

    it('says when a change did not reach the deployment', () => {
        renderMenu({ preferences: { ...settings, notStated: true } });

        expect(screen.getByText(/was not saved to the deployment/u)).toBeDefined();
    });
});

describe('AccountMenu’s tab-mode switch', () => {
    afterEach(() => {
        atTabWidth(false);
    });

    it('is off unless the person turned it on', () => {
        renderMenu();

        expect(screen.getByRole('switch', { name: /Tab mode/u, hidden: true })).toHaveProperty('checked', false);
    });

    it('is on where they did', () => {
        renderMenu({ preferences: { ...settings, openMailInTabs: true } });

        expect(screen.getByRole('switch', { name: /Tab mode/u, hidden: true })).toHaveProperty('checked', true);
    });

    it('is inert below the width the tab mode needs, and says so in its own line', () => {
        atTabWidth(false);
        renderMenu();

        const control = screen.getByRole('switch', { name: /Tab mode/u, hidden: true });

        expect(control).toHaveProperty('disabled', true);
        expect(screen.getByText('available on a wider screen')).toBeDefined();
    });

    it('says so in Polish too', () => {
        atTabWidth(false);
        window.localStorage.setItem('mailfathom.locale', 'pl');
        renderMenu();

        expect(screen.getByText('dostępne na szerszym ekranie')).toBeDefined();
        window.localStorage.clear();
    });

    it('is operable at the width the tab mode needs, and reports the change', () => {
        atTabWidth(true);
        const chooseTabMode = vi.fn();
        renderMenu({ preferences: { ...settings, chooseTabMode } });

        const control = screen.getByRole('switch', { name: /Tab mode/u, hidden: true });

        expect(control).toHaveProperty('disabled', false);
        expect(screen.queryByText('available on a wider screen')).toBeNull();
        fireEvent.click(control);

        expect(chooseTabMode).toHaveBeenCalledWith(true);
    });
});

describe('AccountMenu’s theme segments', () => {
    it('offers the three choices as one group', () => {
        renderMenu();

        expect(
            screen.getAllByRole('radio', { hidden: true }).map((segment) => segment.getAttribute('value')),
        ).toStrictEqual(['system', 'light', 'dark']);
    });

    it('marks the one in force, which is what the device opened in', () => {
        renderMenu();

        expect(screen.getByRole('radio', { name: 'Auto', hidden: true })).toHaveProperty('checked', true);
        expect(screen.getByRole('radio', { name: 'Dark', hidden: true })).toHaveProperty('checked', false);
    });

    it('reports a chosen segment rather than deciding anything itself', () => {
        const chooseTheme = vi.fn();
        renderMenu({ preferences: { ...settings, chooseTheme } });

        fireEvent.click(screen.getByRole('radio', { name: 'Dark', hidden: true }));

        expect(chooseTheme).toHaveBeenCalledWith('dark');
    });
});
