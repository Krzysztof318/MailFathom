// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import { LocalizationProvider } from '../localization/Localization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { ThemeProvider } from '../theme/Theme';
import { WorkspaceProvider } from '../workspace/Workspace';
import type { MailScope } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
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
    markReadOnOpen: true,
    telemetryEnabled: true,
    expandWholeThread: false,
    notStated: false,
    chooseTheme: () => undefined,
    chooseTabMode: () => undefined,
    chooseTelemetry: () => undefined,
    chooseThreadExpansion: () => undefined,
};

const nobody: OwnProfileInForce = {
    displayName: null,
    changeable: false,
    picture: null,
    nameNotAcceptable: false,
    nameNotStated: false,
    pictureNotStated: false,
    correctName: () => undefined,
    choosePicture: () => undefined,
    removePicture: () => undefined,
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

/** What the deployment answered about forwarding, which this menu only passes to the screen behind its own row. */
const forwardedTo: TelemetryForwarding = { answered: true, destination: 'https://mail.example' };

// What the menu wrote, read back the way every other screen will read it: out of the workspace rather than out of the
// component that wrote it. The scope is the only part of it these tests are about.
function ScopeProbe() {
    const { workspace } = useWorkspace();

    return <output>{JSON.stringify(workspace.scope)}</output>;
}

const scopeStartsAt = 'Scope the client the way the folder tree would.';

// Stands in for the folder tree, which is the other place a scope is written: a test about the two agreeing needs
// something outside the menu to write one, and the tree itself is a whole screen away from this component.
function ScopeAs({ scope }: { readonly scope: MailScope }) {
    const { revise } = useWorkspace();

    return (
        <button
            type="button"
            onClick={() => {
                revise({ scope });
            }}
        >
            {scopeStartsAt}
        </button>
    );
}

function scopeAsTheTreeWould(): void {
    fireEvent.click(screen.getByRole('button', { name: scopeStartsAt }));
}

/** The scope the workspace holds, as the probe wrote it out. */
function scopeInForce(): MailScope {
    return JSON.parse(screen.getByRole('status').textContent) as MailScope;
}

function renderMenu({
    accounts = [],
    deploymentVersion = '0.9.0',
    telemetryForwarding = forwardedTo,
    preferences = settings,
    profile = nobody,
    onSignOut = () => undefined,
    scope = null,
}: {
    readonly accounts?: readonly MailAccount[];
    readonly deploymentVersion?: string | null;
    readonly telemetryForwarding?: TelemetryForwarding;
    readonly preferences?: ClientPreferencesInForce;
    readonly profile?: OwnProfileInForce;
    readonly onSignOut?: () => void;
    readonly scope?: MailScope | null;
} = {}): void {
    render(
        <LocalizationProvider>
            <ThemeProvider>
                <WorkspaceProvider>
                    <AccountMenu
                        accounts={accounts}
                        deploymentVersion={deploymentVersion}
                        telemetryForwarding={telemetryForwarding}
                        preferences={preferences}
                        profile={profile}
                        onSignOut={onSignOut}
                    />
                    <ScopeProbe />
                    {scope === null ? null : <ScopeAs scope={scope} />}
                </WorkspaceProvider>
            </ThemeProvider>
        </LocalizationProvider>,
    );

    if (scope !== null) {
        scopeAsTheTreeWould();
    }
}

// jsdom draws a popover closed and never opens one — it implements neither the invoker nor `showPopover` — so what is
// inside the menu is read as hidden, which is what a browser reports of a closed popover too. The tests below read
// through that rather than pretending it is open, because opening it is the platform's and not this component's.
describe('AccountMenu', () => {
    afterEach(() => {
        atTabWidth(false);
        window.localStorage.clear();

        // The workspace outlives a render in the browser store the provider synchronizes with, so a test that scoped
        // the client would otherwise hand its scope to the next one.
        window.sessionStorage.clear();
    });

    it('is opened by a control named for what it holds, which is the platform’s own popover', () => {
        renderMenu();

        const control = screen.getByRole('button', { name: 'Account and preferences' });

        expect(control.getAttribute('popovertarget')).toBe('account-menu');
        expect(document.getElementById('account-menu')?.getAttribute('popover')).toBe('auto');
    });

    it('holds the two settings a person reaches between messages, the way into the rest, and the way out', () => {
        renderMenu();

        expect(screen.getByRole('switch', { name: /Tab mode/u, hidden: true })).toBeDefined();
        expect(screen.getByRole('group', { name: 'Theme', hidden: true })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Settings', hidden: true })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Sign out', hidden: true })).toBeDefined();
    });

    it('does not choose a language here, that being what the settings screen behind its own row is for', () => {
        renderMenu();

        expect(screen.queryByRole('combobox', { name: 'Language', hidden: true })).toBeNull();
    });

    it('names the person once the deployment has said what they are called', () => {
        renderMenu({ profile: { ...nobody, displayName: 'Ada Lovelace' } });

        expect(screen.getByText('Ada Lovelace')).toBeDefined();
    });

    it('names nobody while nothing has answered, rather than a placeholder somebody would read as a name', () => {
        renderMenu({ profile: nobody });

        expect(screen.getByRole('button', { name: 'Account and preferences' }).textContent).toBe('');
    });

    it('draws the person by their own picture once they have one', () => {
        renderMenu({ profile: { ...nobody, displayName: 'Ada Lovelace', picture: 'data:image/png;base64,AA==' } });

        const drawn = screen.getByRole('button', { name: 'Account and preferences' }).querySelector('img');

        expect(drawn?.getAttribute('src')).toBe('data:image/png;base64,AA==');
    });

    it('opens the settings screen from its own row', () => {
        renderMenu({ profile: { ...nobody, displayName: 'Ada Lovelace' } });

        fireEvent.click(screen.getByRole('button', { name: 'Settings', hidden: true }));

        expect(screen.getByRole('dialog', { name: 'Settings' })).toBeDefined();
    });

    it('hands focus back to the row that opened the screen once it closes', () => {
        renderMenu({ profile: { ...nobody, displayName: 'Ada Lovelace' } });

        const row = screen.getByRole('button', { name: 'Settings', hidden: true });
        fireEvent.click(row);
        fireEvent.click(screen.getByRole('button', { name: 'Close settings' }));

        expect(screen.queryByRole('dialog', { name: 'Settings' })).toBeNull();
        expect(document.activeElement).toBe(row);
    });

    it('lists the mailboxes this deployment reads for the person', () => {
        renderMenu({ accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')] });

        expect(screen.getByRole('list', { name: 'Mailboxes', hidden: true })).toBeDefined();
        expect(screen.getAllByRole('listitem', { hidden: true }).map((row) => row.textContent)).toStrictEqual([
            'Work',
            'Board',
        ]);
    });

    it('leaves the rest of the menu working where no account answered', () => {
        renderMenu({ accounts: [] });

        expect(screen.queryByRole('list', { name: 'Mailboxes', hidden: true })).toBeNull();
        expect(screen.getByRole('button', { name: 'Sign out', hidden: true })).toBeDefined();
    });

    it('draws each mailbox as something to press rather than as a line to read', () => {
        renderMenu({ accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')] });

        expect(screen.getByRole('button', { name: 'Work', hidden: true })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Board', hidden: true })).toBeDefined();
    });

    it('puts the mailbox somebody presses in scope, in the workspace every other screen reads', () => {
        renderMenu({ accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')] });

        fireEvent.click(screen.getByRole('button', { name: 'Board', hidden: true }));

        expect(scopeInForce()).toStrictEqual({ kind: 'account', accountId: 'board' });
    });

    it('marks the mailbox in scope and no other', () => {
        renderMenu({
            accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')],
            scope: { kind: 'account', accountId: 'board' },
        });

        expect(screen.getByRole('button', { name: 'Board', hidden: true }).getAttribute('aria-current')).toBe('true');
        expect(screen.getByRole('button', { name: 'Work', hidden: true }).getAttribute('aria-current')).toBeNull();
    });

    it('marks the mailbox a folder in scope belongs to, which is what the folder tree marks too', () => {
        renderMenu({
            accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')],
            scope: { kind: 'folder', accountId: 'work', alias: 'INBOX' },
        });

        expect(screen.getByRole('button', { name: 'Work', hidden: true }).getAttribute('aria-current')).toBe('true');
        expect(screen.getByRole('button', { name: 'Board', hidden: true }).getAttribute('aria-current')).toBeNull();
    });

    it('marks no mailbox where the scope is every one of them at once, or a role spanning them', () => {
        renderMenu({
            accounts: [mailbox('work', 'Work'), mailbox('board', 'Board')],
            scope: { kind: 'role', role: 'Inbox' },
        });

        expect(screen.getByRole('button', { name: 'Work', hidden: true }).getAttribute('aria-current')).toBeNull();
        expect(screen.getByRole('button', { name: 'Board', hidden: true }).getAttribute('aria-current')).toBeNull();
    });

    it('says nothing here about what is running, that being drawn on the screen behind its own row', () => {
        renderMenu({ deploymentVersion: '0.9.0' });

        expect(screen.queryByText(/deployment 0\.9\.0/u)).toBeNull();
        expect(screen.queryByText(/^MailFathom Client /u)).toBeNull();
    });

    it('offers no way out of the deployment, that being the sign-in screen’s once a session ends', () => {
        renderMenu();

        expect(screen.queryByRole('button', { name: 'Point somewhere else', hidden: true })).toBeNull();
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

    it('draws the tab mode from what is in force and hands a change to what holds it', () => {
        atTabWidth(true);
        const chooseTabMode = vi.fn();
        renderMenu({ preferences: { ...settings, openMailInTabs: true, chooseTabMode } });

        const control = screen.getByRole('switch', { name: /Tab mode/u, hidden: true });

        expect(control).toHaveProperty('checked', true);
        fireEvent.click(control);

        expect(chooseTabMode).toHaveBeenCalledWith(false);
    });

    it('hands a chosen theme to what holds it rather than deciding it here', () => {
        const chooseTheme = vi.fn();
        renderMenu({ preferences: { ...settings, chooseTheme } });

        fireEvent.click(screen.getByRole('radio', { name: 'Dark', hidden: true }));

        expect(chooseTheme).toHaveBeenCalledWith('dark');
    });
});
