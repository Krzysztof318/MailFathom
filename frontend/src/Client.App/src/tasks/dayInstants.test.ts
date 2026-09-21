// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { dayAround, endAfter, startOfDay } from './dayInstants';

// The zone is pinned rather than assumed, because what every one of these answers is *which instants are somebody's
// day* — and a test written in whatever zone the machine happens to report would pass in one country and fail in the
// next. Warsaw is used throughout: it is an hour ahead of UTC in winter and two in summer, so a day read as UTC
// midnight shows up as the wrong day rather than as the same answer.
const declaredZone = process.env['TZ'];

function inZone(zone: string): void {
    process.env['TZ'] = zone;
}

afterEach(() => {
    process.env['TZ'] = declaredZone;
});

describe('dayAround', () => {
    it('opens and closes the day at the reader’s own midnights rather than at UTC’s', () => {
        inZone('Europe/Warsaw');

        expect(dayAround(Date.parse('2026-09-21T13:20:00+02:00'))).toStrictEqual({
            from: '2026-09-20T22:00:00.000Z',
            until: '2026-09-21T22:00:00.000Z',
        });
    });

    it('gives the same instant a different day either side of the date line', () => {
        inZone('Pacific/Auckland');

        const auckland = dayAround(Date.parse('2026-09-21T13:20:00+02:00'));

        inZone('Europe/Warsaw');

        expect(dayAround(Date.parse('2026-09-21T13:20:00+02:00')).from).not.toBe(auckland.from);
    });

    // Built by moving the day rather than by adding twenty-four hours, which is the whole reason the function exists:
    // the night Poland leaves summer time is twenty-five hours long, and a day closed an hour early would drop the
    // last hour of it off both the calendar panel and the arrangement.
    it('closes a day carrying a daylight-saving change at the next midnight rather than an hour either side of it', () => {
        inZone('Europe/Warsaw');

        const day = dayAround(Date.parse('2026-10-25T09:00:00+02:00'));

        expect(day.from).toBe('2026-10-24T22:00:00.000Z');
        expect(day.until).toBe('2026-10-25T23:00:00.000Z');
    });
});

describe('startOfDay', () => {
    it('reads a due day as the reader’s own midnight, so an event written for it lands on that day', () => {
        inZone('Europe/Warsaw');

        expect(startOfDay('2026-09-21')).toBe('2026-09-20T22:00:00.000Z');
    });

    it('answers with nothing where the value is not a day at all', () => {
        expect(startOfDay('the day after tomorrow')).toBeNull();
    });
});

describe('endAfter', () => {
    it('ends an arrangement where the minutes the deployment stated run out', () => {
        expect(endAfter('2026-09-21T09:00:00.000Z', 45)).toBe('2026-09-21T09:45:00.000Z');
    });

    it('answers with nothing where the start is not an instant', () => {
        expect(endAfter('some time this morning', 45)).toBeNull();
    });
});
