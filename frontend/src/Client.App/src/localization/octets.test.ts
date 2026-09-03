// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { sizeOf } from './octets';

// The wording is `Intl`'s, so an expectation spelled out by hand would be an expectation about this machine rather than
// about the unit reaching the formatter. Each test therefore asks `Intl` the same question the module asked it.
function worded(value: number, unit: 'byte' | 'kilobyte' | 'megabyte' | 'gigabyte', fractionDigits: number): string {
    return new Intl.NumberFormat('en', {
        style: 'unit',
        unit,
        unitDisplay: 'short',
        maximumFractionDigits: fractionDigits,
    }).format(value);
}

describe('sizeOf', () => {
    it('counts a small file in whole octets, because half an octet says nothing', () => {
        expect(sizeOf(512, 'en')).toBe(worded(512, 'byte', 0));
    });

    it('reads nothing at all as no octets rather than as the smallest unit above them', () => {
        expect(sizeOf(0, 'en')).toBe(worded(0, 'byte', 0));
    });

    it('moves to the largest unit that leaves a number a person reads at a glance', () => {
        expect(sizeOf(2_048, 'en')).toBe(worded(2.048, 'kilobyte', 1));
    });

    it('stays in a unit until the next one holds at least one of what is being measured', () => {
        expect(sizeOf(999_999, 'en')).toBe(worded(999.999, 'kilobyte', 1));
    });

    it('words the unit under the language it was asked in rather than out of a catalogue', () => {
        expect(sizeOf(3_000_000, 'pl')).toBe(
            new Intl.NumberFormat('pl', {
                style: 'unit',
                unit: 'megabyte',
                unitDisplay: 'short',
                maximumFractionDigits: 1,
            }).format(3),
        );
    });

    it('reaches the largest unit it names rather than reporting an enormous number of the one below', () => {
        expect(sizeOf(4_000_000_000, 'en')).toBe(worded(4, 'gigabyte', 1));
    });
});
