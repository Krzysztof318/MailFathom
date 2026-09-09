// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { parseEnrichment } from './mailEnrichment';

const mark = {
    aspect: 'Sense',
    text: 'An invoice for August.',
    reason: 'The message attaches a document the sender calls an invoice.',
    dueAt: null,
    source: 'DeterministicRule',
    origin: 'rules/invoice',
    evidence: ['fragment-1', 'fragment-2'],
};

// A copy of a record with one field left out, which is how a body written before a field existed is stated.
function without(record: Record<string, unknown>, field: string): Record<string, unknown> {
    return Object.fromEntries(Object.entries(record).filter(([named]) => named !== field));
}

function derivation(marks: readonly unknown[]): unknown {
    return { derivedAt: '2026-08-31T09:41:00+00:00', marks };
}

describe('parseEnrichment', () => {
    it('reads a message no derivation has reached as carrying none', () => {
        expect(parseEnrichment(null)).toEqual({ enrichment: null });
        expect(parseEnrichment(undefined)).toEqual({ enrichment: null });
    });

    it('keeps a derivation that settled with nothing to say apart from one that never ran', () => {
        expect(parseEnrichment(derivation([]))).toEqual({
            enrichment: { derivedAt: '2026-08-31T09:41:00+00:00', marks: [] },
        });
    });

    it('reads every field of a mark, in the order the deployment named them', () => {
        const commitment = {
            ...mark,
            aspect: 'Commitment',
            text: 'An answer is owed by Friday.',
            dueAt: '2026-09-04T16:00:00+00:00',
            source: 'Model',
            origin: 'agents/reader',
            evidence: ['fragment-3'],
        };

        expect(parseEnrichment(derivation([mark, commitment]))).toEqual({
            enrichment: {
                derivedAt: '2026-08-31T09:41:00+00:00',
                marks: [
                    {
                        aspect: 'Sense',
                        text: 'An invoice for August.',
                        reason: 'The message attaches a document the sender calls an invoice.',
                        dueAt: null,
                        source: 'DeterministicRule',
                        origin: 'rules/invoice',
                        evidence: ['fragment-1', 'fragment-2'],
                    },
                    {
                        aspect: 'Commitment',
                        text: 'An answer is owed by Friday.',
                        reason: 'The message attaches a document the sender calls an invoice.',
                        dueAt: '2026-09-04T16:00:00+00:00',
                        source: 'Model',
                        origin: 'agents/reader',
                        evidence: ['fragment-3'],
                    },
                ],
            },
        });
    });

    it('reads a mark that named no passage as resting on none', () => {
        const parsed = parseEnrichment(derivation([{ ...mark, evidence: [] }]));

        expect(parsed?.enrichment?.marks[0]?.evidence).toEqual([]);
    });

    it('reads an omitted due date as none', () => {
        const parsed = parseEnrichment(derivation([without(mark, 'dueAt')]));

        expect(parsed?.enrichment?.marks[0]?.dueAt).toBeNull();
    });

    it('refuses a field that is not a derivation at all', () => {
        expect(parseEnrichment('derived')).toBeNull();
        expect(parseEnrichment([])).toBeNull();
    });

    it('refuses a derivation that does not say when it ran', () => {
        expect(parseEnrichment({ marks: [] })).toBeNull();
        expect(parseEnrichment({ derivedAt: '', marks: [] })).toBeNull();
        expect(parseEnrichment({ derivedAt: 17, marks: [] })).toBeNull();
    });

    it('refuses a derivation whose marks are not a list', () => {
        expect(parseEnrichment({ derivedAt: '2026-08-31T09:41:00+00:00' })).toBeNull();
        expect(parseEnrichment({ derivedAt: '2026-08-31T09:41:00+00:00', marks: {} })).toBeNull();
    });

    it('refuses more marks than a message may carry', () => {
        const aspects = ['Sense', 'Significance', 'Commitment', 'Sense'];

        expect(parseEnrichment(derivation(aspects.map((aspect) => ({ ...mark, aspect }))))).toBeNull();
    });

    it('refuses a message answering twice for one aspect', () => {
        expect(parseEnrichment(derivation([mark, { ...mark, text: 'A second reading.' }]))).toBeNull();
    });

    it('refuses a reading of something other than the three aspects', () => {
        expect(parseEnrichment(derivation([{ ...mark, aspect: 'Tone' }]))).toBeNull();
    });

    it('refuses a mark produced by something other than a rule or a model', () => {
        expect(parseEnrichment(derivation([{ ...mark, source: 'Guess' }]))).toBeNull();
    });

    it('refuses a reading with no sentence in it', () => {
        expect(parseEnrichment(derivation([{ ...mark, text: '' }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, reason: '' }]))).toBeNull();
    });

    it('refuses a sentence longer than the deployment stores', () => {
        expect(parseEnrichment(derivation([{ ...mark, text: 'a'.repeat(241) }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, reason: 'a'.repeat(241) }]))).toBeNull();
    });

    it('refuses a mark that does not say what produced it', () => {
        expect(parseEnrichment(derivation([{ ...mark, origin: '' }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, origin: 'a'.repeat(129) }]))).toBeNull();
    });

    it('refuses a due date on a reading that is not a commitment', () => {
        expect(parseEnrichment(derivation([{ ...mark, dueAt: '2026-09-04T16:00:00+00:00' }]))).toBeNull();
    });

    it('refuses a due date that is not an instant', () => {
        expect(parseEnrichment(derivation([{ ...mark, aspect: 'Commitment', dueAt: 17 }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, aspect: 'Commitment', dueAt: '' }]))).toBeNull();
    });

    it('refuses a mark that does not say what it rests on', () => {
        expect(parseEnrichment(derivation([without(mark, 'evidence')]))).toBeNull();
    });

    it('refuses more passages than a mark may rest on', () => {
        expect(parseEnrichment(derivation([{ ...mark, evidence: ['a', 'b', 'c', 'd', 'e'] }]))).toBeNull();
    });

    it('refuses a passage identity that names nothing', () => {
        expect(parseEnrichment(derivation([{ ...mark, evidence: [''] }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, evidence: [17] }]))).toBeNull();
        expect(parseEnrichment(derivation([{ ...mark, evidence: ['a'.repeat(257)] }]))).toBeNull();
    });

    it('refuses a mark that is not a record', () => {
        expect(parseEnrichment(derivation(['Sense']))).toBeNull();
    });
});
