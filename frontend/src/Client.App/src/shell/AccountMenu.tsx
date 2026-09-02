// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { LanguageChoice, ThemeChoice } from './Preferences';

// The menu at the foot of the rail, which is where the design project puts everything that is about the person rather
// than about the mail: the two preferences, what the client and the deployment are running, where the client is
// reading from, and the way out. It is the platform's own popover rather than a menu built out of state — it opens
// and closes from the control that names it, closes on Escape and on a press outside it, and hands focus back to that
// control, none of which this file has to write.
//
// The person is not named here. The client holds a finished credential and never the user name it was composed from,
// so the control is drawn as a person rather than as initials the client would have had to invent.

export function AccountMenu({
    deploymentVersion,
    readingFrom,
    onPointSomewhereElse,
    onSignOut,
}: {
    /** What the deployment answered it is running, or `null` while nothing has answered. */
    readonly deploymentVersion: string | null;

    /** The address somebody named for the deployment, or `null` where the origin that served the client is it. */
    readonly readingFrom: string | null;

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
                </div>

                <div className="flex flex-col gap-2 px-3.25 py-2.5">
                    <ThemeChoice />
                    <LanguageChoice />
                </div>

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
