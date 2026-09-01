// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Which rows of a list are in the document, which is the whole of what keeps a mailbox of two hundred thousand
// messages costing what a screenful costs. It is arithmetic over four numbers rather than a package, and the
// measurement that decided it is in `frontend/README.md`: every row of this list is one height, so the only thing a
// virtualizer would add over this file is the machinery for measuring rows that are not.
//
// The height itself is measured from a rendered row rather than written here, because the row's height is a token
// decision and a number repeated in JavaScript is a second copy of it that drifts on the first change of the type
// scale, of the reader's font size, or of the browser's zoom.

/** How many rows outside the viewport are drawn either side of it, so scrolling does not reach the end of the document. */
export const overscanRows = 6;

/**
 * The estimate a list draws its first frame under, before a row has been rendered to measure.
 *
 * It only has to be close: a wrong estimate draws too many or too few rows for one frame, and the measurement that
 * arrives with that frame replaces it. It is deliberately not the row's real height, so nothing here can quietly become
 * the value the list works to.
 */
export const estimatedRowHeight = 72;

/** Which rows are drawn, and the space standing in for the ones that are not. */
export interface RowWindow {
    /** The index of the first row in the document. */
    readonly first: number;

    /** How many rows are in the document. */
    readonly count: number;

    /** The height, in pixels, reserved above them for the rows before. */
    readonly above: number;

    /** The height, in pixels, reserved below them for the rows after. */
    readonly below: number;
}

/**
 * The rows a scroller of this size shows at this offset, with the overscan either side of them.
 *
 * @param rowCount How many rows the list holds.
 * @param rowHeight What one row measures, in pixels.
 * @param scrollTop How far the scroller has been scrolled, in pixels.
 * @param viewportHeight How tall the scroller is, in pixels.
 * @returns The window of rows to draw and the space standing in for the rest.
 */
export function windowOf(rowCount: number, rowHeight: number, scrollTop: number, viewportHeight: number): RowWindow {
    if (rowCount <= 0 || rowHeight <= 0) {
        return { first: 0, count: 0, above: 0, below: 0 };
    }

    const drawn = Math.ceil(viewportHeight / rowHeight) + overscanRows * 2;
    const reached = Math.floor(Math.max(scrollTop, 0) / rowHeight);
    const first = Math.min(Math.max(reached - overscanRows, 0), Math.max(rowCount - 1, 0));
    const count = Math.min(drawn, rowCount - first);

    return {
        first,
        count,
        above: first * rowHeight,
        below: (rowCount - first - count) * rowHeight,
    };
}

/** How far a list has to be scrolled for a row to be the first one under the top of the scroller. */
export function offsetOfRow(row: number, rowHeight: number): number {
    return Math.max(row, 0) * rowHeight;
}

/**
 * The row the reader is looking at, which is what a returning visit is put back to.
 *
 * The first row under the top of the scroller rather than the first one drawn: the overscan is not on the screen, and
 * returning somebody to it would put them a screenful above where they left off.
 */
export function leadingRow(scrollTop: number, rowHeight: number): number {
    return rowHeight <= 0 ? 0 : Math.floor(Math.max(scrollTop, 0) / rowHeight);
}
