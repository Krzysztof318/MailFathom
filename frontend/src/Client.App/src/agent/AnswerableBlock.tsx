// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useContext, useState, type ReactNode } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { ProposalAnsweringContext, type ProposalPhase } from '../answerCanvas/proposalAnswering';
import { ProposalAnswersContext } from './proposalAnswers';

/**
 * One block of an answer, answerable where it is a proposal and the screen offers a way to answer it.
 *
 * A press that was taken holds the controls until the record says the proposal moved, rather than until the route
 * answered: the route's answer says a decision was recorded, and the phase that decision produced arrives with the next
 * read of the conversation — so releasing the controls earlier would offer the same decision twice.
 *
 * @param proposedAt The sequence the proposal was written at, which is what a decision on it is addressed by.
 * @param phase Where it stands, and `null` where the block proposes nothing.
 */
export function AnswerableBlock({
    proposedAt,
    phase,
    children,
}: {
    readonly proposedAt: number;
    readonly phase: ProposalPhase | null;
    readonly children: ReactNode;
}) {
    const answers = useContext(ProposalAnswersContext);
    const [pressing, setPressing] = useState(false);
    const [takenAt, setTakenAt] = useState<ProposalPhase | null>(null);
    const [refused, setRefused] = useState<ClientFailureReason | null>(null);

    if (phase === null || answers === null) {
        return children;
    }

    const settle = (asked: Promise<ClientFailureReason | null>): void => {
        const pressedAt = phase;

        setPressing(true);
        setRefused(null);

        void asked.then((reason) => {
            setPressing(false);
            setRefused(reason);
            setTakenAt(reason === null ? pressedAt : null);
        });
    };

    return (
        <ProposalAnsweringContext
            value={{
                phase,
                answering: pressing || takenAt === phase,
                refused,
                answer: (decision) => {
                    settle(answers.answer(proposedAt, decision));
                },
                askAnotherTime: (title) => {
                    settle(answers.askAnotherTime(proposedAt, title));
                },
                look: answers.look,
            }}
        >
            {children}
        </ProposalAnsweringContext>
    );
}
