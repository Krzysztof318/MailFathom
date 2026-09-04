// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, type MouseEvent, type PointerEvent } from 'react';
import type { MenuPoint } from './menuPlacement';

// The two ways a row is asked what it offers: the pointer's own menu gesture, and a finger held on it. They live here
// rather than beside whichever list reached for one first, because the design project answers a press with a menu on
// seven of its lists — and a client where a press has to be held a little longer on tasks than on mail has two gesture
// vocabularies, and nobody can learn the second one.
//
// **Neither opener is bound to a head.** Which of the two runs is decided by the pointer that arrived, so a touch
// screen on a desktop gets the press and a mouse on a tablet gets its own menu gesture. A mouse held down over a row
// opens nothing at all, which is what having a second button is for.
//
// The numbers are the design project's own, and the drift is the one place this differs from the prototype: it cancels
// on any movement whatever, which a finger resting on glass cannot satisfy — a browser reports the jitter as movement.
// So the press survives what a still finger actually does and ends the moment somebody starts scrolling with it.

/** How long a finger stays on a row before the row's menu opens under it. */
export const pressOpensAfter = 420;

/** How long the tap arriving after a press that opened a menu is ignored for, so the press does not also act. */
export const pressSuppressesTapFor = 700;

/** How far a finger may wander while it is being held before the press is off. */
export const pressDrift = 10;

/** How long the platform is asked to vibrate as the menu opens, where it offers that at all. */
export const pressAcknowledgedFor = 12;

/** Whether a pointer that has travelled this far from where it landed has cancelled the press it started. */
export function pressCancelled(across: number, down: number): boolean {
    return Math.hypot(across, down) > pressDrift;
}

/** Whether this pointer is one that has no second button, which is what decides that a press is the way in. */
export function pressedByFinger(pointerType: string): boolean {
    return pointerType === 'touch' || pointerType === 'pen';
}

/** What a row binds to answer a press, beside the question every tap on it has to ask first. */
export interface RowPress {
    readonly onContextMenu: (event: MouseEvent) => void;
    readonly onPointerDown: (event: PointerEvent) => void;
    readonly onPointerMove: (event: PointerEvent) => void;
    readonly onPointerUp: () => void;
    readonly onPointerCancel: () => void;

    /** Whether the tap now arriving is the one that follows a press which has already opened a menu. */
    readonly tapSuppressed: () => boolean;
}

/**
 * The two openers, bound to one row.
 *
 * @param open Opens that row's menu at the point the gesture happened, or `undefined` for a row that offers none — in
 * which case the pointer keeps the browser's own menu and a press does nothing, rather than buzzing for a menu that
 * would not arrive.
 */
export function useRowPress(open: ((at: MenuPoint) => void) | undefined): RowPress {
    // Refs throughout, because none of this is on the screen: a timer running is not something a row draws, and a
    // render per pointer move is the one cost a list of forty thousand rows cannot pay.
    const arming = useRef<number | null>(null);
    const suppressing = useRef<number | null>(null);
    const from = useRef<MenuPoint | null>(null);

    function stopArming(): void {
        if (arming.current !== null) {
            window.clearTimeout(arming.current);
            arming.current = null;
        }

        from.current = null;
    }

    // A row scrolled out of the window while a finger is still on it takes its timers with it, so nothing opens a menu
    // for a row that is no longer in the document.
    useEffect(
        () => () => {
            stopArming();

            if (suppressing.current !== null) {
                window.clearTimeout(suppressing.current);
            }
        },
        [],
    );

    return {
        onContextMenu: (event) => {
            if (open === undefined) {
                return;
            }

            // The browser's own menu is replaced rather than stood beside: two menus over one row would be a reader
            // choosing which of them their next press belongs to.
            event.preventDefault();
            stopArming();
            open({ x: event.clientX, y: event.clientY });
        },

        onPointerDown: (event) => {
            stopArming();

            if (open === undefined || !pressedByFinger(event.pointerType)) {
                return;
            }

            const at = { x: event.clientX, y: event.clientY };

            from.current = at;
            arming.current = window.setTimeout(() => {
                arming.current = null;

                // Said in the hand as well as drawn, which is how the design project answers a press: the menu appears
                // under the finger that is covering the place it appears in.
                if (typeof navigator.vibrate === 'function') {
                    navigator.vibrate(pressAcknowledgedFor);
                }

                if (suppressing.current !== null) {
                    window.clearTimeout(suppressing.current);
                }

                suppressing.current = window.setTimeout(() => {
                    suppressing.current = null;
                }, pressSuppressesTapFor);

                open(at);
            }, pressOpensAfter);
        },

        onPointerMove: (event) => {
            const started = from.current;

            if (started !== null && pressCancelled(event.clientX - started.x, event.clientY - started.y)) {
                stopArming();
            }
        },

        onPointerUp: stopArming,
        onPointerCancel: stopArming,
        tapSuppressed: () => suppressing.current !== null,
    };
}
