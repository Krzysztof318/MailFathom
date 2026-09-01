// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { inReadingOrder, onlySelected, rangeBetween, withToggled } from './messageSelection';

const drawn = ['one', 'two', 'three', 'four', 'five'];

describe('onlySelected', () => {
    it('selects the one message a plain click picked out', () => {
        expect(onlySelected('three')).toStrictEqual(['three']);
    });
});

describe('withToggled', () => {
    it('adds a message the selection does not hold', () => {
        expect(withToggled(['one'], 'three')).toStrictEqual(['one', 'three']);
    });

    it('takes out a message the selection holds', () => {
        expect(withToggled(['one', 'three'], 'one')).toStrictEqual(['three']);
    });
});

describe('rangeBetween', () => {
    it('selects every message from the anchor to the one reached', () => {
        expect(rangeBetween(drawn, 'two', 'four')).toStrictEqual(['two', 'three', 'four']);
    });

    it('selects the same run whichever end the reader started from', () => {
        expect(rangeBetween(drawn, 'four', 'two')).toStrictEqual(rangeBetween(drawn, 'two', 'four'));
    });

    it('selects one message where both ends are the same', () => {
        expect(rangeBetween(drawn, 'two', 'two')).toStrictEqual(['two']);
    });

    it('selects nothing where an end is no longer drawn', () => {
        expect(rangeBetween(drawn, 'dropped', 'four')).toStrictEqual([]);
    });
});

describe('inReadingOrder', () => {
    it('orders a selection the way the list draws it, whichever order it was picked in', () => {
        expect(inReadingOrder(['four', 'one'], drawn)).toStrictEqual(['one', 'four']);
    });

    it('keeps a message the list has scrolled past, so a question about four does not lose one', () => {
        expect(inReadingOrder(['scrolled-past', 'two'], drawn)).toStrictEqual(['two', 'scrolled-past']);
    });
});
