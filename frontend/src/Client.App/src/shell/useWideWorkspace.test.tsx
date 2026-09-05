// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useCoarsePointer, useDesktopComposition, useTwoPanes, useWideWorkspace } from './useWideWorkspace';

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

// The three widths themselves, at the four sizes the design project frames a composition at. jsdom lays nothing out
// and declares no custom property, so each query is answered from the width alone and the hooks fall back to the
// stylesheet's own numbers — which is exactly what is being asserted, those numbers being the boundaries.
function atWidth(pixels: number): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/.exec(query)?.[1];

            return {
                media: query,
                matches: named !== undefined && pixels >= Number(named) * 16,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });
}

describe('the widths a composition changes at', () => {
    it.each([
        ['the phone', 390, false, false, false],
        ['the fold', 884, true, true, false],
        ['the tablet', 1024, true, true, false],
        ['the desktop', 1440, true, true, true],
    ])('composes %s as the design project frames it', (_, pixels, workspace, panes, desktop) => {
        atWidth(pixels);

        const composition = renderHook(() => ({
            workspace: useWideWorkspace(),
            panes: useTwoPanes(),
            desktop: useDesktopComposition(),
        }));

        expect(composition.result.current).toEqual({ workspace, panes, desktop });
    });
});
