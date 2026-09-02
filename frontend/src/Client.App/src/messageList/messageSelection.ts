// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What several messages at once means, as an operation on identities rather than as something the list draws. The
// selection itself is the workspace's, because *select and ask* is the client's most-used gesture and the question is
// asked somewhere the list is not — a selection only the list knew about would be a visual state that the rest of the
// client could not read as scope.
//
// Order matters and is the list's rather than the click's: a selection read back as a question about four messages
// should name them in the order the reader sees them, whichever one they happened to point at first.

/** The one message a plain click selects. */
export function onlySelected(id: string): readonly string[] {
    return [id];
}

/** The selection with one message added or taken out of it, which is what a click holding the modifier key does. */
export function withToggled(selected: readonly string[], id: string): readonly string[] {
    return selected.includes(id) ? selected.filter((chosen) => chosen !== id) : [...selected, id];
}

/**
 * Every message from the anchor to the one reached, in the order the list draws them.
 *
 * Replacing rather than adding, which is what a click holding shift does everywhere a list is selected: the anchor
 * stays where it was, so dragging or shifting again from the same anchor grows and shrinks one run rather than leaving
 * the runs it passed over behind.
 *
 * @param rows The identities the list is holding, in the order it draws them.
 * @param anchor Where the run started, which is the last message selected without shift.
 * @param reached Where the run has got to.
 * @returns The run, or nothing where either end is no longer held.
 */
export function rangeBetween(rows: readonly string[], anchor: string, reached: string): readonly string[] {
    const from = rows.indexOf(anchor);
    const to = rows.indexOf(reached);

    if (from < 0 || to < 0) {
        return [];
    }

    return rows.slice(Math.min(from, to), Math.max(from, to) + 1);
}

/**
 * The selection a gesture that runs from the anchor leaves behind.
 *
 * The run where both ends are held, and otherwise what was already selected with the message reached added to it. That
 * fallback is the whole point of this function: the anchor's page is dropped once the reader has scrolled far enough
 * from it, and a run that cannot be worked out must not be written back as the empty selection it computes to — the
 * pages behind the reader go, and what they picked out of them does not.
 *
 * @param selected What is selected.
 * @param rows The identities the list is holding, in the order it draws them.
 * @param anchor Where the run started.
 * @param reached Where the run has got to.
 * @returns The selection after the gesture.
 */
export function extendedTo(
    selected: readonly string[],
    rows: readonly string[],
    anchor: string,
    reached: string,
): readonly string[] {
    const run = rangeBetween(rows, anchor, reached);

    if (run.length > 0) {
        return run;
    }

    return selected.includes(reached) ? selected : [...selected, reached];
}

/**
 * The selection ordered as the list draws it, and with anything the list no longer holds left in place.
 *
 * A message scrolled past is still selected — the pages behind the reader are dropped and the selection is not, because
 * a question about four messages must not lose one of them to the list having been scrolled. So this orders what it can
 * find and appends what it cannot, rather than filtering.
 *
 * @param selected What is selected.
 * @param rows The identities the list is holding, in the order it draws them.
 * @returns The selection in reading order.
 */
export function inReadingOrder(selected: readonly string[], rows: readonly string[]): readonly string[] {
    const held = rows.filter((id) => selected.includes(id));
    const beyond = selected.filter((id) => !rows.includes(id));

    return [...held, ...beyond];
}
