// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
 * An address naming no space is written back to the one actually being shown, so the two never disagree.
 */
export function useSpace(): Space {
    const address = useSyncExternalStore(subscribeToAddress, currentAddress);
    const space = spaceAt(address) ?? defaultSpace;

    // Replaced rather than pushed: a first load at the root, or a fragment nobody answers, would otherwise leave the
    // back gesture landing on an address that immediately corrects itself again.
    useEffect(() => {
        if (spaceAt(address) === null) {
            window.history.replaceState(null, '', addressOf(space));
        }
    }, [address, space]);

    return space;
}

/**
 * Moves to a space from something that is not a link, adding a history entry the back gesture returns through.
 * Navigation a person can see is an anchor instead, which is what the navigation itself renders.
 */
export function goToSpace(space: Space): void {
    window.location.hash = addressOf(space);
}
