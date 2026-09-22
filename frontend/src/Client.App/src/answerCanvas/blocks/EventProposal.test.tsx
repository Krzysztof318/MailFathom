// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AgentProposalDecision, AnswerBlock, BlockEvidence, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { AnswerSourcesContext } from '../answerSources';
import { ProposalAnsweringContext, type ProposalAnswering } from '../proposalAnswering';
import { EventProposal } from './EventProposal';

const lease: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Hall lease — renewal',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

function proposed(overrides: Partial<Extract<AnswerBlock, { type: 'eventProposal' }>['proposal']> = {}): AnswerBlock {
    return {
        type: 'eventProposal',
        named: 'eventProposal',
        evidence: backed,
        proposal: {
            title: 'Renewal call',
            start: '2026-09-23T09:00:00+00:00',
            end: '2026-09-23T10:30:00+00:00',
            isAllDay: false,
            ...overrides,
        },
    };
}

function drawEvent(block: AnswerBlock, answering: ProposalAnswering | null = null) {
    return render(
        <LocalizationProvider>
            <ReadingZoneContext value="Europe/Warsaw">
                <AnswerSourcesContext value={{ sources: new Map([[lease.id, lease]]), follow: null }}>
                    <ProposalAnsweringContext value={answering}>
                        <EventProposal block={block} />
                    </ProposalAnsweringContext>
                </AnswerSourcesContext>
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

function pending(overrides: Partial<ProposalAnswering> = {}): ProposalAnswering {
    return {
        phase: 'pending',
        answering: false,
        refused: null,
        answer: () => undefined,
        askAnotherTime: () => undefined,
        look: null,
        ...overrides,
    };
}

describe('EventProposal', () => {
    it('shows when it would be, for how long, and the thread it came out of, read in the reader’s own zone', () => {
        drawEvent(proposed());

        expect(screen.getByText('Renewal call')).toBeDefined();
        expect(screen.getByText('September 23, 2026, 11:00 AM – 12:30 PM')).toBeDefined();
        expect(screen.getByText('90 minutes')).toBeDefined();
        expect(screen.getByText('Hall lease — renewal').getAttribute('lang')).toBe('');
    });

    it('words a whole-day proposal as the day alone, without an hour nobody chose', () => {
        drawEvent(proposed({ start: '2026-09-22T22:00:00+00:00', end: '2026-09-23T22:00:00+00:00', isAllDay: true }));

        expect(screen.getByText('Wednesday, September 23, 2026')).toBeDefined();
        expect(screen.getByText('1 day')).toBeDefined();
    });

    it('says nothing about how long it lasts where the proposal states no end', () => {
        drawEvent(proposed({ end: null }));

        expect(screen.queryByText('Duration')).toBeNull();
    });

    it('adds it, asks for another time, or declines it, each named for the event it acts on', () => {
        const answer = vi.fn<(decision: AgentProposalDecision) => void>();
        const askAnotherTime = vi.fn<(title: string) => void>();
        drawEvent(proposed(), pending({ answer, askAnotherTime }));

        fireEvent.click(
            // The range is spelled by `Intl`, which puts narrow spaces around the hour that an accessible name keeps.
            screen.getByRole('button', {
                name: /^Add to calendar: “Renewal call”, September 23, 2026, 11:00\sAM\s–\s12:30\sPM$/,
            }),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Ask for another time for “Renewal call”' }));
        fireEvent.click(screen.getByRole('button', { name: 'Decline putting “Renewal call” in the calendar' }));

        expect(answer.mock.calls).toEqual([['accepted'], ['declined']]);
        expect(askAnotherTime).toHaveBeenCalledWith('Renewal call');
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as an event', () => {
        drawEvent({
            type: 'taskProposal',
            named: 'taskProposal',
            evidence: backed,
            proposal: { title: 'Send the schedule', dueOn: null },
        });

        expect(screen.getByText('Unknown block')).toBeDefined();
    });
});
