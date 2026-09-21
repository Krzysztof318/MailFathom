// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AnswerBlock, BlockEvidence, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../../localization/Localization';
import { AnswerSourcesContext } from '../answerSources';
import { SynthesizedAnswer } from './SynthesizedAnswer';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

const addendum: DeclaredSource = { ...agreement, id: 'c-2', label: 'SLA addendum.pdf' };

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

function answering(evidence: BlockEvidence, confidence: 'High' | 'Moderate' | 'Low' = 'High'): AnswerBlock {
    return {
        type: 'answer',
        named: 'answer',
        evidence,
        answer: { text: 'The rate was agreed in April 2021.', confidence },
    };
}

function renderAnswer(block: AnswerBlock, sources: readonly DeclaredSource[] = [agreement]) {
    const declared = new Map(sources.map((source) => [source.id, source]));

    return render(
        <LocalizationProvider>
            <AnswerSourcesContext value={{ sources: declared, follow: null }}>
                <SynthesizedAnswer block={block} />
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

describe('SynthesizedAnswer', () => {
    it('draws the answer in the words the run wrote it in', () => {
        renderAnswer(answering(backed));

        expect(screen.getByText('The rate was agreed in April 2021.')).toBeDefined();
    });

    it.each([
        ['High', 'high confidence'],
        ['Moderate', 'moderate confidence'],
        ['Low', 'low confidence'],
    ] as const)('says the %s band in the form the contract defines rather than as a number', (band, said) => {
        renderAnswer(answering(backed, band));

        expect(screen.getByText(said)).toBeDefined();
    });

    // Each verdict is paired with its own sentence rather than counted, because a transposition between two of them is
    // a reader told a fact is backed when the sources disagree about it.
    it.each([
        ['Supported', 'supported', 'The correspondence backs this, from sources that were current when it was read.'],
        [
            'Unsupported',
            'unsupported',
            'Nothing this run found backs this — treat it as a reading rather than as a fact.',
        ],
        ['Stale', 'outdated', 'This is backed, and the local copy behind it is known to be behind the mail server.'],
        [
            'Conflicting',
            'conflicts with another source',
            'The sources disagree. Both sides are below, each with the sources that say it.',
        ],
    ] as const)('draws a %s answer as that state where the answer is', (support, chip, note) => {
        renderAnswer(answering({ ...backed, support }));

        expect(screen.getByText(chip)).toBeDefined();
        expect(screen.getByText(note)).toBeDefined();
    });

    it('draws both sides of a disagreement rather than resolving them into one sentence', () => {
        renderAnswer(
            answering({
                ...backed,
                support: 'Conflicting',
                citations: ['c-1', 'c-2'],
                conflictingClaims: [
                    { statement: 'The rate is 1 200', sources: ['c-1'] },
                    { statement: 'The rate is 1 350', sources: ['c-2'] },
                ],
            }),
            [agreement, addendum],
        );

        expect(screen.getByText('The rate is 1 200')).toBeDefined();
        expect(screen.getByText('The rate is 1 350')).toBeDefined();
    });

    it('attaches each source as a control naming what it is, not as a bare number', () => {
        renderAnswer(answering({ ...backed, citations: ['c-1', 'c-2'] }), [agreement, addendum]);

        expect(screen.getByRole('button', { name: /Citation 1: Master agreement\.pdf/u })).toBeDefined();
        expect(screen.getByRole('button', { name: /Citation 2: SLA addendum\.pdf/u })).toBeDefined();
    });

    it('says how many sources the answer rests on', () => {
        renderAnswer(answering({ ...backed, citations: ['c-1', 'c-2'] }), [agreement, addendum]);

        expect(screen.getByText('2 citations')).toBeDefined();
    });

    it('names a block registered under the wrong type rather than drawing somebody else’s data as an answer', () => {
        renderAnswer({ type: 'threadState', named: 'threadState' });

        expect(screen.getByText('type: threadState')).toBeDefined();
    });
});
