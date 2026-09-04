// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The arithmetic of the two gestures the notification panel is driven by on a phone, apart from the pointers that
// produce it. Every number here is the design project's own and is stated once, because the two directions are one
// gesture read twice rather than two behaviours: an upward swipe on the bottom navigation pulls the panel up, a
// downward swipe anywhere on the open panel pushes it away, and both follow the finger with no smoothing, no inertia,
// and no delay.
//
// It is a module of its own so the thresholds can be asserted without a browser. What a pointer does with them is
// `usePanelSwipe.ts`.

/**
 * How far a finger travels upward on the bottom navigation before the bar gives the gesture up.
 *
 * Larger than the row's own slop below, because the bar has a tap target on it and a mistake there costs a reader a
 * move to another screen rather than a menu that did not open.
 */
export const barSlop = 12;

/** How far a finger travels before a press being held on a row is off, which is the gesture the panel takes from. */
export const rowSlop = 10;

/** How much of the panel's height a gesture covers before releasing it finishes rather than springs back. */
export const commitFraction = 0.32;

/** How fast a short flick finishes the gesture on its own, in CSS pixels per millisecond. */
export const commitVelocity = 0.5;

/** How long the panel takes to return when neither the distance nor the speed was reached. */
export const springMilliseconds = 260;

/**
 * Whether releasing here finishes the gesture rather than springing back.
 *
 * @param travelled How much of the panel's height the gesture has covered, from where the panel took the gesture over
 * rather than from the edge of the screen, as a fraction.
 * @param velocity How fast the finger was moving over the last stretch, downward positive, in pixels per millisecond.
 * @param closing Whether the gesture is the one that pushes the panel away rather than the one that pulls it up.
 * @returns Whether the panel finishes where the gesture was taking it.
 */
export function swipeSettles(travelled: number, velocity: number, closing: boolean): boolean {
    // The speed in the direction the gesture is going, so both directions are read against one pair of thresholds.
    const towards = closing ? velocity : -velocity;

    // A flick the other way always wins over the distance covered: somebody who has changed their mind mid-gesture has
    // said so with the last thing they did rather than with where they had got to.
    if (towards < -commitVelocity) {
        return false;
    }

    return travelled > commitFraction || towards > commitVelocity;
}

/**
 * How far the panel stands from open while a gesture is being held.
 *
 * @param moved How far the finger has moved since the panel took the gesture over, downward positive.
 * @param height How tall the panel is.
 * @param closing Whether the gesture is the one that pushes the panel away.
 * @returns The distance from open, in pixels, never past either end of the travel.
 */
export function swipeOffset(moved: number, height: number, closing: boolean): number {
    const from = closing ? moved : height - Math.max(0, -moved);

    return Math.max(0, Math.min(height, from));
}

/** How much of the travel a panel standing this far from open has covered, in the gesture's own direction. */
export function swipeTravelled(offset: number, height: number, closing: boolean): number {
    if (height <= 0) {
        return 0;
    }

    return closing ? offset / height : 1 - offset / height;
}
