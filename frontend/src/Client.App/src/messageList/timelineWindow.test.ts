// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { leadingRow, offsetOfRow, overscanRows, windowOf } from './timelineWindow';

// Ten rows of a hundred pixels each, so a number in an expectation reads as the row it stands for.
const rowHeight = 100;
const viewport = 1_000;

describe('windowOf', () => {
    it('draws nothing for a list holding nothing', () => {
        expect(windowOf(0, rowHeight, 0, viewport)).toStrictEqual({ first: 0, count: 0, above: 0, below: 0 });
    });

    it('draws the rows a screenful shows and the overscan after them, at the top of a long list', () => {
        const drawn = windowOf(10_000, rowHeight, 0, viewport);

        expect(drawn.first).toBe(0);
        expect(drawn.count).toBe(10 + overscanRows * 2);
        expect(drawn.above).toBe(0);
    });

    it('keeps the number of rows in the document the same however far down the list it is', () => {
        const near = windowOf(214_000, rowHeight, 0, viewport);
        const far = windowOf(214_000, rowHeight, 21_000_000, viewport);

        expect(far.count).toBe(near.count);
    });

    it('starts the overscan before the row the reader is on, so scrolling does not reach the end of the document', () => {
        const drawn = windowOf(10_000, rowHeight, 5_000, viewport);

        expect(drawn.first).toBe(50 - overscanRows);
    });

    it('reserves the space the rows above and below the window would take', () => {
        const drawn = windowOf(10_000, rowHeight, 5_000, viewport);

        expect(drawn.above).toBe(drawn.first * rowHeight);
        expect(drawn.above + drawn.count * rowHeight + drawn.below).toBe(10_000 * rowHeight);
    });

    it('draws to the end of a list rather than past it', () => {
        const drawn = windowOf(12, rowHeight, 0, viewport);

        expect(drawn.first).toBe(0);
        expect(drawn.count).toBe(12);
        expect(drawn.below).toBe(0);
    });

    it('draws the last rows for a scroller left below every row there is', () => {
        const drawn = windowOf(12, rowHeight, 99_000, viewport);

        expect(drawn.first).toBe(11);
        expect(drawn.count).toBe(1);
        expect(drawn.below).toBe(0);
    });

    it('reads a scroller pulled above its own top as being at the top', () => {
        expect(windowOf(100, rowHeight, -400, viewport)).toStrictEqual(windowOf(100, rowHeight, 0, viewport));
    });

    it('draws nothing before a row has been measured', () => {
        expect(windowOf(100, 0, 0, viewport).count).toBe(0);
    });
});

describe('offsetOfRow', () => {
    it('answers where a row stands in a list of rows one height each', () => {
        expect(offsetOfRow(42, rowHeight)).toBe(4_200);
    });

    it('answers the top of the list for a row before the first', () => {
        expect(offsetOfRow(-3, rowHeight)).toBe(0);
    });
});

describe('leadingRow', () => {
    it('answers the row under the top of the scroller rather than the first one drawn', () => {
        expect(leadingRow(4_250, rowHeight)).toBe(42);
    });

    it('answers the first row before anything has been measured', () => {
        expect(leadingRow(4_250, 0)).toBe(0);
    });
});
