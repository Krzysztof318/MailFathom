// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useSignalledChanges } from '../signals/signalledChanges';

// The one control in the navigation that goes nowhere and changes nothing: it asks the client to read again what it is
// already showing. What that is belongs to the screens rather than to this control — the folder tree and its counts,
// the pages of the list, the open message, the accounts, and the notification centre each hear the refresh and read
// again what they draw — so a screen added later joins by listening rather than by growing this control, and *refresh*
// means one thing in the client rather than something different in each corner of it.
//
// **It is the same act the client performs on its own.** A connection to the deployment standing again after a gap,
// and the interval a visible window refreshes on, both ask for exactly this, and the control answers each of them the
// way it answers a press — so a refresh nobody pressed is still one a reader can see happening.
//
// **Nothing it refreshes is emptied.** Each surface reads again under what it draws and keeps it while the answer is
// on the wire, and one the deployment did not answer keeps it afterwards as well: a refresh that failed is one the next
// repeats, never an error on the screen.
//
// It is drawn the two ways the navigation around it is, exactly as the bell beside it: an item with its name under it
// where the navigation is a sheet, and the symbol alone in the rail of a wide window.

/** How long the control says a refresh is under way, which is one beat of the design project's pulse. */
export const refreshShownFor = 1_000;

// The design project's two looks for the control: at rest it is the rail's panel, and under way it is the accent — its
// ground, its line, and its glyph. A pointer over it draws the hover ground and the text colour over either look,
// leaving the line it stands in, which is the order the design draws them in.
const resting =
    'text-muted hover:bg-hover hover:text-text workspace:border-line workspace:bg-panel workspace:text-muted workspace:hover:bg-hover workspace:hover:text-text';
const underWay =
    'bg-accent-soft text-accent-deep hover:bg-hover hover:text-text workspace:border-accent-line workspace:bg-accent-soft workspace:text-accent-deep workspace:hover:bg-hover workspace:hover:text-text';

export function Refresh() {
    const { translate } = useLocalization();
    const changes = useSignalledChanges();
    const [working, setWorking] = useState(false);

    // Heard rather than set on the press, so the control says the same thing whoever asked: a press, a connection
    // standing again, or the interval.
    useEffect(
        () =>
            changes.listen((change) => {
                if (change.kind === 'refresh') {
                    setWorking(true);
                }
            }),
        [changes],
    );

    // ponytail: one beat of the pulse rather than until every surface's read has landed, which no surface reports; tie
    // it to their reads if a refresh ever takes long enough for the difference to show.
    useEffect(() => {
        if (!working) {
            return;
        }

        const settling = window.setTimeout(() => {
            setWorking(false);
        }, refreshShownFor);

        return () => {
            window.clearTimeout(settling);
        };
    }, [working]);

    return (
        <button
            type="button"
            aria-label={translate('shell.refresh')}
            // Refused while one is under way, which is the design project's rule: a second press would read everything
            // again under reads that have not landed yet.
            aria-disabled={working}
            className={`flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-2xl px-0.5 py-1.75 text-2xs font-medium transition workspace:size-8.5 workspace:flex-none workspace:justify-center workspace:gap-0 workspace:rounded-xl workspace:border workspace:px-0 workspace:py-0 ${working ? underWay : resting}`}
            onClick={() => {
                if (!working) {
                    changes.refresh();
                }
            }}
        >
            <Icon name="refresh" className={working ? 'size-5 animate-working' : 'size-5'} />

            {/* The name is under the symbol wherever the navigation carries names, and gone in the rail, where the
                design draws this control as the symbol alone. */}
            <span className="max-w-full truncate workspace:hidden">{translate('shell.refresh')}</span>
        </button>
    );
}
