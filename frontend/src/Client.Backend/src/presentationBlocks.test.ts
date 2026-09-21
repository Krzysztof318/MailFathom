// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    parseAttachmentEntries,
    parseBlockEvidence,
    parseDeclaredSource,
    parseEvidenceEntries,
    parseFactTableColumns,
    parseFactTableRows,
    parseSynthesizedAnswer,
    parseTimelineEntries,
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

describe('parseTimelineEntries', () => {
    const event = {
        occurredAt: '2021-04-12T00:00:00+00:00',
        summary: 'Monthly remuneration set at EUR 1,200',
        subject: 'Master agreement',
        sources: ['c-1'],
    };

    it('reads each event with what happened, what it happened to, and when', () => {
        expect(parseTimelineEntries([event])).toEqual([
            {
                occurredAt: '2021-04-12T00:00:00+00:00',
                summary: 'Monthly remuneration set at EUR 1,200',
                subject: 'Master agreement',
                sources: ['c-1'],
            },
        ]);
    });

    it('keeps the order the run composed rather than sorting by date', () => {
        const later = { ...event, occurredAt: '2025-11-18T00:00:00+00:00', subject: 'SLA addendum' };

        expect(parseTimelineEntries([later, event])?.map((entry) => entry.subject)).toEqual([
            'SLA addendum',
            'Master agreement',
        ]);
    });

    it('reads an event nothing backs, which is how it stands beside events that are backed', () => {
        expect(parseTimelineEntries([{ ...event, sources: [] }])?.[0]?.sources).toEqual([]);
    });

    it('reads a chronology holding nothing as one holding nothing rather than as a defect', () => {
        expect(parseTimelineEntries([])).toEqual([]);
    });

    it('refuses an event naming one source twice, which is two numbers for one source', () => {
        expect(parseTimelineEntries([{ ...event, sources: ['c-1', 'c-1'] }])).toBeNull();
    });

    it('refuses an event with no date, a chronology being what the dates make it', () => {
        expect(parseTimelineEntries([{ ...event, occurredAt: '' }])).toBeNull();
    });

    it('refuses more events than one timeline may hold', () => {
        expect(parseTimelineEntries(Array.from({ length: 51 }, () => event))).toBeNull();
    });
});

describe('parseFactTableColumns', () => {
    it('reads the columns compared across, in the order they are drawn', () => {
        expect(parseFactTableColumns(['version', 'amount'])).toEqual(['version', 'amount']);
    });

    it('refuses a column the catalogue does not hold, which nothing could draw a heading for', () => {
        expect(parseFactTableColumns(['version', 'likelihood'])).toBeNull();
    });

    it('refuses one column compared across twice, which is two headings a reader cannot tell apart', () => {
        expect(parseFactTableColumns(['version', 'version'])).toBeNull();
    });

    it('refuses more columns than one table may compare across', () => {
        expect(parseFactTableColumns(Array.from({ length: 9 }, () => 'amount'))).toBeNull();
    });
});

describe('parseFactTableRows', () => {
    const row = {
        cells: [
            { value: 'Agreement 2021', sources: ['c-1'] },
            { value: 'EUR 1,200', sources: ['c-1'] },
        ],
    };

    it('reads each cell with the value as the correspondence wrote it and what it rests on', () => {
        expect(parseFactTableRows([row], 2)).toEqual([
            {
                cells: [
                    { value: 'Agreement 2021', sources: ['c-1'] },
                    { value: 'EUR 1,200', sources: ['c-1'] },
                ],
            },
        ]);
    });

    it('reads a cell the correspondence says nothing about as holding no value at all', () => {
        const silent = { cells: [row.cells[0], { value: null, sources: [] }] };

        expect(parseFactTableRows([silent], 2)?.[0]?.cells[1]).toEqual({ value: null, sources: [] });
    });

    it('refuses a cell with no value that names a source, which would be citing an absence', () => {
        const cited = { cells: [row.cells[0], { value: null, sources: ['c-1'] }] };

        expect(parseFactTableRows([cited], 2)).toBeNull();
    });

    it('refuses a row whose cell count disagrees with the header, which is a comparison nobody can trust', () => {
        expect(parseFactTableRows([row], 3)).toBeNull();
    });

    it('refuses more rows than one table may hold', () => {
        expect(
            parseFactTableRows(
                Array.from({ length: 51 }, () => row),
                2,
            ),
        ).toBeNull();
    });
});

describe('parseAttachmentEntries', () => {
    const file = {
        source: 'c-1',
        name: 'Master agreement.pdf',
        mediaType: 'application/pdf',
        sizeOctets: 312_000,
        availability: 'Stored',
    };

    it('reads each file with what it is, how large it is, and whether it can be opened', () => {
        expect(parseAttachmentEntries([file])).toEqual([
            {
                source: 'c-1',
                name: 'Master agreement.pdf',
                mediaType: 'application/pdf',
                sizeOctets: 312_000,
                availability: 'Stored',
            },
        ]);
    });

    it('reads a file the message declared no type for rather than refusing it', () => {
        expect(parseAttachmentEntries([{ ...file, mediaType: null }])?.[0]?.mediaType).toBeNull();
    });

    it('reads the size of a file whose content was never stored, the size being the message’s own account', () => {
        expect(parseAttachmentEntries([{ ...file, availability: 'NotStored' }])?.[0]?.sizeOctets).toBe(312_000);
    });

    it('refuses an availability this contract does not carry rather than reading it as openable', () => {
        expect(parseAttachmentEntries([{ ...file, availability: 'Quarantined' }])).toBeNull();
    });

    it('refuses a size that is not a count of octets', () => {
        expect(parseAttachmentEntries([{ ...file, sizeOctets: -1 }])).toBeNull();
    });

    it('refuses a file with no name, the name being what somebody chooses it by', () => {
        expect(parseAttachmentEntries([{ ...file, name: '' }])).toBeNull();
    });

    it('refuses more files than one gallery may hold', () => {
        expect(parseAttachmentEntries(Array.from({ length: 51 }, () => file))).toBeNull();
    });
});
