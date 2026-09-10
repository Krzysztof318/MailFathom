// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostRowsRemembered, noRows, rowSettled, rowsNoticed } from './movedRows';

describe('rowsNoticed', () => {
    it('reports nothing as having arrived in a list that had drawn nothing, because that list appeared', () => {
        const noticed = rowsNoticed(null, ['one', 'two', 'three']);

        expect([...noticed.arrived]).toEqual([]);
        expect([...noticed.shown]).toEqual(['one', 'two', 'three']);
    });

    it('reports the rows the reader had not been shown, and only those', () => {
        const first = rowsNoticed(null, ['one', 'two']);
        const second = rowsNoticed(first.shown, ['three', 'one', 'two']);

        expect([...second.arrived]).toEqual(['three']);
    });

    it('reports nothing where a page answered with exactly what was on the screen', () => {
        const first = rowsNoticed(null, ['one', 'two']);
        const second = rowsNoticed(first.shown, ['one', 'two']);

        expect([...second.arrived]).toEqual([]);
    });

    it('reports a row the reader has already been shown as arriving no second time', () => {
        const first = rowsNoticed(null, ['one']);
        const second = rowsNoticed(first.shown, ['two', 'one']);
        const third = rowsNoticed(second.shown, ['two', 'one']);

        expect([...second.arrived]).toEqual(['two']);
        expect([...third.arrived]).toEqual([]);
    });

    it('keeps what it remembers bounded, dropping what was drawn longest ago first', () => {
        const many = Array.from({ length: mostRowsRemembered }, (_, at) => `row-${String(at)}`);
        const first = rowsNoticed(null, many);
        const second = rowsNoticed(first.shown, ['one more']);

        expect(second.shown.size).toBe(mostRowsRemembered);
        expect(second.shown.has('one more')).toBe(true);
        expect(second.shown.has('row-0')).toBe(false);
    });

    it('keeps a row the newest page named, however long ago it was first drawn', () => {
        const many = Array.from({ length: mostRowsRemembered }, (_, at) => `row-${String(at)}`);
        const first = rowsNoticed(null, many);
        const second = rowsNoticed(first.shown, ['row-0', 'one more']);

        expect(second.shown.has('row-0')).toBe(true);
        expect(second.shown.has('row-1')).toBe(false);
    });
});

describe('rowSettled', () => {
    it('leaves the rows that have not finished moving where they were', () => {
        const left = rowSettled(new Set(['one', 'two']), 'one');

        expect([...left]).toEqual(['two']);
    });

    it('answers with the same set where the row it is told about was not moving, so nothing beside it is redrawn', () => {
        const rows = new Set(['one']);

        expect(rowSettled(rows, 'two')).toBe(rows);
    });

    it('answers with the one empty set once the last row has settled, so a list that noticed nothing draws nothing', () => {
        expect(rowSettled(new Set(['one']), 'one')).toBe(noRows);
    });
});
