// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { addressOf, implementedSpaces, isSpace, spaceAt, spaces } from './spaces';

describe('addressOf', () => {
    it.each(spaces)('writes %s as a fragment, so the address reloads without a server rule', (space) => {
        expect(addressOf(space)).toBe(`#/${space}`);
    });
});

describe('spaceAt', () => {
    it.each(spaces)('reads back the address %s was written to', (space) => {
        expect(spaceAt(addressOf(space))).toBe(space);
    });

    it('reads an address that has lost its separators, which is what a hand-typed one arrives as', () => {
        expect(spaceAt('mail')).toBe('mail');
    });

    it.each(['', '#', '#/', '#/nowhere', '#/mail/thread'])('names no space in %s', (address) => {
        expect(spaceAt(address)).toBeNull();
    });
});

describe('isSpace', () => {
    it('refuses a value the client carries no space for, whatever its type', () => {
        expect(isSpace('archive')).toBe(false);
        expect(isSpace(null)).toBe(false);
        expect(isSpace(0)).toBe(false);
    });
});

describe('implementedSpaces', () => {
    it('names only spaces the client actually carries, so nothing is drawn as working that is not', () => {
        expect(implementedSpaces.every((space) => spaces.includes(space))).toBe(true);
    });
});
