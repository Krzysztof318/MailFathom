// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PointerEvent as ReactPointerEvent } from 'react';
import { usePanelSwipe, type PanelSwipe } from './usePanelSwipe';

// What a finger releasing the panel comes to, which is the half of the gesture `panelSwipe.test.ts` cannot state: that
// file holds the arithmetic, and this holds what is done with the answer. The two directions end differently in both
// outcomes — a closing gesture that finished closes the panel and one that did not springs it back, an opening gesture
// that finished leaves it open and one that did not closes it again, the handover having already put it on the screen —
// so all four are asserted rather than the two that happen to share a branch.
//
// jsdom lays nothing out, so the panel measures nothing and the gesture takes the window's height, which is the same
// fallback a real opening gesture takes against a panel that is not in the document yet. Every timestamp is stated,
// because the velocity is a distance over one, and events that all carry the same instant would read as a flick.

const height = window.innerHeight;

function drive(shown: boolean) {
    const opened = vi.fn();
    const closed = vi.fn();
    const { result } = renderHook(() => usePanelSwipe(shown, true, opened, closed));

    return { swipe: () => result.current, opened, closed };
}

function press(swipe: PanelSwipe, closing: boolean, at: number): void {
    const event = { clientY: at, timeStamp: 0, pointerType: 'touch' } as unknown as ReactPointerEvent;

    act(() => {
        if (closing) {
            swipe.onPanelPointerDown(event);
        } else {
            swipe.onNavigationPointerDown(event);
        }
    });
}

function moveTo(at: number, when: number): void {
    const event = new MouseEvent('pointermove', { clientY: at });

    Object.defineProperty(event, 'timeStamp', { value: when });

    act(() => {
        document.dispatchEvent(event);
    });
}

function release(): void {
    act(() => {
        document.dispatchEvent(new MouseEvent('pointerup'));
    });
}

describe('usePanelSwipe', () => {
    it('closes the panel when a downward gesture covers enough of it', () => {
        const { swipe, closed } = drive(true);

        press(swipe(), true, 100);
        moveTo(115, 100);
        moveTo(100 + height, 1000);
        release();

        expect(closed).toHaveBeenCalledOnce();
    });

    it('springs a downward gesture back rather than closing when it covers too little', () => {
        const { swipe, closed } = drive(true);

        press(swipe(), true, 100);
        moveTo(115, 100);
        moveTo(150, 1000);
        release();

        expect(closed).not.toHaveBeenCalled();
        expect(swipe().springing).toBe(true);
    });

    it('leaves the panel open when an upward gesture covers enough of it', () => {
        const { swipe, opened, closed } = drive(false);

        press(swipe(), false, height);
        moveTo(height - 20, 100);
        moveTo(0, 1000);
        release();

        // Opening is said at the handover rather than at the release, so the panel rises from under the finger; what
        // the release settles is only whether it stays.
        expect(opened).toHaveBeenCalledOnce();
        expect(closed).not.toHaveBeenCalled();
    });

    it('closes the panel again when an upward gesture covers too little of it', () => {
        const { swipe, opened, closed } = drive(false);

        press(swipe(), false, height);
        moveTo(height - 20, 100);
        moveTo(height - 40, 1000);
        release();

        expect(opened).toHaveBeenCalledOnce();
        expect(closed).toHaveBeenCalledOnce();
    });

    it('does nothing at all where the composition does not offer the gestures', () => {
        const opened = vi.fn();
        const closed = vi.fn();
        const { result } = renderHook(() => usePanelSwipe(false, false, opened, closed));

        press(result.current, false, height);
        moveTo(height - 200, 100);
        release();

        expect(opened).not.toHaveBeenCalled();
        expect(closed).not.toHaveBeenCalled();
        expect(result.current.offset).toBeNull();
    });
});
