// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef } from 'react';

// Keeping the tail of a conversation in view while it is composed, as the design project's rule rather than as a
// `scrollTop` written whenever something arrives. The thread follows the bottom until the reader scrolls up past the
// threshold, and follows it again once they come back down to it. The measurement is made two frames after the content
// changed, so what arrived has been laid out before the gap is read; and a hop across most of the viewport is a jump
// rather than an animation, because a smooth scroll across a whole screen height is the jumping this exists to prevent.

/** How far above the bottom the reader has to scroll before the thread stops following it, in CSS pixels. */
const releasedPast = 40;

/** The share of the viewport past which the thread jumps to the bottom rather than scrolling there. */
const jumpedPast = 0.85;

/** What the scroller answers the reader's own gestures with. */
export interface PinnedToBottom {
    /** Binds the element that scrolls, which stays this hook's to hold. */
    readonly attachScroller: (element: HTMLDivElement | null) => void;
    readonly onScroll: () => void;

    /** A wheel or a finger moving the thread, which is what can release it. */
    readonly onGesture: () => void;
}

function gapBelow(element: HTMLElement): number {
    return element.scrollHeight - element.clientHeight - element.scrollTop;
}

/**
 * Pins a scroller to its bottom as its content changes.
 *
 * @param content Anything that changes when the content does, which is what re-measures.
 * @param following Changed by the screen whenever the reader asks something or opens a conversation, which follows the
 * bottom again whatever they had scrolled to.
 */
export function usePinnedToBottom(content: unknown, following: number): PinnedToBottom {
    const scroller = useRef<HTMLDivElement | null>(null);
    const released = useRef(false);

    useEffect(() => {
        released.current = false;
    }, [following]);

    useEffect(() => {
        let second = 0;

        const first = requestAnimationFrame(() => {
            second = requestAnimationFrame(() => {
                const element = scroller.current;

                if (element === null || released.current) {
                    return;
                }

                const gap = gapBelow(element);

                if (gap <= 2) {
                    return;
                }

                const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

                if (still || gap > element.clientHeight * jumpedPast) {
                    element.scrollTop = element.scrollHeight;
                } else {
                    element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });
                }
            });
        });

        return () => {
            cancelAnimationFrame(first);
            cancelAnimationFrame(second);
        };
    }, [content]);

    const attachScroller = useCallback((element: HTMLDivElement | null): void => {
        scroller.current = element;
    }, []);

    const onScroll = useCallback((): void => {
        const element = scroller.current;

        if (element !== null && gapBelow(element) < releasedPast) {
            released.current = false;
        }
    }, []);

    const onGesture = useCallback((): void => {
        const element = scroller.current;

        if (element !== null) {
            released.current = gapBelow(element) > releasedPast;
        }
    }, []);

    return { attachScroller, onScroll, onGesture };
}
