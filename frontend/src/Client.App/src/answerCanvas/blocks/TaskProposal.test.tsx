// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AgentProposalDecision, AnswerBlock, BlockEvidence, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { ProposalAnsweringContext, type ProposalAnswering } from '../proposalAnswering';
import { TaskProposal } from './TaskProposal';

const request: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Schedule for the hall',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

function proposed(dueOn: string | null = '2026-09-25'): AnswerBlock {
    return {
        type: 'taskProposal',
        named: 'taskProposal',
        evidence: backed,
        proposal: { title: 'Send the schedule', dueOn },
    };
}

function drawTask(block: AnswerBlock, answering: ProposalAnswering | null = null) {
    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: new Map([[request.id, request]]), follow: null }}>
                <ProposalAnsweringContext value={answering}>
                    <TaskProposal block={block} />
                </ProposalAnsweringContext>
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

function pending(answer: (decision: AgentProposalDecision) => void): ProposalAnswering {
    return {
        phase: 'pending',
        answering: false,
        refused: null,
        answer,
        askAnotherTime: () => undefined,
        look: null,
    };
}

describe('TaskProposal', () => {
    it('shows the day it would be due and the message it came out of', () => {
        drawTask(proposed());

        expect(screen.getByText('Send the schedule')).toBeDefined();
        expect(screen.getByText('September 25, 2026')).toBeDefined();
        expect(screen.getByText('Schedule for the hall').getAttribute('lang')).toBe('');
    });

    it('adds it or declines it, each named for the task it acts on', () => {
        const answer = vi.fn<(decision: AgentProposalDecision) => void>();
        drawTask(proposed(), pending(answer));

        fireEvent.click(
            screen.getByRole('button', { name: 'Add the task “Send the schedule”, due September 25, 2026' }),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Decline the task “Send the schedule”' }));

        expect(answer.mock.calls).toEqual([['accepted'], ['declined']]);
    });

    it('names a task with no due day without inventing one', () => {
        drawTask(
            proposed(null),
            pending(() => undefined),
        );

        expect(screen.queryByText('Due')).toBeNull();
        expect(screen.getByRole('button', { name: 'Add the task “Send the schedule”' })).toBeDefined();
    });
});
