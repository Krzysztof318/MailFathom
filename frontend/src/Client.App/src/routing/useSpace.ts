// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useSyncExternalStore } from 'react';
import { addressOf, defaultSpace, spaceAt, type Space } from './spaces';

// The address bar is state that lives outside React and that the person changes with the back gesture as often as the
// client changes it, so it is read through `useSyncExternalStore` rather than copied into a component. Nothing here
// stores the current space: it is what the address says, and the address is the one copy of it.

function subscribeToAddress(changed: () => void): () => void {
    window.addEventListener('hashchange', changed);

    return () => {
        window.removeEventListener('hashchange', changed);
    };
}

function currentAddress(): string {
    return window.location.hash;
}

/**
 * The space the address names, re-read whenever it changes — including when the browser moves back to an earlier one.
 * An address naming no space, or naming one this credential is not offered, is written back to the one actually being
 * shown, so the two never disagree and a bookmarked address is answered rather than left standing over a blank frame.
 *
 * @param offered The spaces this credential may open, which the session decides and which is empty while it is unknown.
 * @returns The space to render, or `null` where there is none to render — a grant carrying nothing among them.
 */
export function useSpace(offered: readonly Space[]): Space | null {
    const address = useSyncExternalStore(subscribeToAddress, currentAddress);
    const named = spaceAt(address);
    const space = named !== null && offered.includes(named) ? named : (fallbackAmong(offered) ?? null);

    // Replaced rather than pushed: a first load at the root, or a fragment nobody answers, would otherwise leave the
    // back gesture landing on an address that immediately corrects itself again. Nothing is written while the offered
    // spaces are unknown, so an address somebody arrived at survives the session being read.
    useEffect(() => {
        if (space !== null && named !== space) {
            window.history.replaceState(null, '', addressOf(space));
        }
    }, [address, named, space]);

    return space;
}

/** Where the client opens among what it may open: its own default where that is offered, else the first that is. */
function fallbackAmong(offered: readonly Space[]): Space | undefined {
    return offered.includes(defaultSpace) ? defaultSpace : offered[0];
}

/**
 * Moves to a space from something that is not a link, adding a history entry the back gesture returns through.
 * Navigation a person can see is an anchor instead, which is what the navigation itself renders.
 */
export function goToSpace(space: Space): void {
    window.location.hash = addressOf(space);
}
