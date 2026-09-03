// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useRef, useState } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { MailboxMark } from '../controls/MailboxMark';
import { PersonAvatar } from '../controls/PersonAvatar';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { Settings } from '../settings/Settings';
import { TabModeSwitch, ThemeSegments } from './Preferences';

// The menu at the foot of the rail, which is where the design project puts everything that is about the person rather
// than about the mail: who they are, which mailboxes this deployment reads for them, the two settings that follow them
// between machines, what the client and the deployment are running, where the client is reading from, the way into
// everything else about them, and the way out. It is the platform's own popover rather than a menu built out of state —
// it opens and closes from the control that names it, closes on Escape and on a press outside it, and hands focus back
// to that control, none of which this file has to write.
//
// The language is not here and is on the settings screen, which is where the design project draws it: the menu holds
// what somebody reaches for between messages, and what language the client reads in is set once.

export function AccountMenu({
    accounts,
    deploymentVersion,
    readingFrom,
    telemetryDestination,
    preferences,
    profile,
    onPointSomewhereElse,
    onSignOut,
}: {
    /** The mailboxes this deployment reads for the signed-in person, which is empty while nothing has answered. */
    readonly accounts: readonly MailAccount[];

    /** What the deployment answered it is running, or `null` while nothing has answered. */
    readonly deploymentVersion: string | null;

    /** The address somebody named for the deployment, or `null` where the origin that served the client is it. */
    readonly readingFrom: string | null;

    /**
     * Where this client's own telemetry is sent, or `null` where this deployment forwards none and there is therefore
     * nothing behind the switch. It is the deployment's address whether or not somebody typed it, because what the
     * settings screen has to name is where the records actually go rather than how the client came to be pointed there.
     */
    readonly telemetryDestination: string | null;

    /** The settings that follow the person, and the three ways of changing one. */
    readonly preferences: ClientPreferencesInForce;

    /** Who the client is drawing, which this menu shows and the screen behind its own row edits. */
    readonly profile: OwnProfileInForce;

    readonly onPointSomewhereElse: () => void;
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

                    <p className="font-mono text-xs text-muted">
                        {deploymentVersion === null
                            ? translate('shell.clientVersion', { client: __MAILFATHOM_VERSION__ })
                            : translate('shell.versions', {
                                  client: __MAILFATHOM_VERSION__,
                                  deployment: deploymentVersion,
                              })}
                    </p>

                    {readingFrom === null ? null : (
                        <p className="flex flex-wrap items-baseline gap-x-2 text-xs text-muted">
                            <span className="break-all">
                                {translate('deployment.reachedAt', { address: readingFrom })}
                            </span>
                            <button
                                type="button"
                                className="text-accent-strong hover:underline"
                                onClick={onPointSomewhereElse}
                            >
                                {translate('deployment.change')}
                            </button>
                        </p>
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
                dialog inside something the platform sets `display: none` on is a dialog nobody can see. */}
            <Settings
                open={settingsOpen}
                profile={profile}
                preferences={preferences}
                telemetryDestination={telemetryDestination}
                onClose={closeSettings}
            />
        </>
    );
}

// Which mailboxes this deployment reads for the person, which is what makes the menu about them rather than about the
// client. The mark is the one the folder tree draws in front of the same mailbox's folders, so a mailbox keeps one
// colour wherever it appears; the ordinal is shifted by one because that tree's first mark stands for every mailbox at
// once and this list has no such row.
//
// Nothing is drawn where nothing answered. An empty list is a deployment that has not answered yet, one whose accounts
// this credential may not read, and one that declares no mailbox — three sentences the connection summary already says
// in the space a person is looking at, and repeating any of them here would be saying it twice.
function Mailboxes({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { translate } = useLocalization();

    // The words over the list name the list rather than heading a section: the menu stands inside a space whose own
    // heading level is the space's, and a heading opened here would be one out of order in a popover.
    const named = useId();

    if (accounts.length === 0) {
        return null;
    }

    return (
        <>
            <p id={named} className="pt-1.5 text-2xs tracking-widest text-faint uppercase">
                {translate('shell.mailboxes')}
            </p>

            <ul aria-labelledby={named} className="flex flex-col">
                {accounts.map((account, ordinal) => (
                    <li key={account.id} className="flex items-center gap-2 py-0.75 text-muted">
                        <MailboxMark ordinal={ordinal + 1} className="size-1.5" />
                        <span className="min-w-0 truncate text-xs">{account.displayName}</span>
                    </li>
                ))}
            </ul>
        </>
    );
}
