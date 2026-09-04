// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { commitFraction, commitVelocity, swipeOffset, swipeSettles, swipeTravelled } from './panelSwipe';

// The thresholds are asserted here rather than in a browser, which is the whole reason the arithmetic is a module of
// its own: what a finger does with them is a pointer's, and what they decide is not.

const height = 800;
const still = 0;

describe('swipeSettles', () => {
    it.each([true, false])('finishes a gesture that covered more than the fraction, closing %s', (closing) => {
        expect(swipeSettles(commitFraction + 0.01, still, closing)).toBe(true);
    });

    it.each([true, false])('springs a gesture back that covered less than the fraction, closing %s', (closing) => {
        expect(swipeSettles(commitFraction - 0.01, still, closing)).toBe(false);
    });

    it('finishes a downward flick that went nowhere, because a flick is a decision rather than a distance', () => {
        expect(swipeSettles(0.05, commitVelocity + 0.1, true)).toBe(true);
    });

    it('finishes an upward flick that went nowhere, reading the same threshold the other way', () => {
        expect(swipeSettles(0.05, -(commitVelocity + 0.1), false)).toBe(true);
    });

    it('springs a gesture back that was flicked the other way, however far it had already got', () => {
        expect(swipeSettles(0.9, -(commitVelocity + 0.1), true)).toBe(false);
        expect(swipeSettles(0.9, commitVelocity + 0.1, false)).toBe(false);
    });
});

describe('swipeOffset', () => {
    it('follows a finger pushing the panel away one to one', () => {
        expect(swipeOffset(120, height, true)).toBe(120);
    });

    it('follows a finger pulling the panel up one to one, measured from closed', () => {
        expect(swipeOffset(-120, height, false)).toBe(height - 120);
    });

    it('goes no further than open when a closing gesture is dragged back past where it began', () => {
        expect(swipeOffset(-50, height, true)).toBe(0);
    });

    it('goes no further than closed when an opening gesture is dragged back past where it began', () => {
        expect(swipeOffset(50, height, false)).toBe(height);
    });

    it('goes no further than the panel is tall, however far the finger travelled', () => {
        expect(swipeOffset(height * 3, height, true)).toBe(height);
        expect(swipeOffset(-height * 3, height, false)).toBe(0);
    });
});

describe('swipeTravelled', () => {
    it('reads a closing gesture as how far from open the panel now stands', () => {
        expect(swipeTravelled(height / 4, height, true)).toBe(0.25);
    });

    it('reads an opening gesture as how much of the way up it has come', () => {
        expect(swipeTravelled(height / 4, height, false)).toBe(0.75);
    });

    it('reads a panel with no height as having travelled nowhere rather than dividing by it', () => {
        expect(swipeTravelled(10, 0, true)).toBe(0);
    });
});
