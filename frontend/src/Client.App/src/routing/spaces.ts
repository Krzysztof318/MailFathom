// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';

// The client's three spaces, and the addresses they are reached at. There is no routing package behind this and the
// workspace pins none: three addresses with no segment, no parameter, and no nested tree is what the fragment and
// `hashchange` already are, and the platform keeps the history for us — a router here would be a dependency, a licence
// review, and a census entry bought for `spaceAt` and `addressOf` below.
//
// The address is a fragment rather than a path for a reason that outlives the size of this file: a path would have to
// be reloadable, and `backend/src/Host/Hosting/ClientApplicationFiles.cs` serves the bundle with no fallback mapping an
// unmatched path onto the entry document — deliberately, because the client surface may share a socket with the MCP
// one. A fragment is never sent to a server at all, so every address below reloads on both heads with nothing
// configured, and the desktop shell's WebView needs no rule of its own either.

export const spaces = ['discover', 'mail', 'cases'] as const;

export type Space = (typeof spaces)[number];

/** Where the client opens, and where an address naming no space resolves to. */
export const defaultSpace: Space = 'discover';

/** What each space is called on the screen. Exhaustive by its own type, so a fourth space fails to compile until it has a name. */
export const spaceLabels: Readonly<Record<Space, MessageKey>> = {
    discover: 'space.discover',
    mail: 'space.mail',
    cases: 'space.cases',
};

export function isSpace(value: unknown): value is Space {
    return typeof value === 'string' && (spaces as readonly string[]).includes(value);
}

/** The address a space is reached at, in the form an `href` takes. */
export function addressOf(space: Space): string {
    return `#/${space}`;
}

/** The space an address names, or `null` where it names none — a first load at the root among them. */
export function spaceAt(address: string): Space | null {
    const named = address.replace(/^#?\/?/, '');

    return isSpace(named) ? named : null;
}
