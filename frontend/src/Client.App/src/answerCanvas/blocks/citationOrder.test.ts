// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { citationOrder } from './citationOrder';

describe('citationOrder', () => {
    it('numbers the block’s own sources first, in the order they are worth reading', () => {
        const positionOf = citationOrder(['c-1', 'c-2']);

        expect([positionOf('c-1'), positionOf('c-2')]).toEqual([1, 2]);
    });

    it('numbers a source only an item names after the ones the block itself rests on', () => {
        const positionOf = citationOrder(['c-1'], ['c-7']);

        expect(positionOf('c-7')).toBe(2);
    });

    it('gives a source named twice one number, so the same source is not two citations', () => {
        const positionOf = citationOrder(['c-1'], ['c-1'], ['c-9']);

        expect([positionOf('c-1'), positionOf('c-9')]).toEqual([1, 2]);
    });

    it('answers zero for a name none of the lists carries, rather than a place nothing holds', () => {
        expect(citationOrder(['c-1'])('c-4')).toBe(0);
    });
});
