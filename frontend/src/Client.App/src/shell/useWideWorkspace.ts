// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useSyncExternalStore } from 'react';

// The three widths a component has to ask about rather than compose against. A stylesheet answers a width question for
// anything CSS can lay out two ways from one tree, and that is almost everything; what it cannot do is decide *which*
// of two screens is in the document, or whether a control is one a person may operate at all.
//
// Both queries are built from the tokens in `styles.css` rather than from a second copy of either number, so each
// breakpoint stays one decision.

function widthAtLeast(token: string, fallback: string) {
    function query(): MediaQueryList | null {
        if (typeof window.matchMedia !== 'function') {
            return null;
        }

        const width = getComputedStyle(document.documentElement).getPropertyValue(token).trim();

        return window.matchMedia(`(min-width: ${width === '' ? fallback : width})`);
    }

    return {
        /** Whether the window is at least this wide. A runtime that cannot answer is read as wide. */
        matches: (): boolean => query()?.matches ?? true,

        watch: (changed: () => void): (() => void) => {
            const watched = query();

            watched?.addEventListener('change', changed);

            return () => {
                watched?.removeEventListener('change', changed);
            };
        },
    };
}

// What the pointer can do, which is the other half of what a screen adapts to and the only one of the two that is not
// a width. It is asked here beside them because a component asking either question asks it the same way, and because
// what it answers decides which gestures exist rather than how something is laid out — a query CSS cannot answer for
// a listener that has to be bound or not bound at all.
function pointerIsCoarse() {
    function query(): MediaQueryList | null {
        return typeof window.matchMedia === 'function' ? window.matchMedia('(pointer: coarse)') : null;
    }

    return {
        /** Whether the pointer is one a finger drives. A runtime that cannot answer is read as fine. */
        matches: (): boolean => query()?.matches ?? false,

        watch: (changed: () => void): (() => void) => {
            const watched = query();

            watched?.addEventListener('change', changed);

            return () => {
                watched?.removeEventListener('change', changed);
            };
        },
    };
}

// Built once each, because `useSyncExternalStore` compares the two functions it is handed by identity and a pair
// rebuilt per render would resubscribe on every one of them.
const workspaceWidth = widthAtLeast('--breakpoint-workspace', '43.75rem');
const paneWidth = widthAtLeast('--breakpoint-panes', '51.25rem');
const desktopWidth = widthAtLeast('--breakpoint-desktop', '73.75rem');
const coarsePointer = pointerIsCoarse();

/**
 * Whether the window is at or above the width the phone shape stops at, kept current as it is resized across that
 * breakpoint.
 *
 * What needs asking rather than composing is where a surface stands at all: bottom navigation is a row of five places
 * and a rail is a column of nine, and a sheet rising from the foot of the window is a different element from a panel
 * beside the rail.
 */
export function useWideWorkspace(): boolean {
    return useSyncExternalStore(workspaceWidth.watch, workspaceWidth.matches);
}

/**
 * Whether the window has room for the list and the message side by side, kept current as it is resized across that
 * breakpoint.
 *
 * The single-pane Mail space is what needs asking: it draws the list or the message and never both, so a row that is
 * not on the screen is not in the document either — and going between them is navigation the back gesture returns
 * through rather than a layout that changed.
 */
export function useTwoPanes(): boolean {
    return useSyncExternalStore(paneWidth.watch, paneWidth.matches);
}

/**
 * Whether the window is in the desktop composition rather than the tablet one.
 *
 * It is the widest of the three, and the design project draws three things at it together: the mailbox column stands
 * beside the list rather than in a drawer over it, the toolbar's controls carry their labels, and the tab mode is
 * worth offering at all — a row of tabs above the columns needs room a rail beside two of them does not. Below it the
 * tab switch is inert rather than absent: a control that vanished by width alone would leave somebody who had turned
 * it on with no way to reach it, and the row says why instead.
 */
export function useDesktopComposition(): boolean {
    return useSyncExternalStore(desktopWidth.watch, desktopWidth.matches);
}

/**
 * Whether the pointer driving this client is one a finger drives, kept current as a device is picked up or put down.
 *
 * What needs asking is a gesture rather than a size: a drag that follows a finger one to one is bound or not bound at
 * all, and a stylesheet cannot decide that. Everything a query can decide — a target big enough to hit, an affordance
 * that would otherwise exist only under a hover — stays in the stylesheet as `pointer-coarse:`.
 */
export function useCoarsePointer(): boolean {
    return useSyncExternalStore(coarsePointer.watch, coarsePointer.matches);
}
