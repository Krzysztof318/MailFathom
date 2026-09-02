// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { BrandMark } from '../controls/BrandMark';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useLocalization } from '../localization/useLocalization';
import { addressOf, implementedSpaces, spaceLabels, type Space } from '../routing/spaces';

// One list of links, laid out two ways by the width it is given: a rail down the side of a wide window, and bottom
// navigation across a narrow one. Nothing here asks which head it is running on, and nothing disappears at either
// width — the same destinations are present in both, which is what makes the two shapes one navigation.
//
// Which destinations those are is the session's answer rather than this component's: a space this credential may not
// open is absent from both shapes, because offering it would offer an action the deployment is going to refuse.
//
// A space with nothing behind it yet is present and says so. The design project shows seven, so drawing three would
// make this a different product from the one that was designed; drawing all seven as though they worked would be
// worse. What separates them is the name the link carries and the weight it is drawn at, and the screen it opens says
// the same thing in a sentence.
//
// Links rather than buttons, because these navigate: the browser then supplies the keyboard path, the history entry,
// and opening one in a window of its own, none of which a click handler would have.
//
// The account stands at the foot of the rail and at the end of the bar, which is where the design project puts what is
// about the person rather than about a space: reachable at both widths, and last in both. It is there before the
// deployment has answered which spaces there are, because signing out and pointing the client elsewhere are the way
// out of a deployment that never answers.

const spaceIcons: Readonly<Record<Space, IconName>> = {
    discover: 'explore',
    mail: 'mail',
    cases: 'topic',
    agent: 'auto_awesome',
    tasks: 'task_alt',
    calendar: 'calendar_month',
    people: 'group',
};

export function SpaceNavigation({
    offered,
    current,
    account,
}: {
    readonly offered: readonly Space[];

    /** The space being shown, or `null` while the deployment has not yet said which spaces there are. */
    readonly current: Space | null;

    /** The control that opens the account menu, which the navigation places rather than draws. */
    readonly account: ReactNode;
}) {
    const { translate } = useLocalization();

    return (
        <nav
            aria-label={translate('shell.spaces')}
            className="flex shrink-0 justify-around gap-0.75 border-t border-line bg-rail px-1.5 pt-1.5 pb-2 workspace:order-first workspace:w-rail workspace:flex-col workspace:justify-start workspace:gap-0.5 workspace:overflow-y-auto workspace:border-t-0 workspace:border-e workspace:px-0 workspace:pt-4 workspace:pb-3"
        >
            {/* The mark stands at the top of the rail and nowhere in the bottom bar: a narrow window gives the row to
                destinations, and a logo taking one of seven places there would cost a reader a space to reach. */}
            <BrandMark label={translate('shell.title')} className="mb-3 hidden size-10 self-center workspace:block" />

            {offered.map((space) => (
                <SpaceLink key={space} space={space} current={space === current} />
            ))}

            <div className="flex flex-1 items-center justify-center workspace:mt-auto workspace:flex-none workspace:pt-3">
                {account}
            </div>
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
