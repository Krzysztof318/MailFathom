// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { initialsOf } from './initials';

// The circle a sender is recognised by, drawn from their initials. The design project draws it on every row of the
// list and at the head of every message card, in two sizes and in one colour: a neutral disc rather than a hue per
// person, because a colour nobody chose says nothing and competes with the accent that marks what is open.
//
// The signed-in person has a circle of their own in `PersonAvatar.tsx`, because they may have put a picture in it and
// a sender never has one here: what the two share is the derivation of the letters, which is `initials.ts`.
//
// The row's circle is drawn at two sizes for the reason the row itself is two heights: the design project enlarges
// both at the phone, where the list is the whole screen, and draws them at one size everywhere above it. The card's is
// one size, because a message's head is the same head at every width.

/** Where the avatar stands: on a row of the list, or at the head of a message drawn as a card. */
export type SenderAvatarPlace = 'row' | 'card';

const places: Readonly<Record<SenderAvatarPlace, string>> = {
    row: 'size-8.5 text-xs workspace:size-5.5 workspace:text-2xs',
    card: 'size-7.5 text-xs',
};

/** The circle a sender is recognised by, drawn only where there are letters to put in it. */
export function SenderAvatar({
    displayName,
    address,
    place = 'card',
}: {
    readonly displayName: string | null;
    readonly address: string | null;
    readonly place?: SenderAvatarPlace;
}) {
    const initials = initialsOf(displayName, address);

    if (initials === null) {
        return null;
    }

    return (
        <span
            aria-hidden="true"
            className={`flex shrink-0 items-center justify-center self-center rounded-full bg-line-strong font-medium tracking-wide text-text-soft ${places[place]}`}
        >
            {initials}
        </span>
    );
}
