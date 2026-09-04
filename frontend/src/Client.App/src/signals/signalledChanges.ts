// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { ClientSignal } from '@mailfathom/client-backend';

// What the deployment has said changed, as something a screen can subscribe to.
//
// It is a subscription rather than a value, because a signal is an instant rather than a state: a screen acts on one
// and then there is nothing left to render. Holding the last statement as state would put a value on the screen that
// nothing draws and that every consumer would have to remember whether it had already acted on.
//
// Nothing here decides what to re-read. Which screens care about which statement is each screen's, which is what keeps
// this module free of every route the client reads.

/** What a screen does with one statement. */
export type SignalListener = (signal: ClientSignal) => void;

/** How a screen hears what the deployment said. */
export interface SignalledChanges {
    /**
     * Listens until the returned function is called, which is what an effect returns.
     *
     * @returns How to stop listening.
     */
    readonly listen: (listener: SignalListener) => () => void;
}

/** What a tree with no channel above it hears, which is every client whose deployment serves no hub. */
export const nothingSignalled: SignalledChanges = { listen: () => () => undefined };

export const SignalledChangesContext = createContext<SignalledChanges>(nothingSignalled);

export function useSignalledChanges(): SignalledChanges {
    return useContext(SignalledChangesContext);
}
