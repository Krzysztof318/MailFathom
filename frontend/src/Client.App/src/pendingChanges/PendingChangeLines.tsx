// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import { actNames, standingReasons, waitingCounts } from './changeWording';
import { usePendingChanges, type UndecidedChange } from './usePendingChanges';

// What this client has asked its mailbox for and has not yet been told the end of, said where a person already looks
// to find out how current what they are reading is. It stands above the freshness summary rather than inside its
// disclosure, because a change waiting on a decision is the one thing on that line nobody should have to open a panel
// to discover.
//
// Nothing here draws a surface of its own for the outcome: a change that failed says so through the toast the frame
// already raises, and this is where the same change is found again once that toast has gone. Which is the whole reason
// both exist — a toast is how somebody hears, and this is how they answer.

export function PendingChangeLines() {
    const { locale, translate } = useLocalization();
    const { waiting, undecided, stoppedFollowing, settle, followAgain } = usePendingChanges();

    if (waiting.length === 0 && undecided.length === 0) {
        return null;
    }

    return (
        <div className="flex flex-col gap-2">
            {waiting.length === 0 ? null : (
                <p className="text-sm text-muted" role="status">
                    {translate(waitingCounts[new Intl.PluralRules(locale).select(waiting.length)], {
                        count: new Intl.NumberFormat(locale).format(waiting.length),
                    })}
                </p>
            )}

            {stoppedFollowing && waiting.length > 0 ? (
                <div className="flex flex-col gap-1.5 rounded-lg bg-sunken px-3 py-2 text-sm">
                    <p className="text-muted">{translate('pendingChange.stoppedFollowing')}</p>

                    <div className="flex flex-wrap gap-2 pt-0.5">
                        <SecondaryButton label={translate('pendingChange.followAgain')} onActivate={followAgain} />
                    </div>
                </div>
            ) : null}

            {undecided.length === 0 ? null : (
                <ul className="flex flex-col gap-2">
                    {undecided.map((change) => (
                        <li key={change.recordId}>
                            <UndecidedLine
                                change={change}
                                onAskAgain={() => {
                                    settle(change.recordId, 'askAgain');
                                }}
                                onLetGo={() => {
                                    settle(change.recordId, 'letGo');
                                }}
                            />
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}

// Both sides and both ways out, which is the whole of what makes this a decision rather than a report: what was asked
// for, what the mailbox did about it, and two acts neither of which the client takes on somebody's behalf.
function UndecidedLine({
    change,
    onAskAgain,
    onLetGo,
}: {
    readonly change: UndecidedChange;
    readonly onAskAgain: () => void;
    readonly onLetGo: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-col gap-1.5 rounded-lg bg-sunken px-3 py-2 text-sm">
            <p className="font-medium">{translate(actNames[change.act])}</p>
            <p className="text-muted">{translate(standingReasons[change.standing])}</p>

            <div className="flex flex-wrap gap-2 pt-0.5">
                <SecondaryButton label={translate('pendingChange.askAgain')} onActivate={onAskAgain} />
                <SecondaryButton label={translate('pendingChange.letGo')} onActivate={onLetGo} />
            </div>
        </div>
    );
}
