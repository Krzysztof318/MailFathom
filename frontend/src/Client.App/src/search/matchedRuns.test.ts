// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { matchedRuns } from './matchedRuns';

describe('matchedRuns', () => {
    it('separates what the search matched from the words around it', () => {
        expect(matchedRuns('The **invoice** for August')).toStrictEqual([
            { text: 'The ', matched: false },
            { text: 'invoice', matched: true },
            { text: ' for August', matched: false },
        ]);
    });

    it('marks every match in an extract rather than the first', () => {
        expect(matchedRuns('**one** and **two**')).toStrictEqual([
            { text: 'one', matched: true },
            { text: ' and ', matched: false },
            { text: 'two', matched: true },
        ]);
    });

    it('reads an extract that is one match from end to end', () => {
        expect(matchedRuns('**invoice**')).toStrictEqual([{ text: 'invoice', matched: true }]);
    });

    it('reads an extract nothing marked as one stretch of words', () => {
        expect(matchedRuns('nothing was marked here')).toStrictEqual([
            { text: 'nothing was marked here', matched: false },
        ]);
    });

    it.each([['a mark that never closes **here'], ['**one** and **two'], ['a message writing ** of its own']])(
        'draws %s whole rather than reading half of it',
        (snippet) => {
            expect(matchedRuns(snippet)).toStrictEqual([{ text: snippet, matched: false }]);
        },
    );

    it('reads an extract holding nothing as no runs at all', () => {
        expect(matchedRuns('')).toStrictEqual([]);
    });
});
