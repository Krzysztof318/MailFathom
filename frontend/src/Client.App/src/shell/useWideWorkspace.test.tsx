// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useCoarsePointer } from './useWideWorkspace';

// What the pointer can do is the half of a composition a stylesheet cannot answer, because it decides whether a
// listener is bound at all. It is stated here the way `theme/Theme.test.tsx` states a machine's appearance: a query
// this suite defines over the one the setup file leaves answering nothing.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

let listeners: (() => void)[] = [];
let pointerIsCoarse = false;

function theDeviceIsPickedUp(coarse: boolean): void {
    pointerIsCoarse = coarse;
    act(() => {
        for (const listener of listeners) {
            listener();
        }
    });
}

beforeEach(() => {
    listeners = [];
    pointerIsCoarse = false;

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: pointerIsCoarse && query.includes('coarse'),
            addEventListener: (_: string, listener: () => void) => {
                listeners.push(listener);
            },
            removeEventListener: (_: string, listener: () => void) => {
                listeners = listeners.filter((listening) => listening !== listener);
            },
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('useCoarsePointer', () => {
    it('reads a pointer a mouse drives as fine, which is what leaves a gesture unbound', () => {
        const { result } = renderHook(() => useCoarsePointer());

        expect(result.current).toBe(false);
    });

    it('reads a pointer a finger drives as coarse', () => {
        pointerIsCoarse = true;

        const { result } = renderHook(() => useCoarsePointer());

        expect(result.current).toBe(true);
    });

    it('keeps the answer current as a device is picked up, rather than answering once at the first render', () => {
        const { result } = renderHook(() => useCoarsePointer());

        theDeviceIsPickedUp(true);

        expect(result.current).toBe(true);
    });
});
