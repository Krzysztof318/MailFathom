// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';

// The client's spaces, and the addresses they are reached at. There is no routing package behind this and the
// workspace pins none: a flat set of addresses with no segment, no parameter, and no nested tree is what the fragment
// and `hashchange` already are, and the platform keeps the history for us — a router here would be a dependency, a
// licence review, and a census entry bought for `spaceAt` and `addressOf` below.
//
// The address is a fragment rather than a path for a reason that outlives the size of this file: a path would have to
// be reloadable, and `backend/src/Host/Hosting/ClientApplicationFiles.cs` serves the bundle with no fallback mapping an
// unmatched path onto the entry document — deliberately, because the client surface may share a socket with the MCP
// one. A fragment is never sent to a server at all, so every address below reloads on both heads with nothing
// configured, and the desktop shell's WebView needs no rule of its own either.

// The order is the design project's, and it is the order the rail draws them in at either width. Six of the seven have
// nothing behind them yet and are placeholders rather than screens — the project shows them, so leaving them out would
// make the client a different product from the one that was designed, and drawing them as though they worked would be
// worse. `Space` is what renders them as what they are.
export const spaces = ['discover', 'mail', 'cases', 'agent', 'tasks', 'calendar', 'people'] as const;

export type Space = (typeof spaces)[number];

// A rail down the side of a wide window has room for all seven; the bottom bar of a narrow one has five places, and
// the design project spends them on three spaces, the bell, and an overflow holding everything else. So the bar is a
// partition of the set above rather than the first few of it, and the two halves are declared here beside the order
// they are drawn in — the navigation reads which spaces it has room for rather than counting them.
//
// The overflow's order is the design project's own and is not the order the rail draws them in: what a sheet opened
// on purpose lists is not the same reading as a rail somebody's eye travels down.

/** The spaces the bottom bar carries itself. */
export const barSpaces: readonly Space[] = ['discover', 'mail', 'agent'];

/** The rest, which the bar reaches through its overflow, in the order the overflow lists them. */
export const overflowSpaces: readonly Space[] = ['tasks', 'cases', 'calendar', 'people'];

/** Where the client opens, and where an address naming no space resolves to. */
export const defaultSpace: Space = 'discover';

/** What each space is called on the screen. Exhaustive by its own type, so a new space fails to compile until it has a name. */
export const spaceLabels: Readonly<Record<Space, MessageKey>> = {
    discover: 'space.discover',
    mail: 'space.mail',
    cases: 'space.cases',
    agent: 'space.agent',
    tasks: 'space.tasks',
    calendar: 'space.calendar',
    people: 'space.people',
};

/** Which spaces have something behind them. Everything else is drawn as a placeholder that says so. */
export const implementedSpaces: readonly Space[] = ['mail'];

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
