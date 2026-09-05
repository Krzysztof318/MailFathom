// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type PointerEvent } from 'react';
import { swipeCarry, swipeSoFar } from '../controls/swipeAcross';
import { pressedByFinger, type RowPress } from '../contextMenu/rowPress';

// The other thing a finger on a message row can mean: carried aside, left to answer the message and right to file it
// away. The arithmetic is `controls/swipeAcross.ts`'s, because a toast answers to the same numbers; what is here is the
// part that belongs to a row — which of the two acts each direction is, and how the gesture shares one finger with the
// press that opens the row's menu.
//
// **One touch, two gestures, and they never both fire.** They begin together and each rules the other out as it takes
// over: the press is already off by the time the swipe engages, because it cancels on a shorter travel than the swipe
// needs; and a press that has opened a menu suppresses everything behind it, which is the same window the tap answers
// to. So the finger is only ever doing one of the three things a row offers.
//
// **It is bound to the pointer rather than to a head**, exactly as the press is: a finger or a pen swipes, a mouse
// never does. Dragging a row with a mouse would file mail on a slipped button, and every act either direction performs
// is on the row's own menu and in the toolbar for a pointer that has one.

/** What lifting the finger asks for: answering the message, or filing it away. */
export type RowSwipeAct = 'answer' | 'archive';

/** How long the tap arriving after a swipe that acted is ignored for, so an archive does not also open what it filed. */
export const swipeSuppressesTapFor = 400;

/** What a row binds to answer a swipe, beside what it is drawing while one is in flight. */
export interface RowSwipe {
    /** How far the row is drawn from where it started, which is `0` for a row no finger is carrying. */
    readonly carried: number;

    /** What lifting the finger now would do, or `null` while the row would spring back instead. */
    readonly commits: RowSwipeAct | null;

    readonly onPointerDown: (event: PointerEvent) => void;
    readonly onPointerMove: (event: PointerEvent) => void;
    readonly onPointerUp: (event: PointerEvent) => void;
    readonly onPointerCancel: () => void;

    /** Whether the tap now arriving is the one that follows a swipe which has already acted. */
    readonly tapSuppressed: () => boolean;
}

/**
 * The gesture, bound to one row.
 *
 * @param press The row's press, which the swipe rules out as it engages and defers to once a menu is open.
 * @param acts What each direction performs, or `undefined` for a direction this list does not offer — a search result
 * cannot be filed, and a deployment that refuses a draft cannot be answered, so a row given neither answers a swipe by
 * springing back rather than by promising an act nobody would see happen.
 */
export function useRowSwipe(
    press: RowPress,
    acts: { readonly answer?: (() => void) | undefined; readonly archive?: (() => void) | undefined },
): RowSwipe {
    // How far the row is drawn is on the screen, so it is state; where the finger landed is not, so it is a ref. A
    // render per pointer move costs this one row, which is what keeps the list's scrolling out of it.
    const [carried, setCarried] = useState(0);
    const from = useRef<{ readonly pointer: number; readonly x: number; readonly y: number } | null>(null);
    const suppressing = useRef<number | null>(null);

    function stop(): void {
        from.current = null;
        setCarried(0);
    }

    // A row scrolled out of the window while its suppression window is still running takes the timer with it.
    useEffect(
        () => () => {
            if (suppressing.current !== null) {
                window.clearTimeout(suppressing.current);
            }
        },
        [],
    );

    /** Which of the two a swipe this far across is, which is its direction and nothing else. */
    function actAt(across: number): RowSwipeAct {
        return across < 0 ? 'answer' : 'archive';
    }

    /** What that direction performs, or `undefined` where the list offers nothing that way. */
    function actOf(across: number): (() => void) | undefined {
        return actAt(across) === 'answer' ? acts.answer : acts.archive;
    }

    return {
        carried,
        commits: swipeSoFar(carried, 0) === 'committed' ? actAt(carried) : null,

        onPointerDown: (event) => {
            if (!pressedByFinger(event.pointerType)) {
                return;
            }

            from.current = { pointer: event.pointerId, x: event.clientX, y: event.clientY };
        },

        onPointerMove: (event) => {
            const started = from.current;

            if (started?.pointer !== event.pointerId) {
                return;
            }

            // A menu is open over this row, so the finger still on it belongs to the press rather than to a swipe.
            if (press.tapSuppressed()) {
                stop();

                return;
            }

            const across = event.clientX - started.x;

            if (swipeSoFar(across, event.clientY - started.y) === 'cancelled') {
                stop();

                return;
            }

            // A direction the list does not offer draws nothing and carries nothing, so a finger that goes that way
            // meets a row that did not move rather than one that moved and then refused.
            setCarried(actOf(across) === undefined ? 0 : swipeCarry(across));
        },

        onPointerUp: (event) => {
            const started = from.current;
            const carriedTo = carried;

            stop();

            if (started?.pointer !== event.pointerId || swipeSoFar(carriedTo, 0) !== 'committed') {
                return;
            }

            const act = actOf(carriedTo);

            if (act === undefined) {
                return;
            }

            // The lift that finished the swipe is also a tap, and a row that acted twice on one finger would open the
            // message it has just filed away.
            if (suppressing.current !== null) {
                window.clearTimeout(suppressing.current);
            }

            suppressing.current = window.setTimeout(() => {
                suppressing.current = null;
            }, swipeSuppressesTapFor);

            act();
        },

        onPointerCancel: stop,
        tapSuppressed: () => suppressing.current !== null,
    };
}
