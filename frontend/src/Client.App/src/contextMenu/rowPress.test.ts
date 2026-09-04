// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MouseEvent, PointerEvent } from 'react';
import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pressCancelled, pressDrift, pressOpensAfter, pressSuppressesTapFor, useRowPress } from './rowPress';

// The press is a timer and nothing else, so the clock is the one thing these tests have to own. It is released after
// every one of them: a fake clock left installed changes the next file the worker runs.
afterEach(() => {
    vi.useRealTimers();
});

function finger(at: { readonly x: number; readonly y: number }): PointerEvent {
    return { pointerType: 'touch', clientX: at.x, clientY: at.y } as unknown as PointerEvent;
}

function mouse(at: { readonly x: number; readonly y: number }): PointerEvent {
    return { pointerType: 'mouse', clientX: at.x, clientY: at.y } as unknown as PointerEvent;
}

function held(): { readonly opened: ReturnType<typeof vi.fn>; readonly press: () => ReturnType<typeof useRowPress> } {
    vi.useFakeTimers();

    const opened = vi.fn();
    const rendered = renderHook(() => useRowPress(opened));

    return { opened, press: () => rendered.result.current };
}

describe('pressCancelled', () => {
    it('reads a finger that has barely moved as one that is still being held', () => {
        expect(pressCancelled(pressDrift - 1, 0)).toBe(false);
    });

    it('reads a finger that has travelled past the drift as one that has started scrolling', () => {
        expect(pressCancelled(0, pressDrift + 1)).toBe(true);
    });

    it('reads travel in both directions together, so a diagonal is not a way to stay under the drift', () => {
        expect(pressCancelled(pressDrift, pressDrift)).toBe(true);
    });
});

describe('useRowPress', () => {
    it('opens the menu where the finger landed, once it has been held for as long as the design asks', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(opened).toHaveBeenCalledWith({ x: 40, y: 120 });
    });

    it('opens nothing while the press is still short of that', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter - 1);
        });

        expect(opened).not.toHaveBeenCalled();
    });

    it('opens nothing for a mouse held down over the row, which has a button of its own for this', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(mouse({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter * 4);
        });

        expect(opened).not.toHaveBeenCalled();
    });

    it('opens the menu at the pointer on the pointer’s own menu gesture, in place of the browser’s', () => {
        const { opened, press } = held();
        const prevented = vi.fn();

        act(() => {
            press().onContextMenu({ clientX: 15, clientY: 25, preventDefault: prevented } as unknown as MouseEvent);
        });

        expect(opened).toHaveBeenCalledWith({ x: 15, y: 25 });
        expect(prevented).toHaveBeenCalled();
    });

    it('ends the press the moment the finger starts travelling', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            press().onPointerMove(finger({ x: 40, y: 120 + pressDrift + 1 }));
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(opened).not.toHaveBeenCalled();
    });

    it('holds the press through the jitter a finger resting on glass reports', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            press().onPointerMove(finger({ x: 41, y: 121 }));
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(opened).toHaveBeenCalledOnce();
    });

    it('ends the press when the finger is lifted before it is armed', () => {
        const { opened, press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            press().onPointerUp();
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(opened).not.toHaveBeenCalled();
    });

    it('suppresses the tap that follows a press which has already opened the menu', () => {
        const { press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter);
        });

        expect(press().tapSuppressed()).toBe(true);
    });

    it('stops suppressing once the design’s own window has passed', () => {
        const { press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter);
            vi.advanceTimersByTime(pressSuppressesTapFor);
        });

        expect(press().tapSuppressed()).toBe(false);
    });

    it('suppresses nothing where no press was held, so an ordinary tap acts', () => {
        const { press } = held();

        act(() => {
            press().onPointerDown(finger({ x: 40, y: 120 }));
            vi.advanceTimersByTime(pressOpensAfter - 1);
            press().onPointerUp();
        });

        expect(press().tapSuppressed()).toBe(false);
    });

    it('opens nothing at all for a row that offers no menu', () => {
        vi.useFakeTimers();

        const rendered = renderHook(() => useRowPress(undefined));
        const prevented = vi.fn();

        act(() => {
            rendered.result.current.onPointerDown(finger({ x: 40, y: 120 }));
            rendered.result.current.onContextMenu({ preventDefault: prevented } as unknown as MouseEvent);
            vi.advanceTimersByTime(pressOpensAfter);
        });

        // The browser's own menu is left alone, which is what a list with nothing of its own to offer should show.
        expect(prevented).not.toHaveBeenCalled();
        expect(rendered.result.current.tapSuppressed()).toBe(false);
    });
});
