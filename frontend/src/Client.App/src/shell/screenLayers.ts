// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';

// What stands over the screen, in the order it was opened. A drawer, a dialog, a popover, and an overflow sheet are
// each one of these, and the shell has two questions about them that no single one of them can answer for itself:
// **which is on top**, so the back gesture closes that one and no other, and **are any of them still open**, so moving
// to another destination can leave none of them behind.
//
// The platform keeps a stack of its own — a modal dialog and a popover both stand in the top layer, and Escape unwinds
// it one element at a time — but it publishes neither the order nor a way to unwind it from code. So the order is
// recorded here as each surface opens, and each surface stays the one thing that knows how to close itself: what is
// registered is a way to close it rather than the element, which is what lets a drawer that is a `dialog`, a menu that
// is a popover, and a confirmation that asks a question before it goes all live in one stack.
//
// It is a stack rather than a set because that is the whole of what it is for: `closeTop` is one press of the back
// gesture, and `closeEvery` is a person taking the bottom navigation somewhere else — a layer that survived that is a
// layer they cannot get rid of without finding its own close control, which on a phone is the trap the rule against it
// exists to prevent.
//
// **One surface deliberately registers nothing**, and it is the blocking overlay: an operation that has to finish
// before the client is usable again is not something a gesture may dismiss, and it carries its own way to stop what it
// is waiting on. A surface that back must not close is a surface that is not a layer.
//
// The array is a ref rather than state because `closeTop` is answered inside one event and its answer decides what the
// next line does: unwinding two of them in one press has to reach two different surfaces, and a component's own state
// has not settled by then. The count beside it is state, because what re-renders on it is the shell arithmetic that
// decides how many history entries the back gesture has to consume.

/** One thing standing over the screen, held as the way to close it. */
interface OpenLayer {
    readonly close: (clearingTheScreen: boolean) => void;
}

/** The layers standing over the screen, and what the shell does to them. */
export interface ScreenLayers {
    /** How many stand over the screen now. */
    readonly depth: number;

    /**
     * Records one while it is open. The returned function withdraws it, and withdrawing twice is a no-op.
     *
     * What is recorded is told which of the two asked, because going away means different things in them: one press
     * of the back gesture leaves a surface with whatever it stood on still on the screen, and clearing the screen
     * leaves nothing at all — so a surface that puts something back as it goes has to know not to.
     */
    readonly opened: (close: (clearingTheScreen: boolean) => void) => () => void;

    /** Closes the one on top, answering whether there was one to close. */
    readonly closeTop: () => boolean;

    /** Closes every one of them, the top first. */
    readonly closeEvery: () => void;
}

export const ScreenLayersContext = createContext<ScreenLayers | null>(null);

/** Builds the stack the shell supplies, which the composition root does once. */
export function useScreenLayerStack(): ScreenLayers {
    const open = useRef<OpenLayer[]>([]);
    const [depth, setDepth] = useState(0);

    // Each of the three is built once and never again, because a surface registers itself against `opened` and would
    // withdraw and register afresh on every change of depth if that function were composed per render — which is a
    // registration that removes itself while the stack is being read.
    const opened = useCallback((close: (clearingTheScreen: boolean) => void) => {
        const layer: OpenLayer = { close };

        open.current = [...open.current, layer];
        setDepth(open.current.length);

        return () => {
            const left = open.current.filter((standing) => standing !== layer);

            if (left.length !== open.current.length) {
                open.current = left;
                setDepth(left.length);
            }
        };
    }, []);

    const closeTop = useCallback(() => {
        const top = open.current.at(-1);

        if (top === undefined) {
            return false;
        }

        // Taken off the stack before it is asked to close rather than after, because closing is what a surface does to
        // its own state and that has not settled inside this event: two steps unwound together would otherwise both
        // reach the same surface and leave the one beneath it standing.
        open.current = open.current.slice(0, -1);
        setDepth(open.current.length);
        top.close(false);

        return true;
    }, []);

    const closeEvery = useCallback(() => {
        const standing = open.current;

        open.current = [];
        setDepth(0);

        for (const layer of [...standing].reverse()) {
            layer.close(true);
        }
    }, []);

    return useMemo(() => ({ depth, opened, closeTop, closeEvery }), [depth, opened, closeTop, closeEvery]);
}

/**
 * Records a surface as standing over the screen for as long as it is open, so that the back gesture reaches it and a
 * change of destination closes it.
 *
 * A surface drawn with no shell around it registers nothing and is otherwise unchanged, which is the one place this
 * differs from the client's other contexts and is deliberate rather than lax. What the shell supplies here is a service
 * to a surface rather than a value it renders from: the sign-in screen stands in front of the frame and opens nothing
 * over itself, and a test drawing one surface alone is looking at that surface rather than at a shell. Refusing to draw
 * in either case would be refusing over something neither of them has any use for.
 *
 * @param open Whether the surface is on the screen. A surface mounted while closed registers nothing.
 * @param close What closing it does, which is the surface's own way out rather than a second implementation of it. It
 *   is told whether the shell is clearing the screen rather than answering one press, which a surface only reads where
 *   the two differ — putting back what it was opened from is the case that has one.
 * @param restoredBy A value that changes whenever closing this surface put a question in front of it instead of
 *   taking it off the screen. `closeTop` spends the registration on the press that reached it, so a surface still
 *   standing afterwards has to be recorded again or the next press would go straight past it.
 */
export function useScreenLayer(open: boolean, close: (clearingTheScreen: boolean) => void, restoredBy?: unknown): void {
    const layers = useContext(ScreenLayersContext);
    const closing = useRef(close);

    // The stack holds one function for the life of the registration, and this is what keeps that function current:
    // what closing a surface does is written where the surface is drawn, and every render may compose it afresh.
    useEffect(() => {
        closing.current = close;
    });

    const opened = layers?.opened;

    useEffect(() => {
        if (!open || opened === undefined) {
            return;
        }

        return opened((clearingTheScreen) => {
            closing.current(clearingTheScreen);
        });
    }, [open, opened, restoredBy]);
}
