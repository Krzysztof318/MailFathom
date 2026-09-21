// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where each source stands among the ones one block names, which is the number `Citation.tsx` draws.
//
// A block that cites per item rests on two lists rather than one: what the block as a whole rests on, and what each
// event, cell, or file rests on — and the contract lets the second name a source the first does not. So the numbering
// cannot be a position in `evidence.citations`: a source found nowhere in it would be numbered zero, which is the
// citation before the first one. What it is instead is the order a reader meets the sources in — the block's own list
// first, because that is the order they are worth reading in, then anything an item adds, as it is first named.

/**
 * Where each source a block names stands among them, counted from one.
 *
 * @param named The lists the block names its sources in, most significant first.
 * @returns What place a source holds, and zero for a name none of those lists carries — which nothing drawn out of the
 * same lists can ask for, and which is a number rather than an absence so that no renderer has a second answer for it.
 */
export function citationOrder(...named: readonly (readonly string[])[]): (source: string) => number {
    const ordered = new Map<string, number>();

    for (const sources of named) {
        for (const source of sources) {
            if (!ordered.has(source)) {
                ordered.set(source, ordered.size + 1);
            }
        }
    }

    return (source) => ordered.get(source) ?? 0;
}
