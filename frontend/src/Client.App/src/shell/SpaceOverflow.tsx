// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { addressOf, implementedSpaces, spaceLabels, type Space } from '../routing/spaces';
import { useScreenLayer } from './screenLayers';
import { spaceIcons } from './spaceIcons';
import { useRef, useState, type ReactNode } from 'react';

// The fifth place in the bottom bar, and what stands behind it. A narrow window has room for five items and the design
// project spends them on three spaces, the bell, and this — so what does not fit is reached rather than dropped, which
// is the whole of why this component exists.
//
// **It is the platform's own popover**, for the reasons the account menu beside it is one: it opens and closes from the
// control that names it, a press outside it closes it, Escape closes it, and focus goes back to that control — none of
// which is written here. It is drawn as a sheet against the foot of the screen because that is where the design project
// draws it and because that is where the thumb that opened it already is.
//
// **What it holds is a place to go and the person going there.** The spaces are links, exactly as the bar's own are,
// so the browser keeps the keyboard path and the history entry; the account control is handed in whole rather than
// rebuilt, because what is about the person belongs to one component at both widths and this is only where it stands
// when the rail is not on the screen.

/** The one element the invoker names. There is one navigation on the screen, so there is one of these. */
const sheet = 'space-overflow';

export function SpaceOverflow({
    spaces,
    current,
    account,
}: {
    /** The spaces the bar had no place for, which this offers in the order the design project lists them. */
    readonly spaces: readonly Space[];

    /** The space being shown, so a reader who is already inside the overflow can see which of these they are on. */
    readonly current: Space | null;

    /** The control that opens the account menu, which stands at the foot of this sheet rather than in the bar. */
    readonly account: ReactNode;
}) {
    const { translate } = useLocalization();

    // Marked while what is on the screen is behind it, which is what keeps the bar honest about where the reader is:
    // three of seven spaces are drawn in it, and without this the other four would leave the whole bar unmarked. It is
    // `aria-current="true"` rather than `page`, which is the row inside the sheet — this says *the current one of these
    // five places*, which is what it is.
    const holdsCurrent = current !== null && spaces.includes(current);
    const opened = useRef<HTMLDivElement>(null);
    const [standing, setStanding] = useState(false);

    // The sheet stands over the screen, so the back gesture closes it before it navigates anywhere and taking the bar
    // to another destination leaves none of it behind. Whether it is open is read from the platform rather than held
    // as a second copy of it: the control that names it is what opens and closes it, and the toggle below is the
    // platform saying which of the two just happened.
    useScreenLayer(standing, () => {
        opened.current?.hidePopover();
    });

    return (
        <>
            <button
                type="button"
                popoverTarget={sheet}
                aria-current={holdsCurrent ? 'true' : undefined}
                className={`flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-2xl px-0.5 py-1.75 text-2xs font-medium transition ${
                    holdsCurrent
                        ? 'bg-accent-soft font-semibold text-accent-deep'
                        : 'text-muted hover:bg-hover hover:text-text'
                }`}
            >
                <Icon name="more_horiz" className="size-6" />
                <span className="max-w-full truncate">{translate('shell.more')}</span>
            </button>

            {/* No display utility on the sheet itself, for the reason the account menu's own popover carries none: the
                platform hides a closed popover from its own stylesheet, and a utility here would outrank that. */}
            <div
                ref={opened}
                id={sheet}
                popover="auto"
                onToggle={(event) => {
                    setStanding(event.newState === 'open');
                }}
                aria-label={translate('shell.more')}
                // Dimmed behind, which the design project draws and which is what says the sheet is the whole of what
                // is being answered right now. The platform paints it: a popover has a `::backdrop` of its own exactly
                // as the notification panel's dialog does, and this is the same token that one uses.
                className="inset-x-0 top-auto bottom-0 m-0 w-full max-w-full rounded-t-2xl border-t border-line bg-panel px-3 pt-3 pb-safe-bottom text-base text-text shadow-overlay backdrop:bg-scrim open:block"
            >
                <div className="flex flex-col gap-0.5 pb-3">
                    {/* The bar the design project draws across the top of the sheet, which says a sheet is what this
                        is. It is decoration and nothing is done with it: the gesture that would pull it is the
                        notification centre's, and this sheet is left by pressing outside it or by Escape. */}
                    <span aria-hidden="true" className="mx-auto mb-1.5 h-1 w-9.5 rounded-full bg-line-strong" />

                    {spaces.map((space) => (
                        <OverflowLink key={space} space={space} current={space === current} />
                    ))}

                    <div className="mt-1 border-t border-line-soft pt-2">{account}</div>
                </div>
            </div>
        </>
    );
}

function OverflowLink({ space, current }: { readonly space: Space; readonly current: boolean }) {
    const { translate } = useLocalization();
    const built = implementedSpaces.includes(space);
    const name = translate(spaceLabels[space]);

    return (
        <a
            href={addressOf(space)}
            aria-current={current ? 'page' : undefined}
            // A placeholder says what it is in its own name here too, for the reason the bar's links do: the sentence
            // is read on the one link it is true of rather than beside every other item.
            aria-label={built ? undefined : translate('space.notBuiltYet', { space: name })}
            className={`flex min-h-13 items-center gap-4 rounded-xl px-3.5 transition ${
                current
                    ? 'bg-accent-soft font-semibold text-accent-deep'
                    : `hover:bg-hover ${built ? 'text-text' : 'text-muted'}`
            }`}
        >
            <Icon name={spaceIcons[space]} className="size-5.5" />
            <span className="truncate">{name}</span>
        </a>
    );
}
