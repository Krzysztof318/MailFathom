// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { reconnectionDelay } from './useConnection';

// The waiting the hook does between attempts, as a function of its arguments rather than of a clock or of a draw it
// made itself — which is what lets it be stated here without a fake timer and without stubbing randomness.

describe('reconnectionDelay', () => {
    it('waits longer after each attempt that did not answer', () => {
        const waits = [0, 1, 2, 3].map((made) => reconnectionDelay(made, 0.5));

        expect(waits).toEqual([...waits].sort((first, second) => first - second));
        expect(new Set(waits).size).toBe(waits.length);
    });

    it('stops lengthening the wait, so a deployment that is down is not left an hour behind', () => {
        expect(reconnectionDelay(20, 0.5)).toBe(reconnectionDelay(30, 0.5));
    });

    it('spreads the wait around the nominal one, so clients that lost one deployment do not return in step', () => {
        const nominal = reconnectionDelay(0, 0.5);

        expect(reconnectionDelay(0, 0)).toBeLessThan(nominal);
        expect(reconnectionDelay(0, 0.999)).toBeGreaterThan(nominal);
    });

    it.each([0, 0.25, 0.5, 0.75, 0.999])('waits a positive time whatever is drawn, here %s', (drawn) => {
        expect(reconnectionDelay(0, drawn)).toBeGreaterThan(0);
    });
});
