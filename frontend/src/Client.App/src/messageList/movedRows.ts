// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { type RowContents, sameRow } from '../messageRows/rowContents';

// Which rows of a list moved, which is what the standing rule for every list in this client is drawn from: a list is
// drawn as a skeleton once, when it holds nothing, and every change after that reaches the rows it actually touched.
// So a page arriving is not a list arriving, and the difference between the two is exactly what this answers.

/**
 * The most rows whose having been drawn is remembered, oldest dropped first, for the reason the places map is bounded.
 *
 * What is kept per row is the mark of what it drew rather than the answer it drew from, which is what makes a bound
 * this high affordable at all: a mark holds a dozen fields of a row, while the answer beside it holds every address
 * the message was sent to, its preview, and its size — a mailbox held in memory to answer a question about the line
 * the reader is looking at.
 */
export const mostRowsRemembered = 10_000;

/** No rows at all, held as one object so a list that noticed nothing renders nothing again. */
export const noRows: ReadonlySet<string> = new Set();

/**
 * The rows still saying they moved, once this one has finished saying it.
 *
 * The same set back where it held nothing about that row, so a row settling changes nothing for the rows beside it.
 */
export function rowSettled(rows: ReadonlySet<string>, id: string): ReadonlySet<string> {
    if (!rows.has(id)) {
        return rows;
    }

    const left = new Set(rows);

    left.delete(id);

    return left.size === 0 ? noRows : left;
}

/**
 * The rows saying they moved, once these ones have started saying it too.
 *
 * A union rather than a replacement, because a page arriving does not finish what the page before it is still
 * animating: two arrivals a second apart are two sets of rows moving at once, and replacing the first with the second
 * would take the class off rows mid-animation and leave them stopped halfway.
 */
export function rowsAlsoMoved(rows: ReadonlySet<string>, moved: ReadonlySet<string>): ReadonlySet<string> {
    if (moved.size === 0) {
        return rows;
    }

    return new Set([...rows, ...moved]);
}

/**
 * The rows still saying they moved, less the ones the list has stopped drawing.
 *
 * A windowed list unmounts a row that scrolled out of it, and an unmounted row never reports its animation ending — so
 * a row held as having moved would come back mounted as one that had just moved, which is the replay this whole
 * mechanism exists to stop, one scroll further along.
 */
export function rowsStillDrawn(rows: ReadonlySet<string>, drawn: ReadonlySet<string>): ReadonlySet<string> {
    const left = [...rows].filter((id) => drawn.has(id));

    if (left.length === rows.size) {
        return rows;
    }

    return left.length === 0 ? noRows : new Set(left);
}

/**
 * What a page's arrival amounts to: which of its rows are new to the reader, which of them they are being shown
 * changing, and what they have now been shown.
 *
 * The third is a map from each row to the mark of what was drawn in it rather than a set of identities, and that is
 * what makes the second answerable at all: a page read again carries a whole answer per message, so the only way to
 * tell a row that changed from one the deployment merely wrote down again is against what the reader was last shown.
 */
export interface RowsNoticed {
    readonly arrived: ReadonlySet<string>;
    readonly changed: ReadonlySet<string>;
    readonly shown: Map<string, RowContents>;
}

/**
 * Reads a page against what the reader has already been shown.
 *
 * A row the page carries is one of three things, and only the first two are something to show happening. It **arrived**
 * where the reader has not been shown it before; it **changed** where they have and the page draws it differently; and
 * it is the same row where they have and it does not, which is the ordinary case for every page a refresh reads again
 * and the whole reason this is asked at all.
 *
 * @param shown What this list has drawn before, by row, or `null` for a list that has drawn nothing — whose first page
 * is the list appearing rather than rows arriving in it, so none of it is an arrival and none of it has changed.
 * @param marks The rows the page carries with the mark of what each of them draws, in the order it carries them.
 * @returns The rows that arrived, the rows that changed, and what the reader has been shown once this page is drawn.
 */
export function rowsNoticed(
    shown: ReadonlyMap<string, RowContents> | null,
    marks: readonly (readonly [string, RowContents])[],
): RowsNoticed {
    const drawn = new Map(shown ?? []);
    const arrived = new Set<string>();
    const changed = new Set<string>();

    for (const [id, mark] of marks) {
        const before = shown?.get(id);

        if (shown !== null) {
            if (before === undefined) {
                arrived.add(id);
            } else if (!sameRow(before, mark)) {
                changed.add(id);
            }
        }

        // Re-inserted rather than written in place, so a row the reader is still looking at is not the oldest thing in
        // the map: a Map keeps what was put in it in that order, and this is what the bound below gives up first.
        drawn.delete(id);
        drawn.set(id, mark);
    }

    for (const oldest of drawn.keys()) {
        if (drawn.size <= mostRowsRemembered) {
            break;
        }

        drawn.delete(oldest);
    }

    return {
        arrived: arrived.size === 0 ? noRows : arrived,
        changed: changed.size === 0 ? noRows : changed,
        shown: drawn,
    };
}
