// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailEnrichmentAspect, MailEnrichmentMark } from '@mailfathom/client-backend';
import { leadingReading, readingNames, readingsOf, readingSources } from './messageReadings';

function mark(aspect: MailEnrichmentAspect): MailEnrichmentMark {
    return {
        aspect,
        text: `A reading of ${aspect}.`,
        reason: 'Because the message says so.',
        dueAt: null,
        source: 'DeterministicRule',
        origin: 'rules/one',
        evidence: [],
    };
}

function derived(marks: readonly MailEnrichmentMark[]) {
    return { derivedAt: '2026-08-31T09:41:00+00:00', marks };
}

describe('readingsOf', () => {
    it('reads a message no derivation reached as carrying no reading', () => {
        expect(readingsOf(null)).toStrictEqual([]);
    });

    it('reads a derivation that settled with nothing to say as carrying no reading', () => {
        expect(readingsOf(derived([]))).toStrictEqual([]);
    });

    it('puts what is owed first, then why the message matters, then what it is about', () => {
        const readings = readingsOf(derived([mark('Sense'), mark('Commitment'), mark('Significance')]));

        expect(readings.map((one) => one.aspect)).toStrictEqual(['Commitment', 'Significance', 'Sense']);
    });

    it('leaves what the deployment answered untouched', () => {
        const marks = [mark('Sense'), mark('Commitment')];

        readingsOf(derived(marks));

        expect(marks.map((one) => one.aspect)).toStrictEqual(['Sense', 'Commitment']);
    });
});

describe('leadingReading', () => {
    it('draws nothing for a message with no reading', () => {
        expect(leadingReading(null)).toBeNull();
        expect(leadingReading(derived([]))).toBeNull();
    });

    it('draws the most actionable reading rather than the first one answered', () => {
        expect(leadingReading(derived([mark('Sense'), mark('Commitment')]))?.aspect).toBe('Commitment');
    });

    it('draws the only reading a message carries whichever it is', () => {
        expect(leadingReading(derived([mark('Sense')]))?.aspect).toBe('Sense');
    });
});

describe('what a reading and its producer are called', () => {
    it('names every aspect the deployment publishes', () => {
        expect(Object.keys(readingNames)).toStrictEqual(['Sense', 'Significance', 'Commitment']);
    });

    it('keeps a rule and a model apart rather than calling both of them AI', () => {
        expect(readingSources.DeterministicRule).not.toBe(readingSources.Model);
    });
});
