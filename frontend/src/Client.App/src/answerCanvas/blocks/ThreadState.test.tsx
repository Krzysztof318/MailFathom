// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    AnswerBlock,
    BlockEvidence,
    ConversationStanding,
    DeclaredSource,
    ThreadCommitment,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { AnswerSourcesContext } from '../answerSources';
import { ConversationStandingBody, ThreadState } from './ThreadState';

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

const counterProposal: ThreadCommitment = {
    text: 'Counter-proposal with a 5% cap',
    owedBy: { displayName: 'Karolina Kowalska', address: null },
    dueAt: '2026-08-28T09:00:00+00:00',
    sources: ['c-1'],
};

const standing: ConversationStanding = {
    participants: [
        { displayName: 'Karolina Kowalska', address: null },
        { displayName: 'Anna Kowalska', address: 'anna@contoso.example' },
    ],
    agreements: [{ text: 'SLA cut from 4 h to 2 h', sources: ['c-1'] }],
    openQuestions: [{ text: 'Do we accept a 5% indexation cap?', sources: [] }],
    commitments: [counterProposal],
};

function stood(over: Partial<ConversationStanding> = {}): AnswerBlock {
    return { type: 'threadState', named: 'threadState', evidence: backed, standing: { ...standing, ...over } };
}

function renderStanding(block: AnswerBlock, sources: readonly DeclaredSource[] = [addendum]) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            {/* A zone the suite is not running in, so the hour below proves a commitment falls due on the reader's own
                clock rather than on whichever one the machine reports. */}
            <ReadingZoneContext value="America/New_York">
                {/* A citation is given somewhere to follow to, as the canvas gives every block one: a chip with nowhere
                    to go says so in its own name, which is `Citation`'s behaviour rather than this block's. */}
                <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                    <ThreadState block={block} />
                </AnswerSourcesContext>
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

describe('ThreadState', () => {
    it('separates what was settled from what is open and from what somebody owes', () => {
        renderStanding(stood());

        expect(screen.getByRole('heading', { name: 'Agreed' })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Open questions' })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Commitments' })).toBeDefined();
        expect(screen.getByText('SLA cut from 4 h to 2 h')).toBeDefined();
        expect(screen.getByText('Do we accept a 5% indexation cap?')).toBeDefined();
    });

    it('says how many people are taking part and names each of them for a screen reader', () => {
        renderStanding(stood());

        expect(screen.getByText('2 participants')).toBeDefined();
        expect(screen.getByRole('list', { name: 'Taking part' })).toBeDefined();
        expect(screen.getByText('Anna Kowalska')).toBeDefined();
    });

    it('says who owes a commitment and when it falls due in one sentence', () => {
        renderStanding(stood());

        // The hour is written out rather than compared against a formatter built here, because that comparison passes
        // just as happily for a block that named a zone of its own. New York is four hours behind in August.
        expect(
            screen.getByText(/^Karolina Kowalska — Counter-proposal with a 5% cap · due 8\/28\/26, 5:00/u),
        ).toBeDefined();
    });

    it('states a commitment the correspondence named nobody for without inventing an owner', () => {
        renderStanding(stood({ commitments: [{ ...counterProposal, owedBy: null, dueAt: null }] }));

        expect(screen.getByText('Counter-proposal with a 5% cap')).toBeDefined();
    });

    it('says a due date it could not read was stated rather than dropping it to no date at all', () => {
        renderStanding(stood({ commitments: [{ ...counterProposal, dueAt: 'sometime next spring' }] }));

        expect(
            screen.getByText('Karolina Kowalska — Counter-proposal with a 5% cap · due date not readable'),
        ).toBeDefined();
    });

    it('numbers a source a statement names and the block does not by its place after the block’s own', () => {
        renderStanding(stood({ agreements: [{ text: 'SLA cut from 4 h to 2 h', sources: ['c-2'] }] }), [
            addendum,
            { ...addendum, id: 'c-2', label: 'CPI calculation 2027' },
        ]);

        // The block's own citations are numbered first, so a source only a statement rests on follows them rather than
        // falling before the first one.
        expect(screen.getByRole('button', { name: 'Citation 2: CPI calculation 2027' })).toBeDefined();
    });

    it('draws no participant count where the card draws no participants, the two reading off one emptiness', () => {
        renderStanding(stood({ agreements: [], openQuestions: [], commitments: [] }));

        expect(screen.queryByText('2 participants')).toBeNull();
    });

    it.each([
        ['agreements', 'Nothing settled yet.'],
        ['openQuestions', 'Nothing left open.'],
        ['commitments', 'Nobody undertook anything.'],
    ] as const)('says a list holding no %s holds none, rather than drawing a heading over nothing', (list, said) => {
        renderStanding(stood({ [list]: [] }));

        expect(screen.getByText(said)).toBeDefined();
    });

    it('carries the citation at the statement it was read from', () => {
        renderStanding(stood());

        expect(
            screen.getAllByRole('button', { name: 'Citation 1: Contract addendum — signatures' }).length,
        ).toBeGreaterThan(0);
    });

    it('says a conversation that holds none of the three holds none of them', () => {
        renderStanding(stood({ agreements: [], openQuestions: [], commitments: [] }));

        expect(
            screen.getByText('This conversation holds no agreements, open questions or commitments yet.'),
        ).toBeDefined();
    });

    it('names a block it is not the renderer for rather than drawing somebody else’s data as a standing', () => {
        renderStanding({ type: null, named: 'RiskScore' });

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });

    it('draws the standing outside the card as well, which is what lets the mail space host it', () => {
        const declared = new Map([[addendum.id, addendum]]);

        render(
            <LocalizationProvider>
                <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                    <ConversationStandingBody citations={backed.citations} standing={standing} />
                </AnswerSourcesContext>
            </LocalizationProvider>,
        );

        expect(screen.getByText('SLA cut from 4 h to 2 h')).toBeDefined();
        expect(screen.queryByRole('article')).toBeNull();
    });
});
