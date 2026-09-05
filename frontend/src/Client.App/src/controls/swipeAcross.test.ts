// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { swipeCarriesTo, swipeCarry, swipeDistance, swipeDrift, swipeEngages, swipeSoFar } from './swipeAcross';

describe('swipeSoFar', () => {
    it('is still travelling while the finger has not gone far enough', () => {
        expect(swipeSoFar(swipeDistance - 1, 0)).toBe('travelling');
    });

    it('asks for what the surface offered once the threshold is reached, in either direction', () => {
        expect(swipeSoFar(swipeDistance, 0)).toBe('committed');
        expect(swipeSoFar(-swipeDistance, 0)).toBe('committed');
    });

    it('is off once the finger has wandered further up or down than it has gone sideways', () => {
        expect(swipeSoFar(swipeDrift, swipeDrift + 1)).toBe('cancelled');
        expect(swipeSoFar(swipeDrift, -(swipeDrift + 1))).toBe('cancelled');
    });

    it('tolerates the drift a straight swipe still carries', () => {
        expect(swipeSoFar(swipeDistance, swipeDrift)).toBe('committed');
    });

    // A finger crossing a row diagonally is going sideways, and the vertical alone would abandon it.
    it('keeps a swipe that slopes, the sideways travel being the greater of the two', () => {
        expect(swipeSoFar(swipeDrift + 2, swipeDrift + 1)).toBe('travelling');
    });
});

describe('swipeCarry', () => {
    it('leaves the surface where it was until the gesture has engaged', () => {
        expect(swipeCarry(swipeEngages - 1)).toBe(0);
        expect(swipeCarry(-(swipeEngages - 1))).toBe(0);
    });

    it('follows the finger once it has', () => {
        expect(swipeCarry(swipeEngages)).toBe(swipeEngages);
        expect(swipeCarry(-swipeEngages)).toBe(-swipeEngages);
    });

    it('stops carrying the surface past where the design draws it, in either direction', () => {
        expect(swipeCarry(swipeCarriesTo + 200)).toBe(swipeCarriesTo);
        expect(swipeCarry(-(swipeCarriesTo + 200))).toBe(-swipeCarriesTo);
    });
});
