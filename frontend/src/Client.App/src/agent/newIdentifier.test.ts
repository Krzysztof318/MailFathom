// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { newIdentifier } from './newIdentifier';

const mintedAt = Date.parse('2024-06-13T15:25:38.296Z');

function drawing(octet: number): void {
    vi.spyOn(crypto, 'getRandomValues').mockImplementation((array) => {
        (array as Uint8Array).fill(octet);
        return array;
    });
}

afterEach(() => {
    vi.restoreAllMocks();
});

describe('newIdentifier', () => {
    it('leads with the millisecond it was minted at and marks itself version 7 over random bits that are all set', () => {
        drawing(0xff);

        expect(newIdentifier(mintedAt)).toBe('01901234-5678-7fff-bfff-ffffffffffff');
    });

    it('sets the version and the variant over random bits that are all clear', () => {
        drawing(0x00);

        expect(newIdentifier(mintedAt)).toBe('01901234-5678-7000-8000-000000000000');
    });
});
