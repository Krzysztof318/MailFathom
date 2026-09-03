// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from './Icon';
import { initialsOf } from './initials';

// The circle the signed-in person is drawn by, in the two places the design project puts it: the control that opens
// the account menu, and the profile section of the settings screen. Three states in one order — the picture they
// chose, the letters of the name this deployment records them under, and the anonymous person while neither has
// answered — because all three are the same circle in the same place and a screen switching between three components
// would be three arrangements to keep in step.
//
// Everything here is hidden from the accessibility tree. In the menu the control around it carries the name, and on
// the settings screen the name is the field beside it: a picture of somebody announced next to their own name is the
// same sentence twice.

/** Where the circle stands: the control that opens the account menu, or the settings screen's profile section. */
export type PersonAvatarPlace = 'menu' | 'profile';

const places: Readonly<Record<PersonAvatarPlace, string>> = {
    menu: 'size-8.5 text-xs',
    profile: 'size-11 text-md',
};

export function PersonAvatar({
    displayName,
    picture,
    place,
}: {
    /** What this deployment records them as called, or `null` while nothing has answered. */
    readonly displayName: string | null;

    /** Where their picture may be drawn from, or `null` where they have none. */
    readonly picture: string | null;

    readonly place: PersonAvatarPlace;
}) {
    const initials = initialsOf(displayName, null);

    return (
        <span
            aria-hidden="true"
            className={`flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-line-strong font-medium tracking-wide text-text-soft ${places[place]}`}
        >
            {picture === null ? (
                (initials ?? <Icon name="person" className="size-5" />)
            ) : (
                // The empty alternative text is the markup saying this picture says nothing, rather than a sentence
                // somebody reads: the circle around it is already hidden, and the name it stands for is beside it.
                <img src={picture} alt={''} className="size-full object-cover" />
            )}
        </span>
    );
}
