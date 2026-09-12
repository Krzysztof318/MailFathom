// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { senderColourRole } from './senderColour';

// What is asserted here is the one distinction the reading surfaces keep — small print stays small print — and that
// everything else is the body colour. No assertion names a hue or a contrast figure: the two answers are tokens the
// stylesheet states for both panels, so legibility is a property of the token rather than of a value computed here.

describe('senderColourRole', () => {
    it.each([['#666666'], ['#888888'], ['#999999'], ['#aaaaaa'], ['#cccccc']])(
        'reads %s as small print, which is the grey a sender steps back with',
        (written) => {
            expect(senderColourRole(written)).toBe('receding');
        },
    );

    it.each([['#000000'], ['#111111'], ['#222222'], ['#333333']])(
        'reads %s as the message own text, which is what a sender writes a body in',
        (written) => {
            expect(senderColourRole(written)).toBe('ordinary');
        },
    );

    // A decoration is a decoration whatever its lightness, which is the half of this that keeps every message in one
    // colour: a newsletter's blue headings, a red warning and a green tick each take the body colour.
    it.each([['#0048e0'], ['#ff0000'], ['#008000'], ['#800080'], ['#ffc0cb'], ['#ffff00']])(
        'reads %s as a decoration the reading surface does not reproduce',
        (written) => {
            expect(senderColourRole(written)).toBe('ordinary');
        },
    );

    it.each([['red'], ['#fff'], ['rgb(0 0 0)'], [''], ['#12345g']])(
        'reads %s as the body colour, a value this client cannot read deciding nothing',
        (written) => {
            expect(senderColourRole(written)).toBe('ordinary');
        },
    );
});
