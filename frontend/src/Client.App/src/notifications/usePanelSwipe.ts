// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { barSlop, rowSlop, springMilliseconds, swipeOffset, swipeSettles, swipeTravelled } from './panelSwipe';

// The pointers behind the two gestures. What they mean is `panelSwipe.ts`; this is where a finger reaches them.
//
// **Both belong to a coarse pointer in the phone composition and to nothing else.** The fold, the tablet, and the
// desktop draw the panel beside the rail, where there is no bottom navigation to pull it from and a scrim, a close
// control, and Escape to leave it by — so the caller passes `false` there and every handler below becomes inert.
//
// **Neither gesture is the only way to anything.** A tap on the bell opens the panel and the close control leaves it,
// at every width; these are a second route rather than the route, which is what keeps every act reachable from a
// keyboard that has no swipe.
//
// **A gesture is taken over rather than begun.** Until the bar's own slop is passed the touch still belongs to the
// navigation item it landed on, and until the list underneath can scroll no further the movement is still that list's.
// The zero point is set at the moment of the handover rather than at the moment of the touch, so the panel never jumps
// by the slop it took to get there.

/** What the frame binds to drive the panel with a finger, and what it draws the panel at while one is on the screen. */
export interface PanelSwipe {
    /** How far the panel stands from open, in pixels, or `null` where no gesture is holding it. */
    readonly offset: number | null;

    /** Whether a finger is holding the panel right now, which is what takes the transition off it. */
    readonly dragging: boolean;

    /** Whether the panel is returning to where it was, which is the one motion with a duration of its own. */
    readonly springing: boolean;

    /** Bound to the panel itself, which is what its height is measured off. */
    readonly attachPanel: (element: HTMLElement | null) => void;

    /** Bound to the list inside it, which keeps the gesture until it can scroll no further. */
    readonly attachList: (element: HTMLElement | null) => void;

    /** What the bottom navigation binds to answer an upward swipe. */
    readonly onNavigationPointerDown: (event: ReactPointerEvent) => void;

    /** What the navigation binds so the tap ending a swipe does not also follow the link it started on. */
    readonly onNavigationClickCapture: (event: { preventDefault: () => void; stopPropagation: () => void }) => void;

    /** What the panel binds to answer a downward swipe anywhere on it. */
    readonly onPanelPointerDown: (event: ReactPointerEvent) => void;
}

interface Held {
    readonly closing: boolean;
    readonly height: number;

    /** Where the gesture is measured from, which moves while a list underneath is still scrolling on it. */
    from: number;
    at: number;
    when: number;
    velocity: number;
    anchor: number | null;
}

/**
 * Binds the two gestures to the panel and to the bottom navigation.
 *
 * @param shown Whether the panel is open, which is what decides which of the two gestures a touch can begin.
 * @param offered Whether this composition has these gestures at all — a coarse pointer in a narrow window.
 * @param onOpen Opens the panel, called at the moment the navigation gives the gesture up rather than at the end of it.
 * @param onClose Closes it, called when a gesture that was pushing it away is released past the threshold.
 */
export function usePanelSwipe(shown: boolean, offered: boolean, onOpen: () => void, onClose: () => void): PanelSwipe {
    const panel = useRef<HTMLElement>(null);
    const list = useRef<HTMLElement>(null);
    const held = useRef<Held | null>(null);
    const tapTaken = useRef(false);
    const springingFor = useRef<number | null>(null);
    const [offset, setOffset] = useState<number | null>(null);
    const [dragging, setDragging] = useState(false);
    const [springing, setSpringing] = useState(false);

    useEffect(
        () => () => {
            if (springingFor.current !== null) {
                window.clearTimeout(springingFor.current);
            }
        },
        [],
    );

    // The two elements arrive as bindings rather than as the refs themselves, so what holds them stays this hook's:
    // a component handed a ref to write into is a component writing into somebody else's state.
    const attachPanel = useCallback((element: HTMLElement | null): void => {
        panel.current = element;
    }, []);

    const attachList = useCallback((element: HTMLElement | null): void => {
        list.current = element;
    }, []);

    const springBack = useCallback((): void => {
        setOffset(0);
        setSpringing(true);

        if (springingFor.current !== null) {
            window.clearTimeout(springingFor.current);
        }

        springingFor.current = window.setTimeout(() => {
            springingFor.current = null;
            setSpringing(false);
            setOffset(null);
        }, springMilliseconds);
    }, []);

    const begin = useCallback(
        (event: ReactPointerEvent, closing: boolean): void => {
            if (!offered || held.current !== null || event.pointerType === 'mouse') {
                return;
            }

            // A panel that is not open is not in the document either, so it measures nothing: an opening gesture
            // takes the window's height instead, which is what the sheet is about to fill anyway.
            const measured = panel.current?.offsetHeight ?? 0;

            held.current = {
                closing,
                from: event.clientY,
                height: measured > 0 ? measured : window.innerHeight,
                at: event.clientY,
                when: event.timeStamp,
                velocity: 0,
                anchor: null,
            };

            if (!closing) {
                tapTaken.current = false;
            }
        },
        [offered],
    );

    // Bound to the document rather than to the element the touch landed on, because a finger that has taken the panel
    // over goes on driving it wherever it travels — including off the navigation it started on and past the edge of
    // the panel it is pushing away.
    useEffect(() => {
        if (!offered) {
            return;
        }

        function moved(event: PointerEvent): void {
            const gesture = held.current;

            if (gesture === null) {
                return;
            }

            gesture.velocity = (event.clientY - gesture.at) / Math.max(1, event.timeStamp - gesture.when);
            gesture.at = event.clientY;
            gesture.when = event.timeStamp;

            const travelled = event.clientY - gesture.from;

            if (gesture.anchor === null) {
                const verdict = handover(gesture, travelled, list.current);

                if (verdict === 'abandoned') {
                    held.current = null;

                    return;
                }

                if (verdict === 'waiting') {
                    return;
                }

                gesture.anchor = event.clientY;

                if (gesture.closing) {
                    setOffset(0);
                } else {
                    // The panel is put on the screen at the moment the bar gives the gesture up, already closed, so the
                    // first movement after the handover draws it rising from under the finger.
                    tapTaken.current = true;
                    setOffset(gesture.height);
                    onOpen();
                }

                setDragging(true);
                setSpringing(false);

                return;
            }

            setOffset(swipeOffset(event.clientY - gesture.anchor, gesture.height, gesture.closing));
        }

        function released(): void {
            const gesture = held.current;

            held.current = null;

            if (gesture?.anchor == null) {
                return;
            }

            setDragging(false);

            const standing = swipeOffset(gesture.at - gesture.anchor, gesture.height, gesture.closing);
            const settles = swipeSettles(
                swipeTravelled(standing, gesture.height, gesture.closing),
                gesture.velocity,
                gesture.closing,
            );

            if (settles) {
                // The gesture finished where it was going. The offset goes, so the panel is drawn where its own state
                // says rather than where a finger left it; only the closing direction has anything to tell the frame,
                // because the opening one already told it at the handover.
                setOffset(null);
                setSpringing(false);

                if (gesture.closing) {
                    onClose();
                }

                return;
            }

            // It did not, so it goes back to where it started — which is not the same act in the two directions. A
            // panel being pushed away was open before the gesture and springs back to open; a panel being pulled up
            // was closed, and was put on the screen at the handover, so going back means closing it again.
            if (gesture.closing) {
                springBack();

                return;
            }

            setOffset(null);
            setSpringing(false);
            onClose();
        }

        document.addEventListener('pointermove', moved);
        document.addEventListener('pointerup', released);
        document.addEventListener('pointercancel', released);

        return () => {
            document.removeEventListener('pointermove', moved);
            document.removeEventListener('pointerup', released);
            document.removeEventListener('pointercancel', released);
        };
    }, [offered, onOpen, onClose, springBack]);

    return {
        offset,
        dragging,
        springing,
        attachPanel,
        attachList,
        onNavigationPointerDown: (event) => {
            if (!shown) {
                begin(event, false);
            }
        },
        onNavigationClickCapture: (event) => {
            if (!tapTaken.current) {
                return;
            }

            tapTaken.current = false;
            event.preventDefault();
            event.stopPropagation();
        },
        onPanelPointerDown: (event) => {
            begin(event, true);
        },
    };
}

/** What a movement made before the handover comes to: the panel takes it, nothing yet, or it was never this gesture. */
type Handover = 'taken' | 'waiting' | 'abandoned';

/**
 * What the movement so far means, before the panel has taken the gesture over.
 *
 * Downward on the panel is taken from the list, and only once the list can scroll no further; upward on the navigation
 * is taken from the item the finger landed on, and only past the bar's own slop. Movement the other way on the
 * navigation is nobody's gesture at all, which is what `abandoned` says — the touch goes back to the item it landed
 * on, tap and all.
 */
function handover(gesture: Held, travelled: number, scrolling: HTMLElement | null): Handover {
    if (gesture.closing) {
        if (travelled < rowSlop) {
            return 'waiting';
        }

        // A list scrolled away from its top scrolls first. The zero point moves with the finger while that is true, so
        // the same uninterrupted movement becomes the panel's the moment the list reaches the top.
        if ((scrolling?.scrollTop ?? 0) > 0.5) {
            gesture.from = gesture.at;

            return 'waiting';
        }

        return 'taken';
    }

    if (travelled > barSlop) {
        return 'abandoned';
    }

    return -travelled >= barSlop ? 'taken' : 'waiting';
}
