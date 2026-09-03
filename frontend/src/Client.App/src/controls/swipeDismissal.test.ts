// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { swipeDistance, swipeDrift, swipeSoFar } from './swipeDismissal';

describe('swipeSoFar', () => {
    it('is still travelling while the finger has not gone far enough', () => {
        expect(swipeSoFar(swipeDistance - 1, 0)).toBe('travelling');
    });

    it('asks for the surface to go once the threshold is reached, in either direction', () => {
        expect(swipeSoFar(swipeDistance, 0)).toBe('dismissing');
        expect(swipeSoFar(-swipeDistance, 0)).toBe('dismissing');
    });

    it('is off once the finger has wandered too far up or down', () => {
        expect(swipeSoFar(swipeDistance, swipeDrift + 1)).toBe('cancelled');
        expect(swipeSoFar(swipeDistance, -(swipeDrift + 1))).toBe('cancelled');
    });

    it('tolerates the drift a straight swipe still carries', () => {
        expect(swipeSoFar(swipeDistance, swipeDrift)).toBe('dismissing');
    });
});
