// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The letters somebody is recognised by where there is no picture of them. Two circles draw them — the one a sender is
// recognised by on every row of the list, and the one the signed-in person is drawn by in the account menu and on the
// settings screen — so the rule for deriving them is stated once rather than twice with a drift between them.
//
// It sits beside the two components rather than in either, because a module a component is named after takes a name of
// its own: `Initials.tsx` beside `initials.ts` is one name to a filesystem that ignores case.

/**
 * The letters somebody is recognised by, or nothing where neither a name nor an address offers one.
 *
 * @param displayName What they are called, where anything calls them anything.
 * @param address Their address, which stands in where there is no name — the part in front of the host, being what a
 * person is recognised by rather than the machine.
 */
export function initialsOf(displayName: string | null, address: string | null): string | null {
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
