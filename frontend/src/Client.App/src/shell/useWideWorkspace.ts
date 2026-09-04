// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useSyncExternalStore } from 'react';

// The two widths a component has to ask about rather than compose against. A stylesheet answers a width question for
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
const workspaceWidth = widthAtLeast('--breakpoint-workspace', '48.75rem');
const tabsWidth = widthAtLeast('--breakpoint-tabs', '73.75rem');
const coarsePointer = pointerIsCoarse();

/**
 * Whether the window is at or above the width the workspace opens out at, kept current as it is resized across that
 * breakpoint.
 *
 * The narrow Mail space is what needs asking: it draws the list or the message and never both, so a row that is not on
 * the screen is not in the document either.
 */
export function useWideWorkspace(): boolean {
    return useSyncExternalStore(workspaceWidth.watch, workspaceWidth.matches);
}

/**
 * Whether the window is wide enough for the tab mode to be worth offering.
 *
 * It is a second and wider breakpoint than the workspace's, because a rail beside two columns fits long before a row
 * of tabs above them does. Below it the switch is inert rather than absent: a control that vanished by width alone
 * would leave somebody who had turned it on with no way to reach it, and the row says why instead.
 */
export function useWideEnoughForTabs(): boolean {
    return useSyncExternalStore(tabsWidth.watch, tabsWidth.matches);
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
