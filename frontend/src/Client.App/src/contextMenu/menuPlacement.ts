// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where a menu opened at a gesture stands, which is arithmetic over two rectangles and nothing else — no element, no
// event, and no question about which composition is on the screen. It sits apart from the menu that reads it because
// the one thing that can go wrong here is invisible in a diff: a menu opened near the foot of a window is drawn off the
// edge of it, and the acts nobody can reach are the ones furthest down the list.

/** How close to the edge of the space it stands in a menu may be drawn. The design project's own margin. */
export const menuEdge = 10;

/** A point in the space a menu is placed in, measured from that space's own start corner. */
export interface MenuPoint {
    readonly x: number;
    readonly y: number;
}

export interface MenuSize {
    readonly width: number;
    readonly height: number;
}

/**
 * Where a menu's own corner goes so that a menu asked for at `at` stays inside `within`.
 *
 * Each axis is answered on its own, because the two failures are different: a menu opened near the end of a line runs
 * off the side, and one opened near the foot of the window runs off the bottom, and a gesture in the far corner does
 * both. A menu larger than the space it has is drawn from the near edge rather than pushed off the far one — what is
 * past the end of it is then reached by scrolling, which is the only answer that leaves every item reachable.
 */
export function placedWithin(at: MenuPoint, menu: MenuSize, within: MenuSize): MenuPoint {
    return {
        x: along(at.x, menu.width, within.width),
        y: along(at.y, menu.height, within.height),
    };
}

function along(at: number, menu: number, within: number): number {
    return Math.max(menuEdge, Math.min(at, within - menu - menuEdge));
}
