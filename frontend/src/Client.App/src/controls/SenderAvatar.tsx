// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The circle a sender is recognised by, drawn from their initials. The design project draws it on every row of the
// list and at the head of every message card, in two sizes and in one colour: a neutral disc rather than a hue per
// person, because a colour nobody chose says nothing and competes with the accent that marks what is open.

/** The letters a sender is recognised by, or nothing where neither a name nor an address offers one. */
function initialsOf(displayName: string | null, address: string | null): string | null {
    const named = words(displayName);
    const first = named.at(0);
    const last = named.at(-1);

    if (first !== undefined && last !== undefined) {
        return named.length > 1 ? `${leading(first)}${leading(last)}` : leading(first);
    }

    const only = words(localPart(address)).at(0);

    return only === undefined ? null : leading(only);
}

/** What a sender called themselves, which is the part of an address a person is recognised by rather than the host. */
function localPart(address: string | null): string | null {
    const at = address?.indexOf('@') ?? -1;

    return address !== null && at > 0 ? address.slice(0, at) : address;
}

function words(text: string | null): readonly string[] {
    return (text ?? '').split(/[\s._-]+/u).filter((word) => /\p{L}|\p{N}/u.test(word));
}

function leading(word: string): string {
    return (Array.from(word)[0] ?? '').toUpperCase();
}

/** Where the avatar stands: on a row of the list, or at the head of a message drawn as a card. */
export type SenderAvatarPlace = 'row' | 'card';

const places: Readonly<Record<SenderAvatarPlace, string>> = {
    row: 'size-5.5 text-2xs',
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
