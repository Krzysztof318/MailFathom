// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type {
    AgentProposalDecision,
    AnswerBlock,
    BlockEvidence,
    DeclaredSource,
    SuggestedAction as Suggestion,
    SuggestedActionImpact,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { ProposalAnsweringContext, type ProposalAnswering, type ProposalPhase } from '../proposalAnswering';
import { SuggestedAction } from './SuggestedAction';

const addendum: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Contract addendum — signatures',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const reply: Suggestion = {
    action: 'ReplyToThread',
    reason: 'The decision is due on 28.08 and they have been waiting two days.',
    impact: 'SendsMail',
    requiresConfirmation: true,
};

function suggested(over: Partial<Suggestion> = {}): AnswerBlock {
    return {
        type: 'suggestedAction',
        named: 'suggestedAction',
        evidence: backed,
        suggestion: { ...reply, ...over },
    };
}

function inConversation(
    phase: ProposalPhase,
    answer: (decision: AgentProposalDecision) => void = () => undefined,
): ProposalAnswering {
    return { phase, answering: false, refused: null, answer, askAnotherTime: () => undefined, look: null };
}

function renderSuggestion(block: AnswerBlock, answering: ProposalAnswering | null = null) {
    const declared = new Map([[addendum.id, addendum]]);

    return render(
        <LocalizationProvider>
            {/* A citation is given somewhere to follow to, as the canvas gives every block one: a chip with nowhere to
                go says so in its own name, which is `Citation`'s behaviour rather than this block's. */}
            <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                <ProposalAnsweringContext value={answering}>
                    <SuggestedAction block={block} />
                </ProposalAnsweringContext>
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('SuggestedAction', () => {
    it('names the step, why it is suggested, and that nothing has happened yet', () => {
        renderSuggestion(suggested());

        expect(screen.getByText('Reply to this conversation')).toBeDefined();
        expect(
            screen.getByText('Why: The decision is due on 28.08 and they have been waiting two days.'),
        ).toBeDefined();
        expect(screen.getByText('MailFathom suggests this. Nothing has happened yet.')).toBeDefined();
    });

    it.each([
        ['ReadsOnly', 'What would change: nothing changes — it only shows you something'],
        ['ChangesMailbox', 'What would change: something in your mailbox changes, and you can change it back'],
        ['SendsMail', 'What would change: mail leaves this deployment, which nothing here can undo'],
    ] as const)('says what taking a %s step would change before anybody agrees', (impact, said) => {
        renderSuggestion(suggested({ impact, requiresConfirmation: impact !== 'ReadsOnly' }));

        expect(screen.getByText(said)).toBeDefined();
    });

    it.each(['SendsMail', 'ChangesMailbox'] as const)(
        'draws a %s step as needing confirmation, and says what the confirmation is for',
        (impact: SuggestedActionImpact) => {
            renderSuggestion(suggested({ impact }));

            expect(screen.getByText('needs confirmation')).toBeDefined();
            expect(screen.getByText('You are asked to confirm this before anything changes.')).toBeDefined();
        },
    );

    it.each(['SendsMail', 'ChangesMailbox'] as const)(
        'draws a %s step that claims it needs no confirming as needing it, the impact deciding that much',
        (impact: SuggestedActionImpact) => {
            // The parser refuses only the sending half of this, which is what the service refuses; a reversible step
            // stating no confirmation reaches the screen, and the screen is what refuses to offer it unasked.
            renderSuggestion(suggested({ impact, requiresConfirmation: false }));

            expect(screen.getByText('needs confirmation')).toBeDefined();
        },
    );

    it('asks for no confirmation where the step only shows somebody something', () => {
        renderSuggestion(suggested({ impact: 'ReadsOnly', requiresConfirmation: false }));

        expect(screen.queryByText('needs confirmation')).toBeNull();
    });

    it('draws a step the run said needs confirming as needing it even where it changes nothing', () => {
        renderSuggestion(suggested({ impact: 'ReadsOnly', requiresConfirmation: true }));

        expect(screen.getByText('needs confirmation')).toBeDefined();
    });

    it('performs nothing, offering the citations and no control that would take the step', () => {
        renderSuggestion(suggested());

        const named = screen.getAllByRole('button').map((control) => control.getAttribute('aria-label'));

        expect(named).toEqual(['Citation 1: Contract addendum — signatures']);
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as a step', () => {
        renderSuggestion({ type: null, named: 'RiskScore' });

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });

    describe('in a conversation', () => {
        it('takes the step or declines it, each named for the step', () => {
            const answer = vi.fn<(decision: AgentProposalDecision) => void>();
            renderSuggestion(suggested(), inConversation('pending', answer));

            fireEvent.click(screen.getByRole('button', { name: 'Do it: Reply to this conversation' }));
            fireEvent.click(screen.getByRole('button', { name: 'Decline: Reply to this conversation' }));

            expect(answer.mock.calls).toEqual([['accepted'], ['declined']]);
        });

        it('stops saying nothing has happened once the step was taken', () => {
            renderSuggestion(suggested(), inConversation('accepted'));

            expect(screen.getByText('Done')).toBeDefined();
            expect(screen.queryByText('MailFathom suggests this. Nothing has happened yet.')).toBeNull();
        });
    });
});
