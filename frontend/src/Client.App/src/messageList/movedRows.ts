// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Which rows of a list moved, which is what the standing rule for every list in this client is drawn from: a list is
// drawn as a skeleton once, when it holds nothing, and every change after that reaches the rows it actually touched.
// So a page arriving is not a list arriving, and the difference between the two is exactly what this answers.

/** The most rows whose having been drawn is remembered, oldest dropped first, for the reason the places map is bounded. */
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

/** What a page's arrival amounts to: which of its rows are new to the reader, and what they have now been shown. */
export interface RowsNoticed {
    readonly arrived: ReadonlySet<string>;
    readonly shown: Set<string>;
}

/**
 * Reads a page against what the reader has already been shown.
 *
 * @param shown What this list has drawn before, or `null` for a list that has drawn nothing — whose first page is the
 * list appearing rather than rows arriving in it, and none of which is therefore an arrival.
 * @param ids The rows the page carries, in the order it carries them.
 * @returns The rows that arrived, and what the reader has been shown once this page is drawn.
 */
export function rowsNoticed(shown: ReadonlySet<string> | null, ids: readonly string[]): RowsNoticed {
    const drawn = new Set(shown ?? []);
    const arrived = shown === null ? noRows : new Set(ids.filter((id) => !drawn.has(id)));

    for (const id of ids) {
        // Re-inserted rather than left where it was, so a row the reader is still looking at is not the oldest thing
        // in the map: a Set keeps what was put in it in that order, and this is what the bound below gives up first.
        drawn.delete(id);
        drawn.add(id);
    }

    for (const oldest of drawn) {
        if (drawn.size <= mostRowsRemembered) {
            break;
        }

        drawn.delete(oldest);
    }

    return { arrived, shown: drawn };
}
