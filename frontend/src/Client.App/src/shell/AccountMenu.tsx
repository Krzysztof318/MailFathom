// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useRef, useState } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { MailboxMark } from '../controls/MailboxMark';
import { PersonAvatar } from '../controls/PersonAvatar';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { Settings } from '../settings/Settings';
import { accountInScope, scopeOfAccount } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { TabModeSwitch, ThemeSegments } from './Preferences';

// The menu at the foot of the rail, which is where the design project puts everything that is about the person rather
// than about the mail: who they are, which mailboxes this deployment reads for them and which of those the client is
// scoped to, the two settings that follow them between machines, the way into everything else about them, and the way
// out. It is the platform's own popover rather than a menu built out of state — it opens and closes from the control
// that names it, closes on Escape and on a press outside it, and hands focus back to that control, none of which this
// file has to write.
//
// Two things a reader might expect here are deliberately elsewhere, both because the design project draws them there.
// The language is on the settings screen: the menu holds what somebody reaches for between messages, and what language
// the client reads in is set once. What the client and the deployment are running is on the sign-in screen and at the
// foot of that same settings screen, which is where somebody looks for a version — this menu only carries the
// deployment's version through to the screen its own row opens.
//
// The way out of a deployment somebody named themselves is not here either, and that one is not a design decision:
// pointing the client elsewhere ends the session anyway, so the sign-in screen the frame falls back to is where it is
// offered and where it still works when a kept password has stopped being accepted.

export function AccountMenu({
    accounts,
    deploymentVersion,
    telemetryForwarding,
    preferences,
    profile,
    onSignOut,
}: {
    /** The mailboxes this deployment reads for the signed-in person, which is empty while nothing has answered. */
    readonly accounts: readonly MailAccount[];

    /** What the deployment answered it is running, or `null` while nothing has answered, for the screen below. */
    readonly deploymentVersion: string | null;

    /**
     * What this deployment has said about forwarding this client's own records, which the settings screen behind this
     * menu's own row words. A forwarded one carries the deployment's address whether or not somebody typed it, because
     * what that screen has to name is where the records actually go rather than how the client came to be pointed
     * there.
     */
    readonly telemetryForwarding: TelemetryForwarding;

    /** The settings that follow the person, and the four ways of changing one. */
    readonly preferences: ClientPreferencesInForce;

    /** Who the client is drawing, which this menu shows and the screen behind its own row edits. */
    readonly profile: OwnProfileInForce;

    readonly onSignOut: () => void;
}) {
    const { translate } = useLocalization();
    const [settingsOpen, setSettingsOpen] = useState(false);
    const menu = useRef<HTMLDivElement>(null);
    const settingsRow = useRef<HTMLButtonElement>(null);

    // The menu is folded away while the screen behind it is open, which is what the design project draws, and put back
    // when it closes so that focus returns to the row it was opened from rather than to the rail. The platform's own
    // two methods rather than a state of ours: the popover is the platform's, and a second opinion about whether it is
    // open is the pair that comes to disagree.
    function openSettings(): void {
        menu.current?.hidePopover();
        setSettingsOpen(true);
    }

    function closeSettings(): void {
        setSettingsOpen(false);
        menu.current?.showPopover();
        settingsRow.current?.focus();
    }

    return (
        <>
            <button
                type="button"
                popoverTarget="account-menu"
                aria-label={translate('shell.account')}
                className="flex size-8.5 shrink-0 items-center justify-center rounded-full text-text-soft shadow-raised transition hover:-translate-y-px hover:shadow-overlay"
            >
                <PersonAvatar displayName={profile.displayName} picture={profile.picture} place="menu" />
            </button>

            {/* No display utility on the popover itself: the platform hides a closed popover with `display: none` from
                its own stylesheet, and a utility on the element would outrank that and draw the menu open forever. */}
            <div
                ref={menu}
                id="account-menu"
                popover="auto"
                aria-label={translate('shell.accountMenu')}
                className="inset-x-4 top-auto bottom-22 m-0 overflow-hidden rounded-2xl border border-line bg-panel p-0 text-base text-text shadow-overlay workspace:inset-x-auto workspace:bottom-4.5 workspace:left-26.5 workspace:w-54"
            >
                <div className="flex flex-col gap-0.5 border-b border-line-soft px-3.25 py-2.75">
                    {profile.displayName === null ? null : (
                        <p className="truncate font-semibold">{profile.displayName}</p>
                    )}

                    <Mailboxes accounts={accounts} />
                </div>

                <TabModeSwitch on={preferences.openMailInTabs} onChange={preferences.chooseTabMode} />

                {/* One rule under the theme block and none above it, which is where the design project draws the
                    menu's second divider: the tab row and the theme row read as one group of settings. */}
                <div className="border-b border-line-soft">
                    <ThemeSegments onChoose={preferences.chooseTheme} />
                </div>

                <button
                    ref={settingsRow}
                    type="button"
                    className="flex w-full items-center gap-2.5 px-3.25 py-2.5 text-start transition hover:bg-hover"
                    onClick={openSettings}
                >
                    <Icon name="settings" className="size-4.75" />
                    {translate('settings.title')}
                </button>

                {preferences.notStated ? (
                    <p className="border-t border-line-soft px-3.25 py-2 text-xs text-warning-text">
                        {translate('preferences.notStated')}
                    </p>
                ) : null}

                <button
                    type="button"
                    className="flex w-full items-center gap-2.5 border-t border-line-soft px-3.25 py-2.5 text-start text-muted transition hover:bg-hover hover:text-text"
                    onClick={onSignOut}
                >
                    <Icon name="logout" className="size-4.75" />
                    {translate('shell.signOut')}
                </button>
            </div>

            {/* Outside the popover rather than inside it, because the menu is folded away while this is open and a
                dialog inside something the platform sets `display: none` on is a dialog nobody can see. Rendered only
                while it is open, which is what keeps the tab it was last left on out of anything that outlives it. */}
            {settingsOpen ? (
                <Settings
                    profile={profile}
                    preferences={preferences}
                    telemetryForwarding={telemetryForwarding}
                    deploymentVersion={deploymentVersion}
                    onClose={closeSettings}
                />
            ) : null}
        </>
    );
}

// Which mailboxes this deployment reads for the person, and which of them the client is scoped to — the second place
// that is chosen, the folder tree being the first. It writes the same `MailScope` into the same workspace that tree
// writes, so the two are one decision drawn twice rather than two notions of which mailbox is in scope; anything that
// held its own would be the pair that comes to disagree.
//
// The mark is the one the folder tree draws in front of the same mailbox's folders, so a mailbox keeps one colour
// wherever it appears; the ordinal is shifted by one because that tree's first mark stands for every mailbox at once
// and this list has no such row.
//
// The check follows the account a scope belongs to rather than the scope itself, which is what makes it right for the
// folder scopes too: somebody reading one folder of their work mailbox is in that mailbox, and the row saying so is
// the same row they would press to widen the scope back out to all of it. Every mailbox at once, and a role spanning
// them, belong to no account and are checked nowhere — which is the tree's own answer as well.
//
// Nothing is drawn where nothing answered. An empty list is a deployment that has not answered yet, one whose accounts
// this credential may not read, and one that declares no mailbox — three sentences the connection summary already says
// in the space a person is looking at, and repeating any of them here would be saying it twice.
function Mailboxes({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();

    // The words over the list name the list rather than heading a section: the menu stands inside a space whose own
    // heading level is the space's, and a heading opened here would be one out of order in a popover.
    const named = useId();

    if (accounts.length === 0) {
        return null;
    }

    const inScope = accountInScope(workspace.scope);

    return (
        <>
            <p id={named} className="pt-1.5 text-2xs tracking-widest text-faint uppercase">
                {translate('shell.mailboxes')}
            </p>

            <ul aria-labelledby={named} className="flex flex-col">
                {accounts.map((account, ordinal) => (
                    <li key={account.id} className="flex">
                        {/* The row is a button rather than a list item somebody may click: what it does is an act, so
                            it takes focus, answers Enter and Space, and is announced as something to press without
                            any of that being written here. `aria-current` is what says which one is in scope — the
                            check beside it is that same fact drawn, for a reader who is looking. */}
                        <button
                            type="button"
                            aria-current={account.id === inScope ? 'true' : undefined}
                            className="-mx-1 flex min-w-0 flex-1 items-center gap-2 rounded-md px-1 py-0.75 text-start text-muted transition hover:bg-hover hover:text-text"
                            onClick={() => {
                                revise({ scope: scopeOfAccount(account.id) });
                            }}
                        >
                            <MailboxMark ordinal={ordinal + 1} className="size-1.5" />
                            <span className="min-w-0 flex-1 truncate text-xs">{account.displayName}</span>

                            {account.id === inScope ? (
                                <Icon name="check" className="size-3.5 shrink-0 text-accent-strong" />
                            ) : null}
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}
