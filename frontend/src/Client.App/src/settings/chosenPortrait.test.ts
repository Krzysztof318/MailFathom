// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { largestPortraitOctets } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { chosenPortrait } from './chosenPortrait';

describe('chosenPortrait', () => {
    it.each(['image/jpeg', 'image/png'])('admits a %s within the bound, under the kind it is', (type) => {
        expect(chosenPortrait(type, 1_000)).toStrictEqual({ outcome: 'admissible', type });
    });

    it('admits a file of exactly the largest size, the bound being what may be sent rather than what may not', () => {
        expect(chosenPortrait('image/png', largestPortraitOctets)).toStrictEqual({
            outcome: 'admissible',
            type: 'image/png',
        });
    });

    it.each(['image/gif', 'image/webp', 'image/svg+xml', 'application/pdf', ''])(
        'refuses %s as a kind this surface does not store',
        (type) => {
            expect(chosenPortrait(type, 1_000)).toStrictEqual({ outcome: 'notAnImageKind' });
        },
    );

    it('refuses a picture over the bound as too large rather than sending it to be refused', () => {
        expect(chosenPortrait('image/jpeg', largestPortraitOctets + 1)).toStrictEqual({
            outcome: 'largerThanAllowed',
        });
    });

    it('reports the kind before the size, so a file failing both is named by what it is', () => {
        expect(chosenPortrait('image/gif', largestPortraitOctets + 1)).toStrictEqual({ outcome: 'notAnImageKind' });
    });
});
