// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { MailboxMark } from '../controls/MailboxMark';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import { LanguageChoice, TabModeSwitch, ThemeSegments } from './Preferences';

// The menu at the foot of the rail, which is where the design project puts everything that is about the person rather
// than about the mail: which mailboxes this deployment reads for them, the two settings that follow them between
// machines, the one that stays on the machine, what the client and the deployment are running, where the client is
// reading from, and the way out. It is the platform's own popover rather than a menu built out of state — it opens
// and closes from the control that names it, closes on Escape and on a press outside it, and hands focus back to that
// control, none of which this file has to write.
//
// The person is not named here, and neither is the way into Settings. The client holds a finished credential and never
// the user name it was composed from, so the control is drawn as a person rather than as initials the client would have
// had to invent; both arrive with the profile routes and the Settings screen.

export function AccountMenu({
    accounts,
    deploymentVersion,
    readingFrom,
    preferences,
    onPointSomewhereElse,
    onSignOut,
}: {
    /** The mailboxes this deployment reads for the signed-in person, which is empty while nothing has answered. */
    readonly accounts: readonly MailAccount[];

    /** What the deployment answered it is running, or `null` while nothing has answered. */
    readonly deploymentVersion: string | null;

    /** The address somebody named for the deployment, or `null` where the origin that served the client is it. */
    readonly readingFrom: string | null;

    /** The settings that follow the person, and the two ways of changing one. */
    readonly preferences: ClientPreferencesInForce;

    readonly onPointSomewhereElse: () => void;
    readonly onSignOut: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <>
            <button
                type="button"
                popoverTarget="account-menu"
                aria-label={translate('shell.account')}
                className="flex size-8.5 shrink-0 items-center justify-center rounded-full bg-line-strong text-text-soft shadow-raised transition hover:-translate-y-px hover:shadow-overlay"
            >
                <Icon name="person" className="size-5" />
            </button>

            {/* No display utility on the popover itself: the platform hides a closed popover with `display: none` from
                its own stylesheet, and a utility on the element would outrank that and draw the menu open forever. */}
            <div
                id="account-menu"
                popover="auto"
                aria-label={translate('shell.accountMenu')}
                className="inset-x-4 top-auto bottom-22 m-0 overflow-hidden rounded-2xl border border-line bg-panel p-0 text-base text-text shadow-overlay workspace:inset-x-auto workspace:bottom-4.5 workspace:left-26.5 workspace:w-54"
            >
                <div className="flex flex-col gap-0.5 border-b border-line-soft px-3.25 py-2.75">
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

                <div className="flex items-center justify-between gap-2 px-3.25 py-2.5">
                    <span className="text-base">{translate('shell.language')}</span>
                    <LanguageChoice />
                </div>

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

    if (accounts.length === 0) {
        return null;
    }

    return (
        <>
            <h2 className="pt-1.5 text-2xs tracking-widest text-faint uppercase">{translate('shell.mailboxes')}</h2>

            <ul className="flex flex-col">
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
