// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext } from 'react';
import type { AgentProposalDecision, ClientFailureReason } from '@mailfathom/client-backend';
import type { ProposalLook } from '../answerCanvas/proposalAnswering';

// How the conversation in front answers what its agent proposed. The screen owns the routes and the conversation they
// are addressed to; a block owns only whether its own press is still on its way, so that answering one proposal holds
// its own controls rather than every proposal in the thread.

export interface ProposalAnswers {
    /** Records a decision on the proposal written at `proposedAt`, and answers why it was refused, or `null`. */
    readonly answer: (proposedAt: number, decision: AgentProposalDecision) => Promise<ClientFailureReason | null>;

    /** Declines it and asks for the same thing at another time, answering as {@link answer} does. */
    readonly askAnotherTime: (proposedAt: number, title: string) => Promise<ClientFailureReason | null>;

    readonly look: ((where: ProposalLook) => void) | null;
}

export const ProposalAnswersContext = createContext<ProposalAnswers | null>(null);
