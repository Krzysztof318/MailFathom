// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { RowContents } from '../messageRows/rowContents';
import { mostRowsRemembered, noRows, rowSettled, rowsAlsoMoved, rowsNoticed, rowsStillDrawn } from './movedRows';

/** One row's drawing, with whatever the case under test cares about written over it. */
function drawn(over: Partial<RowContents> = {}): RowContents {
    return {
        senderDisplayName: 'Ada Lovelace',
        senderAddress: 'ada@example.test',
        subject: 'The analytical engine',
        receivedAt: '2026-09-11T08:00:00.000Z',
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        threadMessageCount: null,
        reading: null,
        ...over,
    };
}

/** The rows a page carries, each drawing what every other page of the same test draws for it. */
function page(...ids: readonly string[]): readonly (readonly [string, RowContents])[] {
    return ids.map((id) => [id, drawn({ subject: id })] as const);
}

describe('rowsNoticed', () => {
    it('reports nothing as having arrived in a list that had drawn nothing, because that list appeared', () => {
        const noticed = rowsNoticed(null, page('one', 'two', 'three'));

        expect([...noticed.arrived]).toEqual([]);
        expect([...noticed.changed]).toEqual([]);
        expect([...noticed.shown.keys()]).toEqual(['one', 'two', 'three']);
    });

    it('reports the rows the reader had not been shown, and only those', () => {
        const first = rowsNoticed(null, page('one', 'two'));
        const second = rowsNoticed(first.shown, page('three', 'one', 'two'));

        expect([...second.arrived]).toEqual(['three']);
    });

    it('reports nothing where a page answered with exactly what was on the screen', () => {
        const first = rowsNoticed(null, page('one', 'two'));
        const second = rowsNoticed(first.shown, page('one', 'two'));

        expect([...second.arrived]).toEqual([]);
        expect(second.changed).toBe(noRows);
    });

    it('reports a row the page draws differently as changed rather than as having arrived', () => {
        const first = rowsNoticed(null, page('one', 'two'));
        const second = rowsNoticed(first.shown, [
            ['one', drawn({ subject: 'one', unread: true })],
            ['two', drawn({ subject: 'two' })],
        ]);

        expect([...second.changed]).toEqual(['one']);
        expect([...second.arrived]).toEqual([]);
    });

    it('reports a row the reader is shown changing once, the page after it drawing the same row again', () => {
        const first = rowsNoticed(null, page('one'));
        const second = rowsNoticed(first.shown, [['one', drawn({ subject: 'one', flagged: true })]]);
        const third = rowsNoticed(second.shown, [['one', drawn({ subject: 'one', flagged: true })]]);

        expect([...second.changed]).toEqual(['one']);
        expect(third.changed).toBe(noRows);
    });

    it('reports a row the reader has already been shown as arriving no second time', () => {
        const first = rowsNoticed(null, page('one'));
        const second = rowsNoticed(first.shown, page('two', 'one'));
        const third = rowsNoticed(second.shown, page('two', 'one'));

        expect([...second.arrived]).toEqual(['two']);
        expect([...third.arrived]).toEqual([]);
    });

    it('keeps what it remembers bounded, dropping what was drawn longest ago first', () => {
        const many = page(...Array.from({ length: mostRowsRemembered }, (_, at) => `row-${String(at)}`));
        const first = rowsNoticed(null, many);
        const second = rowsNoticed(first.shown, page('one more'));

        expect(second.shown.size).toBe(mostRowsRemembered);
        expect(second.shown.has('one more')).toBe(true);
        expect(second.shown.has('row-0')).toBe(false);
    });

    it('keeps a row the newest page named, however long ago it was first drawn', () => {
        const many = page(...Array.from({ length: mostRowsRemembered }, (_, at) => `row-${String(at)}`));
        const first = rowsNoticed(null, many);
        const second = rowsNoticed(first.shown, page('row-0', 'one more'));

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

describe('rowsAlsoMoved', () => {
    it('keeps the rows already moving beside the ones that just did, two arrivals being able to overlap', () => {
        const moving = rowsAlsoMoved(new Set(['one']), new Set(['two', 'three']));

        expect([...moving].sort()).toEqual(['one', 'three', 'two']);
    });

    it('answers with the same set where nothing moved, so a page that brought nothing new redraws nothing', () => {
        const rows = new Set(['one']);

        expect(rowsAlsoMoved(rows, noRows)).toBe(rows);
    });
});

describe('rowsStillDrawn', () => {
    it('lets go of a row the list has stopped drawing, which will never say it has settled', () => {
        const left = rowsStillDrawn(new Set(['one', 'two']), new Set(['two']));

        expect([...left]).toEqual(['two']);
    });

    it('answers with the same set where every row it holds is still drawn', () => {
        const rows = new Set(['one', 'two']);

        expect(rowsStillDrawn(rows, new Set(['one', 'two', 'three']))).toBe(rows);
    });

    it('answers with the one empty set where the window has left every row it held behind', () => {
        expect(rowsStillDrawn(new Set(['one']), new Set(['two']))).toBe(noRows);
    });
});
