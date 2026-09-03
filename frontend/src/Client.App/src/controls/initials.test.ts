// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { initialsOf } from './initials';

describe('initialsOf', () => {
    it('takes the first letter of the first name and of the last', () => {
        expect(initialsOf('Ada Lovelace', null)).toBe('AL');
    });

    it('takes one letter from somebody who goes by a single name', () => {
        expect(initialsOf('Ada', null)).toBe('A');
    });

    it('skips the names between the first and the last rather than growing with them', () => {
        expect(initialsOf('Ada Byron King Lovelace', null)).toBe('AL');
    });

    it('reads the part in front of the host where nothing calls the person anything', () => {
        expect(initialsOf(null, 'ada.lovelace@example.invalid')).toBe('A');
    });

    it('reads a whole address as a name where there is no host to cut it at', () => {
        expect(initialsOf(null, 'ada')).toBe('A');
    });

    it('prefers the name over the address, that being what a person is called', () => {
        expect(initialsOf('Grace Hopper', 'ada@example.invalid')).toBe('GH');
    });

    it('answers nothing where neither offers a letter', () => {
        expect(initialsOf(null, null)).toBeNull();
    });

    it('answers nothing for a name made of nothing but punctuation', () => {
        expect(initialsOf('— —', null)).toBeNull();
    });

    it('draws the letters in upper case whatever they were written in', () => {
        expect(initialsOf('ada lovelace', null)).toBe('AL');
    });

    it('reads a letter outside the Latin alphabet as one letter rather than as its encoding', () => {
        expect(initialsOf('Żaneta Ćwik', null)).toBe('ŻĆ');
    });
});
