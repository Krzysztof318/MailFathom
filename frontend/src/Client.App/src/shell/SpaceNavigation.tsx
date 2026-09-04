// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent, ReactNode } from 'react';
import { BrandMark } from '../controls/BrandMark';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { addressOf, barSpaces, implementedSpaces, overflowSpaces, spaceLabels, type Space } from '../routing/spaces';
import { spaceIcons } from './spaceIcons';
import { SpaceOverflow } from './SpaceOverflow';
import { useWideWorkspace } from './useWideWorkspace';

// One list of links, laid out two ways by the width it is given: a rail down the side of a wide window, and bottom
// navigation across a narrow one. Nothing here asks which head it is running on, and nothing is dropped at either
// width — everything the rail offers is reachable from the bar too, which is what makes the two shapes one navigation.
//
// **The two shapes hold a different number of things, and that is the one part CSS cannot lay out.** A rail has room
// for all seven spaces, the bell, and the account down the side of the window. A bar has five places across the foot
// of it, and the design project spends them on three spaces, the bell, and an overflow — so what the bar has no room
// for stands in a sheet behind that fifth item rather than being hidden by width, which is what the width question is
// asked for. Which three the bar carries is `routing/spaces.ts`, beside the order they are drawn in.
//
// Which destinations there are at all is the session's answer rather than this component's: a space this credential
// may not open is absent from both shapes, because offering it would offer an action the deployment is going to refuse.
//
// A space with nothing behind it yet is present and says so. The design project shows seven, so drawing three would
// make this a different product from the one that was designed; drawing all seven as though they worked would be
// worse. What separates them is the name the link carries and the weight it is drawn at, and the screen it opens says
// the same thing in a sentence.
//
// Links rather than buttons, because these navigate: the browser then supplies the keyboard path, the history entry,
// and opening one in a window of its own, none of which a click handler would have.
//
// The account stands at the foot of the rail and at the foot of the overflow sheet, which is where the design project
// puts what is about the person rather than about a space: last in both, and never in the bar itself, where the five
// places are spent. It is offered before the deployment has answered which spaces there are, because signing out and
// pointing the client elsewhere are the way out of a deployment that never answers.

export function SpaceNavigation({
    offered,
    current,
    account,
    notifications,
    onPointerDown,
    onClickCapture,
}: {
    readonly offered: readonly Space[];

    /** The space being shown, or `null` while the deployment has not yet said which spaces there are. */
    readonly current: Space | null;

    /** The control that opens the account menu, which the navigation places rather than draws. */
    readonly account: ReactNode;

    /** The bell, which is the one thing here that is not a place to go and stands beside the account for that reason. */
    readonly notifications: ReactNode;

    /**
     * What an upward swipe anywhere on the bar begins, which is the second way into the notification centre on a
     * phone. It is bound here rather than on the bell because the design project gives the gesture the whole bar.
     */
    readonly onPointerDown: (event: PointerEvent) => void;

    /** What keeps the tap ending such a swipe from also following the link it started on. */
    readonly onClickCapture: (event: { preventDefault: () => void; stopPropagation: () => void }) => void;
}) {
    const { translate } = useLocalization();
    const wide = useWideWorkspace();

    // The rail draws everything it was offered in the order it was offered; the bar draws the three it has room for,
    // and the overflow draws the rest in its own order. Every one of the three is what the session offered filtered
    // against a list in `routing/spaces.ts`, so a space this credential may not open is absent from whichever shape is
    // on the screen without either shape having to be told about it twice.
    const drawn = wide ? offered : offered.filter((space) => barSpaces.includes(space));
    const behindMore = wide ? [] : overflowSpaces.filter((space) => offered.includes(space));

    return (
        <nav
            aria-label={translate('shell.spaces')}
            // The bar is `pan-x` so a finger moving up it is this navigation's rather than the page's, which is what a
            // gesture that has to be followed one to one needs; the rail above the workspace breakpoint has no such
            // gesture and is unaffected by it.
            className="flex shrink-0 touch-pan-x justify-around gap-0.75 border-t border-line bg-rail px-1.5 pt-1.5 pb-2 workspace:order-first workspace:min-h-0 workspace:w-rail workspace:touch-auto workspace:flex-col workspace:justify-start workspace:gap-0.5 workspace:overflow-y-auto workspace:border-t-0 workspace:border-e workspace:px-0 workspace:pt-4 workspace:pb-3 min-h-navigation"
            onPointerDown={onPointerDown}
            onClickCapture={onClickCapture}
        >
            {/* The mark stands at the top of the rail and nowhere in the bottom bar: a narrow window gives the row to
                destinations, and a logo taking one of five places there would cost a reader a space to reach. */}
            <BrandMark label={translate('shell.title')} className="mb-3 hidden size-10 self-center workspace:block" />

            {drawn.map((space) => (
                <SpaceLink key={space} space={space} current={space === current} />
            ))}

            {/* The bell and the account, in that order at both widths: what happened while nobody was looking stands
                beside who is looking. In the rail that is the foot of it, with the bell above the account; in the bar
                the bell is the fourth place and the account has moved behind the fifth, which is what the design
                project draws and what leaves the bar five items wide however many spaces the session offers. */}
            {wide ? (
                <div className="mt-auto flex flex-none flex-col items-center justify-center gap-2 pt-3">
                    {notifications}
                    {account}
                </div>
            ) : (
                <>
                    {notifications}
                    <SpaceOverflow spaces={behindMore} current={current} account={account} />
                </>
            )}
        </nav>
    );
}

function SpaceLink({ space, current }: { readonly space: Space; readonly current: boolean }) {
    const { translate } = useLocalization();
    const built = implementedSpaces.includes(space);
    const name = translate(spaceLabels[space]);

    return (
        <a
            href={addressOf(space)}
            aria-current={current ? 'page' : undefined}
            // A placeholder says what it is in its own name rather than in a note beside it: the name is what a screen
            // reader announces on the link, and it is the one place the sentence is not read on every other item too.
            aria-label={built ? undefined : translate('space.notBuiltYet', { space: name })}
            className={`flex flex-1 flex-col items-center gap-0.75 rounded-2xl px-0.5 py-1.75 text-2xs font-medium transition workspace:flex-none workspace:gap-1 workspace:rounded-none workspace:border-e-3 workspace:py-2.25 workspace:text-sm ${
                current
                    ? 'bg-accent-soft font-semibold text-accent-deep workspace:border-accent'
                    : `workspace:border-transparent hover:bg-hover hover:text-text ${built ? 'text-muted' : 'text-faint'}`
            }`}
        >
            <Icon name={spaceIcons[space]} className="size-6 workspace:size-5.5" />
            <span className="max-w-full truncate">{name}</span>
        </a>
    );
}
