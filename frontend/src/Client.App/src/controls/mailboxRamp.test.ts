// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mailboxMarkHue } from './mailboxRamp';

// How many hues the ramp is declared with, restated here so that the expectations below read as statements about the
// rule rather than about three numbers that happen to line up with it.
const declaredHues = 3;

describe('mailboxMarkHue', () => {
    it.each([
        [1, 1],
        [2, 2],
        [3, 3],
    ])('gives the mailbox at ordinal %i the hue declared for it', (ordinal, hue) => {
        expect(mailboxMarkHue(ordinal)).toBe(hue);
    });

    it('starts the ramp again at the mailbox after the last declared hue', () => {
        expect(mailboxMarkHue(declaredHues + 1)).toBe(1);
    });

    it('keeps cycling for a deployment reading far more mailboxes than the ramp declares hues', () => {
        expect(mailboxMarkHue(declaredHues * 4 + 2)).toBe(2);
    });
});
