// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useBackNavigation } from './backNavigation';

// The session history is the one thing outside React this hook synchronizes with, and jsdom's own is a real stack this
// suite cannot rewind without waiting on it — `history.back()` there is asynchronous and delivers nothing a test can
// await. So the three calls that matter are recorded and the entry showing is stated directly, which is what a press of
// the back gesture actually leaves behind: an earlier entry, with whatever it was marked with.
//
// Nothing here mentions a head. The gesture arrives as `popstate` in the browser, in the desktop shell's WebView, and
// on Android — where the head's own activity hands it to the page rather than finishing the application — so what is
// proven below is proven for all three at once.

const declaredState = Object.getOwnPropertyDescriptor(window.history, 'state');

let pushed: unknown[] = [];
let replaced: unknown[] = [];
let travelled: number[] = [];
let showing: unknown = null;

function theEntryShowing(state: unknown): void {
    showing = state;
}

function theGestureIsUsed(): void {
    act(() => {
        window.dispatchEvent(new PopStateEvent('popstate'));
    });
}

beforeEach(() => {
    pushed = [];
    replaced = [];
    travelled = [];
    showing = null;

    Object.defineProperty(window.history, 'state', {
        configurable: true,
        get: () => showing,
    });

    vi.spyOn(window.history, 'pushState').mockImplementation((state: unknown) => {
        pushed.push(state);
        showing = state;
    });

    vi.spyOn(window.history, 'replaceState').mockImplementation((state: unknown) => {
        replaced.push(state);
        showing = state;
    });

    vi.spyOn(window.history, 'go').mockImplementation((delta?: number) => {
        travelled.push(delta ?? 0);
    });
});

afterEach(() => {
    if (declaredState === undefined) {
        Reflect.deleteProperty(window.history, 'state');
    } else {
        Object.defineProperty(window.history, 'state', declaredState);
    }

    vi.restoreAllMocks();
});

describe('useBackNavigation', () => {
    it('leaves the history alone while nothing stands over the screen', () => {
        renderHook(() => {
            useBackNavigation(0, () => undefined);
        });

        expect(pushed).toEqual([]);
    });

    it('adds one entry for the gesture to consume as something opens over the screen', () => {
        const { rerender } = renderHook(
            ({ steps }) => {
                useBackNavigation(steps, () => undefined);
            },
            { initialProps: { steps: 0 } },
        );

        rerender({ steps: 1 });

        expect(pushed).toHaveLength(1);
    });

    it('adds one entry per step, so two surfaces take two presses to unwind', () => {
        const { rerender } = renderHook(
            ({ steps }) => {
                useBackNavigation(steps, () => undefined);
            },
            { initialProps: { steps: 0 } },
        );

        rerender({ steps: 2 });

        expect(pushed).toHaveLength(2);
    });

    it('unwinds one step for the entry the gesture consumed', () => {
        const unwound = vi.fn();

        renderHook(() => {
            useBackNavigation(2, unwound);
        });

        theEntryShowing({ 'mailfathom.back': 1 });
        theGestureIsUsed();

        expect(unwound).toHaveBeenCalledExactlyOnceWith(1);
    });

    // In one call rather than in two: what the second of them would take away depends on the first having happened,
    // and the screen it would be asked against is the screen as it stood before either.
    it('unwinds everything standing over the screen where the gesture landed past all of it', () => {
        const unwound = vi.fn();

        renderHook(() => {
            useBackNavigation(2, unwound);
        });

        theEntryShowing(null);
        theGestureIsUsed();

        expect(unwound).toHaveBeenCalledExactlyOnceWith(2);
    });

    it('unwinds nothing where the gesture left as many entries as there are steps', () => {
        const unwound = vi.fn();

        renderHook(() => {
            useBackNavigation(1, unwound);
        });

        theGestureIsUsed();

        expect(unwound).not.toHaveBeenCalled();
    });

    it('gives the spare entry back where a surface was closed by its own control instead', () => {
        const { rerender } = renderHook(
            ({ steps }) => {
                useBackNavigation(steps, () => undefined);
            },
            { initialProps: { steps: 0 } },
        );

        rerender({ steps: 1 });
        rerender({ steps: 0 });

        expect(travelled).toEqual([-1]);
    });

    // A reload is the one time the entry showing describes a screen that no longer exists: the marks were written by
    // the client that was thrown away, and the one that came back has nothing standing over it. Giving those entries
    // up would walk the reader backwards out of the client on a reload, so the entry is re-marked instead.
    it('re-marks the entry a reload arrived on rather than giving its entries back', () => {
        theEntryShowing({ 'mailfathom.back': 3 });

        renderHook(() => {
            useBackNavigation(0, () => undefined);
        });

        expect(travelled).toEqual([]);
        expect(replaced).toEqual([{ 'mailfathom.back': 0 }]);
    });
});
