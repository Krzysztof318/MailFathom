// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// Blocking the whole client on one operation, which is an application-level act rather than a screen's: the operations
// that need it — a mailbox migration, a bulk change, an export, a search index rebuilt — each begin somewhere different
// and none of them is the parent of what has to be covered. It is reached through context for the reason the composer
// is, and it exists as one surface so that the next such operation asks for this one instead of inventing a modal of
// its own.
//
// What is reached through it is the act rather than the operation: the work itself belongs to whatever started it, so
// nothing here runs anything, times anything, or knows when it finished. An operation says what it is doing and how
// far it has got; when it stops saying so, the client is no longer blocked.

/** What the client is blocked on while it happens. */
export interface BlockingOperation {
    /** What is happening, in the few words a person reads first. */
    readonly title: string;

    /** Why the client is blocked while it happens, said as a sentence rather than as a state. */
    readonly explanation: string;

    /**
     * How far it has got, from 0 to 1.
     *
     * Absent where the operation genuinely cannot say, which draws the variant reporting that something is happening
     * rather than how far it is — an operation that does not know is never drawn as one that does.
     */
    readonly progress?: number;

    /**
     * What stopping it would leave behind, said before the person confirms rather than afterwards.
     *
     * It is the operation's own sentence because only the operation knows what survives a stop: how much of a mailbox
     * already moved, which half of a bulk change was written, what an interrupted export holds.
     */
    readonly stoppingLeavesBehind: string;

    /** Stops it. Called once, and only once the person has confirmed. */
    readonly stop: () => void;
}

/** How an operation blocks the client. */
export interface Blocking {
    /** Blocks the client on an operation, or restates the one it is already blocked on as that one moves. */
    readonly block: (operation: BlockingOperation) => void;

    /** Releases it, which is what an operation calls when it has finished or has been stopped. */
    readonly release: () => void;
}

export const BlockingContext = createContext<Blocking | null>(null);

export function useBlocking(): Blocking {
    const blocking = useContext(BlockingContext);

    if (blocking === null) {
        throw new Error('A component asked to block the client outside the BlockingContext that App.tsx supplies.');
    }

    return blocking;
}
