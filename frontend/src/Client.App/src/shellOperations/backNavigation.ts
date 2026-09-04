// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';

// Going back, which on Android is a system gesture, on the web is the browser's own control, and on the desktop is
// whatever the window manager offers — and which is one thing in this client rather than three, because all three
// arrive as the same event.
//
// **It is a shell operation and it sits here for that reason**, ADR 0027 § _The no-platform-branch rule holds, and the
// shell is the only seam a head operation crosses_ naming the back gesture the third member of this directory, beside
// where the credential is kept and how a system notification is raised.
// What it resolves, though, is that there is nothing left to choose between: the Android shell is asked to hand the
// gesture to the page it is showing — `handleBackNavigation` in the head's own activity — which is what a browser
// already does, so back arrives in all three heads as one entry coming off the session history. So this module reads
// no shell command, offers no context, and publishes one hook; a second implementation to pick between would be an
// indirection with one implementation, which is the abstraction `frontend/src/AGENTS.md` refuses. What makes the
// difference between the heads disappear is the shell being configured rather than the application being told which
// shell it is in, and the desktop and web heads gained the behaviour without a line written for either.
//
// **What is unwound, and in what order.** Whatever stands on top of the screen goes first — an overflow sheet, a
// popover, a drawer, a dialog — one per press, from `shell/screenLayers.ts`. Only with nothing standing over it does
// back move inside the screen, which in the single-pane composition is a message going back to the list it was opened
// from. Only with nothing left to go back to at all does it reach the platform, which then does what it does with an
// application at its root: on Android it leaves, in a browser it goes wherever the tab was before.
//
// **How a press reaches us.** Neither a layer nor the pane is an address — nothing about mail is written where it
// outlives the process, and a message identifier in a history entry is exactly that, kept by the browser's own store
// under [ADR 0028](../../../../../docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md).
// So what is pushed is an entry at the address already showing, marked with how many steps stand over the screen at
// that point. Back consumes one of those marks, the page is told, and the number the mark carries says how many steps
// to unwind; the address never changes, so the space the reader is in is the space the address names throughout.
//
// The marks are pushed and given up by one effect rather than by whoever opened a surface, which is what keeps them
// honest: a surface closed by its own control leaves one step fewer than the history holds, and the same effect gives
// the spare entry back. Nothing else in the client writes history state, and `routing/useSpace.ts` carries a mark
// across the one address it rewrites rather than replacing it with nothing.

const stepsTaken = 'mailfathom.back';

/** Whatever the entry showing carries, as something a mark can be written beside rather than over. */
function asState(state: unknown): Record<string, unknown> {
    return typeof state === 'object' && state !== null ? (state as Record<string, unknown>) : {};
}

/** How many steps the entry showing says stand over the screen, which is none for an entry nothing marked. */
function markedSteps(): number {
    const state: unknown = window.history.state;

    if (typeof state !== 'object' || state === null || !(stepsTaken in state)) {
        return 0;
    }

    const marked: unknown = (state as Record<string, unknown>)[stepsTaken];

    return typeof marked === 'number' && Number.isInteger(marked) && marked > 0 ? marked : 0;
}

/**
 * Keeps the session history holding one entry per step the back gesture has to unwind, and unwinds them as it is used.
 *
 * @param steps How many things stand between the reader and the screen underneath — every open layer, plus the pane
 *   standing in front of the list where the composition shows one at a time.
 * @param unwind Takes that many of them away, the topmost first, in one call. It is handed the count rather than
 *   called once per step because closing one of them is a revision the caller's own event does not yet hold, so a
 *   second call would answer against the screen as it stood before the first.
 */
export function useBackNavigation(steps: number, unwind: (used: number) => void): void {
    const standing = useRef(steps);
    const unwinding = useRef(unwind);
    const reconciled = useRef(false);

    useEffect(() => {
        standing.current = steps;
        unwinding.current = unwind;
    });

    // The history is brought into step with the screen rather than written by whoever changed it: a surface that opened
    // needs an entry to consume, and one closed by its own control leaves an entry nobody will. Both are the same
    // difference read in opposite directions, which is why one effect answers both.
    //
    // The first reading of it is the exception, and it is a reload: the entry showing keeps whatever mark it carried
    // before the page was thrown away, while the client that came back has nothing standing over its screen. Giving
    // those entries up would be a reload that walks the reader backwards out of the client, so the entry is re-marked
    // for the screen actually being drawn and the entries behind it are left where they are.
    useEffect(() => {
        const marked = markedSteps();

        if (steps > marked) {
            for (let step = marked + 1; step <= steps; step += 1) {
                window.history.pushState({ [stepsTaken]: step }, '');
            }
        } else if (steps < marked) {
            if (reconciled.current) {
                window.history.go(steps - marked);
            } else {
                window.history.replaceState({ ...asState(window.history.state), [stepsTaken]: steps }, '');
            }
        }

        reconciled.current = true;
    }, [steps]);

    useEffect(() => {
        function backWasUsed(): void {
            // What the entry now showing says stands over the screen, against what actually does. A press that landed
            // on an address of ours rather than on a mark says nothing stands over it, which is the same arithmetic:
            // an unmarked entry is zero steps.
            const used = standing.current - markedSteps();

            if (used > 0) {
                unwinding.current(used);
            }
        }

        window.addEventListener('popstate', backWasUsed);

        return () => {
            window.removeEventListener('popstate', backWasUsed);
        };
    }, []);
}
