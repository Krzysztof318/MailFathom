// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a swipe across a surface is, in one place, because more than one surface answers to one. A toast is dismissed by
// it, and the message row is meant to be, so the two numbers and the rule between them live here rather than beside
// whichever surface reached for them first: a client where a finger has to travel further on one card than on another
// has two gesture vocabularies, and nobody can learn the second one.
//
// It is arithmetic over two distances and nothing else — no element, no event, no timer — so a surface reads its own
// pointer and asks this what the travel so far amounts to.

/** How far a pointer travels across a surface before it has asked for the surface to go. */
export const swipeDistance = 64;

/** How far it may wander up or down on the way. Past this the finger was scrolling and the swipe is off. */
export const swipeDrift = 32;

/**
 * What a gesture has come to.
 *
 * `travelling` is a finger still on its way and is the answer to most moves; `dismissing` is the threshold crossed;
 * and `cancelled` is a gesture that turned out to be a scroll, which never becomes a dismissal however far it then
 * travels sideways.
 */
export type Swipe = 'travelling' | 'dismissing' | 'cancelled';

/**
 * What a pointer that has travelled `across` and `down` from where it landed amounts to.
 *
 * Vertical travel is read first and either direction counts, because a list scrolled with a finger moves as far up as
 * it does down, and a gesture that has become a scroll must not turn back into a dismissal at the far end of it.
 */
export function swipeSoFar(across: number, down: number): Swipe {
    if (Math.abs(down) > swipeDrift) {
        return 'cancelled';
    }

    return Math.abs(across) >= swipeDistance ? 'dismissing' : 'travelling';
}
