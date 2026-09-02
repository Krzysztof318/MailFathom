// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { ageOf } from './synchronizationAge';

// The instant every age below is measured against, handed in rather than taken off a clock — which is the whole reason
// `ageOf` accepts one, and what lets a suite state the wording a person reads rather than the day it ran on.
const readAt = new Date('2026-08-31T12:00:00Z');

const second = 1_000;
const minute = 60 * second;
const hour = 60 * minute;
const day = 24 * hour;

/** An instant that long before the answer was read, in the form a deployment reports one. */
function before(elapsed: number): string {
    return new Date(readAt.getTime() - elapsed).toISOString();
}

describe('ageOf', () => {
    // One case per unit the ladder climbs, because a wrong ratio between two of them is invisible in the unit above and
    // shows the wrong age on the freshness panel rather than failing anywhere.
    it.each([
        ['seconds', before(30 * second), '30 seconds ago'],
        ['minutes', before(5 * minute), '5 minutes ago'],
        ['hours', before(3 * hour), '3 hours ago'],
        ['days', before(2 * day), '2 days ago'],
        ['weeks', before(20 * day), '3 weeks ago'],
        ['months', before(60 * day), '2 months ago'],
        ['years', before(900 * day), '2 years ago'],
    ])('words an age of %s the way a person reads it', (_, instant, expected) => {
        expect(ageOf(instant, readAt, 'en')).toBe(expected);
    });

    // Asked of `Intl` rather than spelled out, for both reasons at once: the wording is the platform's and a suite
    // that repeated it would be asserting against a copy of it, and Polish spellings do not go into a file the typo
    // checker reads as English. What this proves is the part that is this module's — that the language reaches the
    // formatter at all, over the three plural forms Polish needs where English has one.
    it.each([1, 2, 5])('agrees with the language it is asked in, here %s minute(s) in Polish', (elapsed) => {
        expect(ageOf(before(elapsed * minute), readAt, 'pl')).toBe(
            new Intl.RelativeTimeFormat('pl', { numeric: 'auto' }).format(-elapsed, 'minute'),
        );
    });

    it('has no age for an account the deployment named no instant for', () => {
        expect(ageOf(null, readAt, 'en')).toBeNull();
    });

    it('has no age for an instant this client cannot read, rather than putting a broken date on the screen', () => {
        expect(ageOf('the day before yesterday', readAt, 'en')).toBeNull();
    });

    it('reads an instant after the answer was read as one, rather than as an age it cannot word', () => {
        expect(ageOf(new Date(readAt.getTime() + 5 * minute).toISOString(), readAt, 'en')).toBe('in 5 minutes');
    });
});
