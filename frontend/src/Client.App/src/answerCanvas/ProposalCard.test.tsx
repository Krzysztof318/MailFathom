// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AgentProposalDecision, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { AnswerSourcesContext } from './answerSources';
import { ProposalCard, type ProposalControl, type ProposalKind } from './ProposalCard';
import {
    ProposalAnsweringContext,
    type ProposalAnswering,
    type ProposalLook,
    type ProposalPhase,
} from './proposalAnswering';
import type { AnswerBlockState } from './answerBlocks';

const thread: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Hall lease — renewal',
    medium: 'Written',
    unreadable: null,
};

const label = 'Event proposal';
const body = 'What the proposal says';
const title = 'Renewal call';

function answeringIn(phase: ProposalPhase, overrides: Partial<ProposalAnswering> = {}): ProposalAnswering {
    return {
        phase,
        answering: false,
        refused: null,
        answer: () => undefined,
        askAnotherTime: () => undefined,
        look: () => undefined,
        ...overrides,
    };
}

function drawCard({
    answering = null,
    kind = 'eventProposal',
    state = 'ready',
    onRetry,
    controls,
    quotesMail = false,
}: {
    readonly answering?: ProposalAnswering | null;
    readonly kind?: ProposalKind;
    readonly state?: AnswerBlockState;
    readonly onRetry?: () => void;
    readonly controls?: readonly ProposalControl[];
    readonly quotesMail?: boolean;
} = {}) {
    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: new Map([[thread.id, thread]]), follow: null }}>
                <ProposalAnsweringContext value={answering}>
                    <ProposalCard
                        citations={['c-1']}
                        controls={
                            controls ?? [
                                {
                                    said: 'Add to calendar',
                                    name: 'Add to calendar: “Renewal call”',
                                    primary: true,
                                    run: () => undefined,
                                },
                            ]
                        }
                        kind={kind}
                        label={label}
                        quotesMail={quotesMail}
                        state={state}
                        title={title}
                        onRetry={onRetry}
                    >
                        <p>{body}</p>
                    </ProposalCard>
                </ProposalAnsweringContext>
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

function card(): HTMLElement {
    return screen.getByRole('article', { name: 'Event proposal' });
}

describe('ProposalCard', () => {
    it('says a pending proposal needs confirmation and offers the controls its renderer states', () => {
        drawCard({ answering: answeringIn('pending') });

        expect(within(card()).getByText('needs confirmation')).toBeDefined();
        expect(within(card()).getByRole('button', { name: 'Add to calendar: “Renewal call”' })).toBeDefined();
    });

    it('draws the same card read-only where the surface offers no way to answer it: the chip, and nothing to press', () => {
        drawCard();

        expect(within(card()).getByText('needs confirmation')).toBeDefined();
        expect(within(card()).queryAllByRole('button')).toEqual([]);
        expect(within(card()).getByText('What the proposal says')).toBeDefined();
    });

    it('drops the controls once accepted, and says it was done instead', () => {
        drawCard({ answering: answeringIn('accepted') });

        expect(within(card()).getByText('done')).toBeDefined();
        expect(within(card()).getByText('Added to the calendar')).toBeDefined();
        expect(within(card()).queryByRole('button', { name: /^Add to calendar/ })).toBeNull();
    });

    it('offers only trying again once accepting failed, which accepts it again', () => {
        const answer = vi.fn<(decision: AgentProposalDecision) => void>();
        drawCard({ answering: answeringIn('failed', { answer }) });

        expect(within(card()).getByText('not done')).toBeDefined();
        expect(within(card()).getByText('Could not add it to the calendar.')).toBeDefined();
        expect(within(card()).queryByRole('button', { name: /^Add to calendar/ })).toBeNull();

        fireEvent.click(within(card()).getByRole('button', { name: 'Try again: Renewal call' }));

        expect(answer).toHaveBeenCalledWith('accepted');
    });

    it('collapses a declined proposal to one struck line, taking its body and the rows leading away with it', () => {
        drawCard({ answering: answeringIn('declined') });

        expect(within(card()).getByText('declined')).toBeDefined();
        expect(within(card()).getByText('Renewal call').tagName).toBe('S');
        expect(within(card()).getByText('Declined — nothing was done')).toBeDefined();
        expect(within(card()).queryByText('What the proposal says')).toBeNull();
        expect(within(card()).queryAllByRole('button')).toEqual([]);
    });

    it('reads a struck title quoted out of mail in the language it was written in', () => {
        drawCard({ answering: answeringIn('declined'), quotesMail: true });

        expect(within(card()).getByText('Renewal call').getAttribute('lang')).toBe('');
    });

    it('holds every control while a decision is on its way', () => {
        drawCard({ answering: answeringIn('pending', { answering: true }) });

        expect(
            within(card()).getByRole('button', { name: 'Add to calendar: “Renewal call”' }).hasAttribute('disabled'),
        ).toBe(true);
    });

    it('says why a decision was not taken', () => {
        drawCard({ answering: answeringIn('pending', { refused: 'missing' }) });

        expect(within(card()).getByRole('alert').textContent).toBe(
            'This proposal was already answered, or it is no longer there.',
        );
    });

    it('does nothing for a control held because pressing it could not do what it says', () => {
        const run = vi.fn();
        drawCard({
            answering: answeringIn('pending'),
            controls: [
                { said: 'Send', name: 'Send the draft to anna@contoso.example', primary: true, held: true, run },
            ],
        });

        const send = within(card()).getByRole('button', { name: 'Send the draft to anna@contoso.example' });
        fireEvent.click(send);

        expect(send.getAttribute('aria-disabled')).toBe('true');
        expect(run).not.toHaveBeenCalled();
    });

    it('leads an event to the calendar alone, saying it leaves the conversation', () => {
        const look = vi.fn<(where: ProposalLook) => void>();
        drawCard({ answering: answeringIn('pending', { look }) });

        fireEvent.click(
            within(card()).getByRole('button', {
                name: 'Open the calendar — leaves the conversation, you can come straight back',
            }),
        );

        expect(look.mock.calls).toEqual([[{ kind: 'calendar' }]]);
        expect(within(card()).queryByRole('button', { name: /^Open the thread/ })).toBeNull();
    });

    it('leads anything else to the thread it came from, saying it leaves the conversation', () => {
        const look = vi.fn<(where: ProposalLook) => void>();
        drawCard({ kind: 'taskProposal', answering: answeringIn('pending', { look }) });

        fireEvent.click(
            within(card()).getByRole('button', {
                name: 'Open the thread — leaves the conversation, you can come straight back',
            }),
        );

        expect(look.mock.calls).toEqual([[{ kind: 'thread', email: thread.target?.email }]]);
        expect(within(card()).queryByRole('button', { name: /^Open the calendar/ })).toBeNull();
    });

    it('leads nowhere where the surface has nowhere to open anything', () => {
        drawCard({ answering: answeringIn('pending', { look: null }) });

        expect(within(card()).queryByRole('button', { name: /^Open the/ })).toBeNull();
    });

    it.each<[ProposalKind, string]>([
        ['eventProposal', 'No free slot long enough this week — nothing to propose.'],
        ['taskProposal', 'Nothing in this thread carries a date or an owner.'],
        ['draft', 'Nothing to draft — the last message in the thread is yours.'],
        ['suggestedAction', 'Nothing worth doing here — the thread needs no step.'],
    ])('says in its own words why a %s has nothing to propose', (kind, said) => {
        drawCard({ kind, state: 'empty' });

        expect(within(card()).getByText(said)).toBeDefined();
    });

    it.each<[ProposalKind, string]>([
        ['eventProposal', 'Could not build this proposal. The calendar did not answer.'],
        ['taskProposal', 'Could not build this proposal. The task list did not answer.'],
        ['draft', 'Could not build this proposal. The model timed out.'],
        ['suggestedAction', 'Could not build this proposal. The model timed out.'],
    ])('says in its own words why a %s could not be built, and offers trying again', (kind, said) => {
        const onRetry = vi.fn();
        drawCard({ kind, state: 'error', onRetry });

        expect(within(card()).getByText(said)).toBeDefined();

        fireEvent.click(within(card()).getByRole('button', { name: 'Try again: Event proposal' }));

        expect(onRetry).toHaveBeenCalledOnce();
    });
});
