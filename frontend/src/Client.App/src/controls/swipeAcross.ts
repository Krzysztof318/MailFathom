// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a swipe across a surface is, in one place, because more than one surface answers to one: a message row is
// carried aside by it and a toast is dismissed by it. The four numbers and the rule between them live here rather than
// beside whichever surface reached for them first — a client where a finger has to travel further on a row than on a
// card has two gesture vocabularies, and nobody can learn the second one.
//
// The numbers are the design project's own, taken from the gesture it draws on a message row, which is the surface
// that has all four of them. Nothing here is a dismissal or an archive: it is arithmetic over two distances — no
// element, no event, no timer — so a surface reads its own pointer, asks this what the travel so far amounts to, and
// decides for itself what that means.

/**
 * How far a pointer travels before the surface starts following it at all.
 *
 * Below it nothing moves and nothing is cancelled, which is what leaves the first fraction of a finger's travel to the
 * gestures a touch may still turn out to be — a tap, or the press that opens a row's menu.
 */
export const swipeEngages = 14;

/** How far a pointer travels across a surface before lifting it asks for what the surface offered. */
export const swipeDistance = 96;

/** How far it may wander up or down on the way. Past this the finger was scrolling and the swipe is off. */
export const swipeDrift = 26;

/** How far the surface itself is carried, however much further the finger goes. */
export const swipeCarriesTo = 148;

/**
 * What a gesture has come to.
 *
 * `travelling` is a finger still on its way and is the answer to most moves; `committed` is the threshold crossed; and
 * `cancelled` is a gesture that turned out to be a scroll, which never becomes a swipe again however far it then
 * travels sideways.
 */
export type Swipe = 'travelling' | 'committed' | 'cancelled';

/**
 * What a pointer that has travelled `across` and `down` from where it landed amounts to.
 *
 * Vertical travel is read first and either direction counts, because a list scrolled with a finger moves as far up as
 * it does down. It cancels only where the vertical travel is also the greater of the two, which is what tells a scroll
 * from a swipe that happens to slope: a finger crossing a row diagonally is still going sideways, and a rule reading
 * the vertical alone would abandon it the moment the hand it belongs to rotated.
 */
export function swipeSoFar(across: number, down: number): Swipe {
    if (Math.abs(down) > swipeDrift && Math.abs(down) > Math.abs(across)) {
        return 'cancelled';
    }

    return Math.abs(across) >= swipeDistance ? 'committed' : 'travelling';
}

/**
 * How far the surface is drawn from where it started, for a pointer that has travelled `across`.
 *
 * Nothing until the gesture has engaged, and never further than the surface is carried, so the row under a finger that
 * has crossed the whole screen is where it was at 148 pixels rather than off the edge of the list.
 */
export function swipeCarry(across: number): number {
    if (Math.abs(across) < swipeEngages) {
        return 0;
    }

    return Math.max(-swipeCarriesTo, Math.min(swipeCarriesTo, across));
}
