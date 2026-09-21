// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    parseBlockEvidence,
    parseDeclaredSource,
    parseEvidenceEntries,
    parseSynthesizedAnswer,
} from './presentationBlocks';

const current = { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' };

function evidence(over: Readonly<Record<string, unknown>> = {}): Readonly<Record<string, unknown>> {
    return { support: 'Supported', citations: ['c-1'], freshness: current, ...over };
}

function source(over: Readonly<Record<string, unknown>> = {}): Readonly<Record<string, unknown>> {
    return {
        id: 'c-1',
        target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
        label: 'Master agreement.pdf',
        medium: 'Written',
        ...over,
    };
}

describe('parseBlockEvidence', () => {
    it('reads what the correspondence does for a block', () => {
        expect(parseBlockEvidence(evidence())).toEqual({
            support: 'Supported',
            citations: ['c-1'],
            freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
            conflictingClaims: [],
        });
    });

    it('reads both sides of a disagreement, each with the sources that say it', () => {
        const parsed = parseBlockEvidence(
            evidence({
                support: 'Conflicting',
                citations: ['c-1', 'c-2'],
                conflictingClaims: [
                    { statement: 'The rate is 1 200', sources: ['c-1'] },
                    { statement: 'The rate is 1 350', sources: ['c-2'] },
                ],
            }),
        );

        expect(parsed?.conflictingClaims.map((claim) => claim.statement)).toEqual([
            'The rate is 1 200',
            'The rate is 1 350',
        ]);
    });

    it('refuses a side of a disagreement that names no source, which is not a side', () => {
        expect(
            parseBlockEvidence(
                evidence({
                    support: 'Conflicting',
                    conflictingClaims: [{ statement: 'Nobody said this', sources: [] }],
                }),
            ),
        ).toBeNull();
    });

    it('refuses a side naming a source the block does not rest on, which nothing could number', () => {
        expect(
            parseBlockEvidence(
                evidence({
                    support: 'Conflicting',
                    citations: ['c-1'],
                    conflictingClaims: [{ statement: 'Somebody else said this', sources: ['c-9'] }],
                }),
            ),
        ).toBeNull();
    });

    it('refuses a block naming one source twice, which is two numbers for one source', () => {
        expect(parseBlockEvidence(evidence({ citations: ['c-1', 'c-1'] }))).toBeNull();
    });

    it('refuses a side naming one source twice, which would draw it once and lose the other', () => {
        expect(
            parseBlockEvidence(
                evidence({
                    support: 'Conflicting',
                    citations: ['c-1', 'c-2'],
                    conflictingClaims: [
                        { statement: 'The rate is 1 200', sources: ['c-1', 'c-1'] },
                        { statement: 'The rate is 1 350', sources: ['c-2'] },
                    ],
                }),
            ),
        ).toBeNull();
    });

    it('refuses more sides of a disagreement than one block may present', () => {
        const conflictingClaims = Array.from({ length: 7 }, (_, at) => ({
            statement: `Side ${String(at)}`,
            sources: ['c-1'],
        }));

        expect(
            parseBlockEvidence(evidence({ support: 'Conflicting', citations: ['c-1', 'c-2'], conflictingClaims })),
        ).toBeNull();
    });

    it.each([
        ['a supported block backed by nothing', { citations: [] }],
        ['a supported block read from a copy known to be behind', { freshness: { staleness: 'Stale' } }],
        ['an unsupported block naming the source it is supposed not to have', { support: 'Unsupported' }],
        [
            'a settled block presenting sides of a disagreement',
            { conflictingClaims: [{ statement: 'A', sources: ['c-1'] }] },
        ],
        [
            'a conflict presented as one side',
            {
                support: 'Conflicting',
                citations: ['c-1', 'c-2'],
                conflictingClaims: [{ statement: 'A', sources: ['c-1'] }],
            },
        ],
    ])('refuses %s, which says two things at once', (_case, over) => {
        expect(parseBlockEvidence(evidence(over))).toBeNull();
    });

    it('refuses a verdict this contract does not carry rather than reading it as a settled one', () => {
        expect(parseBlockEvidence(evidence({ support: 'ProbablyFine' }))).toBeNull();
    });

    it('refuses more sources than a block may rest on', () => {
        const citations = Array.from({ length: 25 }, (_, at) => `c-${String(at)}`);

        expect(parseBlockEvidence(evidence({ citations }))).toBeNull();
    });

    it('reads a freshness nothing established, which carries no time', () => {
        expect(parseBlockEvidence(evidence({ freshness: { staleness: 'Unknown' } }))?.freshness).toEqual({
            staleness: 'Unknown',
            observedAt: null,
        });
    });
});

describe('parseDeclaredSource', () => {
    it('reads the name a block refers to a source by and what it is called', () => {
        expect(parseDeclaredSource(source())).toEqual({
            id: 'c-1',
            kind: 'email',
            label: 'Master agreement.pdf',
            medium: 'Written',
            unreadable: null,
        });
    });

    it('reads why a source yielded nothing', () => {
        expect(parseDeclaredSource(source({ unreadable: 'Encrypted' }))?.unreadable).toBe('Encrypted');
    });

    it('refuses a reason this contract does not carry rather than reading the source as readable', () => {
        expect(parseDeclaredSource(source({ unreadable: 'Locked' }))).toBeNull();
    });

    it('leaves a target kind this contract does not carry unnamed rather than refusing the source', () => {
        expect(parseDeclaredSource(source({ target: { kind: 'calendar' } }))?.kind).toBeNull();
    });

    it('refuses a source naming no target at all, which is a citation nothing can be followed to', () => {
        expect(parseDeclaredSource(source({ target: undefined }))).toBeNull();
    });
});

describe('parseSynthesizedAnswer', () => {
    it('reads the words and the band they are worth', () => {
        expect(parseSynthesizedAnswer({ text: 'The rate was agreed.', confidence: 'Moderate' })).toEqual({
            text: 'The rate was agreed.',
            confidence: 'Moderate',
        });
    });

    it('refuses an answer with no words, which is not an answer', () => {
        expect(parseSynthesizedAnswer({ text: '', confidence: 'High' })).toBeNull();
    });

    it('refuses a band this contract does not define rather than reading it as the middle one', () => {
        expect(parseSynthesizedAnswer({ text: 'Something', confidence: 'Certain' })).toBeNull();
    });
});

describe('parseEvidenceEntries', () => {
    const entry = {
        source: 'c-1',
        fragment: 'Monthly remuneration is EUR 1,200 net',
        relevance: 0.94,
        freshness: current,
    };

    it('reads each message with the part of it worth reading', () => {
        expect(parseEvidenceEntries([entry])).toEqual([
            {
                source: 'c-1',
                fragment: 'Monthly remuneration is EUR 1,200 net',
                relevance: 0.94,
                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
            },
        ]);
    });

    it('reads a list holding nothing as a list holding nothing rather than as a defect', () => {
        expect(parseEvidenceEntries([])).toEqual([]);
    });

    it.each([-0.1, 1.1, Number.NaN])('refuses a relevance of %s, which is not a share of anything', (relevance) => {
        expect(parseEvidenceEntries([{ ...entry, relevance }])).toBeNull();
    });

    it('refuses more messages than one list may hold', () => {
        expect(parseEvidenceEntries(Array.from({ length: 51 }, () => entry))).toBeNull();
    });

    it('refuses an entry quoting nothing, the fragment being what makes it checkable', () => {
        expect(parseEvidenceEntries([{ ...entry, fragment: '' }])).toBeNull();
    });
});
