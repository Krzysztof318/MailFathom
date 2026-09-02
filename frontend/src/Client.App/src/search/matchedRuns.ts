// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// An extract from a message, split into the parts that matched and the parts around them.
//
// The deployment marks what matched with `**`, and that is a mark rather than markup: the extract is text cut from
// untrusted mail, so what this answers is runs of text with a flag on each, and the screen draws them as elements it
// wrote itself. Nothing here produces markup and nothing downstream renders any.
//
// An extract whose marks do not pair up is drawn whole, marks and all, rather than half-interpreted. A message may
// carry `**` of its own, and dropping characters this could not read would be showing somebody an extract that is not
// what their mail says — which is worse than showing them a pair of asterisks.

const mark = '**';

/** One stretch of an extract, and whether it is what the search matched. */
export interface MatchedRun {
    readonly text: string;
    readonly matched: boolean;
}

/**
 * The runs an extract is drawn as.
 *
 * @param snippet The extract as the deployment marked it.
 * @returns The runs, in order, with the empty ones the marks leave behind removed.
 */
export function matchedRuns(snippet: string): readonly MatchedRun[] {
    const parts = snippet.split(mark);

    // An odd number of parts is an even number of marks, which is every mark closed. Anything else is an extract this
    // cannot read, and it is answered as the text it is.
    if (parts.length % 2 === 0) {
        return snippet.length === 0 ? [] : [{ text: snippet, matched: false }];
    }

    return parts.map((text, at) => ({ text, matched: at % 2 === 1 })).filter((run) => run.text.length > 0);
}
