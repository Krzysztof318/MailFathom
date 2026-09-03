// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { ComposerOpening } from './composition';

// Asking for the composer, which three unrelated places do: the toolbar over the Mail space, the control a narrow
// window puts at the corner a thumb reaches, and the reply controls beside a message. None of the three is the
// composer's parent, and a callback handed down to all of them would travel through components that never open one —
// which is what `frontend/src/AGENTS.md` says context is for.
//
// What is reached through it is the act rather than what is being written: the composition itself belongs to the one
// component drawing it, so nothing outside the composer can hold half a message.

/** How a screen asks for the composer, and whether one is already open. */
export interface Composing {
    /**
     * Whether writing a message is offered at all.
     *
     * `false` where the credential may not write a draft on this deployment, which is what makes the controls stand as
     * what they are rather than open a screen the deployment would refuse every act of.
     */
    readonly offered: boolean;

    /** What is being written, or `null` where the composer is closed. */
    readonly opening: ComposerOpening | null;

    /** Opens the composer on a message of its own or on an answer to one. */
    readonly compose: (opening: ComposerOpening) => void;

    /** Closes it, which the composer itself does once there is nothing left to lose. */
    readonly close: () => void;
}

export const ComposingContext = createContext<Composing | null>(null);

export function useComposing(): Composing {
    const composing = useContext(ComposingContext);

    if (composing === null) {
        throw new Error('A component asked to compose outside the ComposingProvider that App.tsx supplies.');
    }

    return composing;
}
