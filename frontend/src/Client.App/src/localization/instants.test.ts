// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { wordCalendarDay, wordInstant, wordRecentInstant } from './instants';

// The zone is pinned rather than compared against a formatter built the same way, which is the whole point of this
// file: an assertion written as `expect(shown).toBe(new Intl.DateTimeFormat(locale, options).format(at))` passes for a
// screen that named `timeZone: 'UTC'` as happily as for one that did not, and would have proved nothing. Each
// expectation below is the literal spelling the named zone produces, so rendering in any other zone fails it.
//
// `process.env['TZ']` is what the runtime resolves an unnamed zone from, and Node re-reads it when it is assigned, so
// setting it inside a test is how a screen is put in front of a reader somewhere else. It is restored afterwards
// because the suite runs in one worker and a zone left behind would decide what every later file sees.

const zoneBefore = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

// One instant, said twice from opposite sides of the planet: 09:41 UTC is the evening of the same day in Tokyo and the
// small hours of it in Los Angeles.
const instant = '2026-08-31T09:41:00+00:00';

describe('wordInstant', () => {
    it('places an instant in the zone the reader is actually in, east of it', () => {
        process.env['TZ'] = 'Asia/Tokyo';

        expect(wordInstant(instant, 'en', 'stamp')).toBe('8/31/26, 6:41 PM');
    });

    it('places the same instant in the zone the reader is actually in, west of it', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        expect(wordInstant(instant, 'en', 'stamp')).toBe('8/31/26, 2:41 AM');
    });

    it('says the whole of an instant a reader has stopped on, under the active language', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        expect(wordInstant(instant, 'pl', 'full')).toBe('31 sierpnia 2026 11:41');
    });

    it('answers with nothing where the message carries no instant', () => {
        expect(wordInstant(null, 'en', 'stamp')).toBeNull();
    });

    it('answers with nothing where what the service sent is not an instant this client can read', () => {
        expect(wordInstant('the day before yesterday', 'en', 'full')).toBeNull();
    });
});

describe('wordRecentInstant', () => {
    // The row is read at noon Warsaw time on the last day of August, which is the day of the instant above there.
    const readAt = Date.parse('2026-08-31T10:00:00+00:00');

    it('says the time alone for an instant of the same day', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        expect(wordRecentInstant(instant, 'en', readAt)).toBe('11:41 AM');
        expect(wordRecentInstant(instant, 'pl', readAt)).toBe('11:41');
    });

    it('says yesterday, in the active language, for an instant of the day before', () => {
        process.env['TZ'] = 'Europe/Warsaw';
        const tomorrow = Date.parse('2026-09-01T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', tomorrow)).toBe('yesterday');
        expect(wordRecentInstant(instant, 'pl', tomorrow)).toBe('wczoraj');
    });

    it('decides which day it is in the zone the reader is in rather than in the zone the message was sent from', () => {
        // 23:30 UTC on the 31st is already the 1st in Warsaw, so a reader there at noon on the 1st reads the message as
        // today's, and a reader in Los Angeles at the same moment reads it as yesterday's.
        const lateOnTheThirtyFirst = '2026-08-31T23:30:00+00:00';
        const noonOnTheFirst = Date.parse('2026-09-01T10:00:00+00:00');

        process.env['TZ'] = 'Europe/Warsaw';
        expect(wordRecentInstant(lateOnTheThirtyFirst, 'en', noonOnTheFirst)).toBe('1:30 AM');

        process.env['TZ'] = 'America/Los_Angeles';
        expect(wordRecentInstant(lateOnTheThirtyFirst, 'en', noonOnTheFirst)).toBe('yesterday');
    });

    it('says the day and the month for an instant earlier in the year', () => {
        process.env['TZ'] = 'Europe/Warsaw';
        const later = Date.parse('2026-10-15T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', later)).toBe('08/31');
        expect(wordRecentInstant(instant, 'pl', later)).toBe('31.08');
    });

    it('says the whole date for an instant of another year', () => {
        process.env['TZ'] = 'Europe/Warsaw';
        const nextYear = Date.parse('2027-01-05T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', nextYear)).toBe('8/31/26');
    });

    it('answers with nothing where the message carries no instant this client can read', () => {
        expect(wordRecentInstant(null, 'en', readAt)).toBeNull();
        expect(wordRecentInstant('the day before yesterday', 'en', readAt)).toBeNull();
    });
});

describe('wordCalendarDay', () => {
    it('reads back the day that was picked rather than one an offset moved it to', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        expect(wordCalendarDay('2026-08-15', 'en')).toBe('August 15, 2026');
    });

    it('reads back that same day east of the meridian too', () => {
        process.env['TZ'] = 'Pacific/Auckland';

        expect(wordCalendarDay('2026-08-15', 'en')).toBe('August 15, 2026');
    });

    it('shows a day it cannot read as the value it was given rather than as an invalid date', () => {
        expect(wordCalendarDay('sometime in August', 'en')).toBe('sometime in August');
    });
});
