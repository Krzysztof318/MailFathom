// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { DeclaredSource } from '@mailfathom/client-backend';

// What a run rests on, reachable from inside any block it composed. It is context rather than a prop for the one
// reason § *Components, props, and names* allows one: which component draws a block is read out of a registry, so
// nothing between the canvas and a renderer is in a position to pass a value down — and what is passed would be the
// same value to every block anyway, the sources being the run's rather than any one block's.
//
// It carries the way to follow a citation beside the sources themselves, because the two are one question: a block
// draws a source it can name, and pressing that source is how somebody checks it.

export interface AnswerSources {
    /** The sources the run declared, by the name its blocks refer to each of them by. */
    readonly sources: ReadonlyMap<string, DeclaredSource>;

    /** What following one source does, and `null` where the surface drawing the answer offers nowhere to follow it to. */
    readonly follow: ((source: string) => void) | null;
}

export const AnswerSourcesContext = createContext<AnswerSources | null>(null);

/** What the run this block belongs to rests on. */
export function useAnswerSources(): AnswerSources {
    const sources = useContext(AnswerSourcesContext);

    if (sources === null) {
        throw new Error('A block renderer read the run’s sources outside the AnswerCanvas that provides them.');
    }

    return sources;
}
