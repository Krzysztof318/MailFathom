// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type {
    AgentProposalDecision,
    AgentProposalState,
    ClientFailureReason,
    DeclaredSource,
} from '@mailfathom/client-backend';

// What a proposal block may do on the surface drawing it. Discover draws an answer nobody can act on from there, so it
// provides nothing and every proposal renders read-only; a conversation provides one per block, which is what gives the
// same renderer its controls without a second component for the surface that has them.

/** Where a proposal stands: pending until a resolution says otherwise, and the latest resolution after that. */
export type ProposalPhase = 'pending' | AgentProposalState;

/** Somewhere a proposal points that the reader may go and look at before deciding. */
export type ProposalLook = { readonly kind: 'calendar' } | { readonly kind: 'thread'; readonly email: string };

export interface ProposalAnswering {
    /** Where the proposal stands, as the conversation's record says. */
    readonly phase: ProposalPhase;

    /** Whether a decision the reader pressed is still on its way, which holds every control until it answers. */
    readonly answering: boolean;

    /** Why the last decision pressed was not taken, and `null` where none was refused. */
    readonly refused: ClientFailureReason | null;

    readonly answer: (decision: AgentProposalDecision) => void;

    /** Declines the proposal and asks the agent for the same thing at another time, in the reader's own words. */
    readonly askAnotherTime: (title: string) => void;

    /** Opens what the proposal points at, and `null` where the surface has nowhere to open it. */
    readonly look: ((where: ProposalLook) => void) | null;
}

export const ProposalAnsweringContext = createContext<ProposalAnswering | null>(null);

/** How this block may be answered, and `null` where the surface drawing it offers no way to. */
export function useProposalAnswering(): ProposalAnswering | null {
    return useContext(ProposalAnsweringContext);
}

/**
 * The first source a block cites that names a message, which is the thread the proposal came out of.
 *
 * @param citations The sources the block cites, in the order it cites them.
 * @param sources Everything the run declared, by the name a block cites each by.
 */
export function citedThread(
    citations: readonly string[],
    sources: ReadonlyMap<string, DeclaredSource>,
): { readonly label: string; readonly email: string } | null {
    for (const cited of citations) {
        const target = sources.get(cited)?.target;

        if (target !== undefined && target !== null) {
            return { label: sources.get(cited)?.label ?? '', email: target.email };
        }
    }

    return null;
}
