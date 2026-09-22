// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { wordCalendarDay, wordDueDay, wordInstant, wordInstantRange, wordRecentInstant } from './instants';

// The zone is pinned rather than compared against a formatter built the same way, which is the whole point of this
// file: an assertion written as `expect(shown).toBe(new Intl.DateTimeFormat(locale, options).format(at))` passes for a
// screen that named `timeZone: 'UTC'` as happily as for one that did not, and would have proved nothing. Each
// expectation below is the literal spelling the named zone produces, so rendering in any other zone fails it.
//
// The zone is the one the reader's record states, which is what every screen passes in. `null` is the case where no
// record has answered, and there it is the runtime's own zone that decides — which `process.env['TZ']` is what pins,
// Node re-reading it when it is assigned. It is restored afterwards because the suite runs in one worker and a zone
// left behind would decide what every later file sees.

const zoneBefore = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

// One instant, said twice from opposite sides of the planet: 09:41 UTC is the evening of the same day in Tokyo and the
// small hours of it in Los Angeles.
const instant = '2026-08-31T09:41:00+00:00';

describe('wordInstant', () => {
    it('places an instant in the zone the reader’s record states, east of it', () => {
        expect(wordInstant(instant, 'en', 'stamp', 'Asia/Tokyo')).toBe('8/31/26, 6:41 PM');
    });

    it('places the same instant in the zone the reader’s record states, west of it', () => {
        expect(wordInstant(instant, 'en', 'stamp', 'America/Los_Angeles')).toBe('8/31/26, 2:41 AM');
    });

    // The record is what a deployment anchors a relative period on, so a browser reporting another zone changes
    // nothing about what a reader is shown — which is the defect this parameter exists to close.
    it('places it in the record’s zone rather than in the one the runtime reports', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        expect(wordInstant(instant, 'en', 'stamp', 'Asia/Tokyo')).toBe('8/31/26, 6:41 PM');
    });

    it('places it in the runtime’s own zone where no record has answered', () => {
        process.env['TZ'] = 'Asia/Tokyo';

        expect(wordInstant(instant, 'en', 'stamp', null)).toBe('8/31/26, 6:41 PM');
    });

    it('words an instant standing for a whole day as the day alone, on the reader’s side of midnight', () => {
        const lateInTheEvening = '2026-08-31T20:00:00+00:00';

        expect(wordInstant(lateInTheEvening, 'en', 'day', 'Asia/Tokyo')).toBe('Tuesday, September 1, 2026');
        expect(wordInstant(lateInTheEvening, 'en', 'day', 'America/Los_Angeles')).toBe('Monday, August 31, 2026');
    });

    // The deployment's zone database is what validated the identifier and the two need not be the same build, so a
    // zone this runtime has never heard of leaves one date in the runtime's zone rather than throwing through every
    // row on the screen at once.
    it('falls back to the runtime’s own zone for a zone this runtime does not carry', () => {
        process.env['TZ'] = 'Asia/Tokyo';

        expect(wordInstant(instant, 'en', 'stamp', 'Europe/Warszawa')).toBe('8/31/26, 6:41 PM');
    });

    it('says the whole of an instant a reader has stopped on, under the active language', () => {
        expect(wordInstant(instant, 'pl', 'full', 'Europe/Warsaw')).toBe('31 sierpnia 2026 11:41');
    });

    it('answers with nothing where the message carries no instant', () => {
        expect(wordInstant(null, 'en', 'stamp', 'Europe/Warsaw')).toBeNull();
    });

    it('answers with nothing where what the service sent is not an instant this client can read', () => {
        expect(wordInstant('the day before yesterday', 'en', 'full', 'Europe/Warsaw')).toBeNull();
    });
});

// One character of a range's spelling is the runtime's rather than the client's: ICU writes the space before `AM`
// and `PM` as a narrow no-break one from version 72, and the pipeline's Node and a developer's need not carry the same
// ICU. So that one character is read as an ordinary space and every other part of the spelling stays literal — which
// is what keeps the zone, the order, and the dash asserted rather than waved through.
function spelled(said: string | null): string | null {
    return said === null ? said : said.replaceAll('\u202f', ' ');
}

describe('wordInstantRange', () => {
    // A meeting on one morning in Warsaw, which is the shape a calendar entry has: two instants an hour and a half
    // apart on one day.
    const opens = '2026-09-24T09:00:00+02:00';
    const closes = '2026-09-24T10:30:00+02:00';

    it('says the run between two instants with the dash and the spacing the language uses', () => {
        expect(spelled(wordInstantRange(opens, closes, 'en', 'time', 'Europe/Warsaw'))).toBe(
            '9:00\u2009\u2013\u200910:30 AM',
        );
    });

    it('says the same run the way the other language says one, rather than in English order', () => {
        expect(wordInstantRange(opens, closes, 'pl', 'time', 'Europe/Warsaw')).toBe('09:00\u201310:30');
    });

    // An event is as much the reader's own day as a message is, so a calendar entry is placed by the record exactly as
    // a message row is — otherwise one screen draws a meeting at nine and another draws the mail about it at midnight.
    it('places the run in the zone the reader’s record states', () => {
        expect(spelled(wordInstantRange(opens, closes, 'en', 'time', 'America/Los_Angeles'))).toBe(
            '12:00\u2009\u2013\u20091:30 AM',
        );
    });

    it('places it in the record’s zone rather than in the one the runtime reports', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        expect(spelled(wordInstantRange(opens, closes, 'en', 'time', 'Europe/Warsaw'))).toBe(
            '9:00\u2009\u2013\u200910:30 AM',
        );
    });

    it('places it in the runtime’s own zone where no record has answered', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        expect(spelled(wordInstantRange(opens, closes, 'en', 'time', null))).toBe('12:00\u2009\u2013\u20091:30 AM');
    });

    it('says the whole of both ends where a reader has stopped on them', () => {
        expect(spelled(wordInstantRange(opens, closes, 'en', 'full', 'Europe/Warsaw'))).toBe(
            'September 24, 2026, 9:00\u2009\u2013\u200910:30 AM',
        );
    });

    it('says the first alone where the event states no end', () => {
        expect(wordInstantRange(opens, null, 'en', 'time', 'Europe/Warsaw')).toBe('9:00 AM');
    });

    it('says the first alone where the end is not an instant this client can read', () => {
        expect(wordInstantRange(opens, 'the day after', 'en', 'time', 'Europe/Warsaw')).toBe('9:00 AM');
    });

    it('answers with nothing where the beginning is not an instant this client can read', () => {
        expect(wordInstantRange('whenever', closes, 'en', 'time', 'Europe/Warsaw')).toBeNull();
    });
});

describe('wordRecentInstant', () => {
    // The row is read at noon Warsaw time on the last day of August, which is the day of the instant above there.
    const readAt = Date.parse('2026-08-31T10:00:00+00:00');

    it('says the time alone for an instant of the same day', () => {
        expect(wordRecentInstant(instant, 'en', readAt, 'Europe/Warsaw')).toBe('11:41 AM');
        expect(wordRecentInstant(instant, 'pl', readAt, 'Europe/Warsaw')).toBe('11:41');
    });

    it('says yesterday, in the active language, for an instant of the day before', () => {
        const tomorrow = Date.parse('2026-09-01T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', tomorrow, 'Europe/Warsaw')).toBe('yesterday');
        expect(wordRecentInstant(instant, 'pl', tomorrow, 'Europe/Warsaw')).toBe('wczoraj');
    });

    it('decides which day it is in the reader’s own zone rather than in the zone the message was sent from', () => {
        // 23:30 UTC on the 31st is already the 1st in Warsaw, so a reader there at noon on the 1st reads the message as
        // today's, and a reader in Los Angeles at the same moment reads it as yesterday's.
        const lateOnTheThirtyFirst = '2026-08-31T23:30:00+00:00';
        const noonOnTheFirst = Date.parse('2026-09-01T10:00:00+00:00');

        expect(wordRecentInstant(lateOnTheThirtyFirst, 'en', noonOnTheFirst, 'Europe/Warsaw')).toBe('1:30 AM');
        expect(wordRecentInstant(lateOnTheThirtyFirst, 'en', noonOnTheFirst, 'America/Los_Angeles')).toBe('yesterday');
    });

    // The two answers above are what a person would read on one machine standing in the other zone, which is the pair
    // this whole change exists to stop disagreeing: the record decides, and the runtime's report decides nothing.
    it('decides which day it is in the record’s zone rather than in the one the runtime reports', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        const lateOnTheThirtyFirst = '2026-08-31T23:30:00+00:00';
        const noonOnTheFirst = Date.parse('2026-09-01T10:00:00+00:00');

        expect(wordRecentInstant(lateOnTheThirtyFirst, 'en', noonOnTheFirst, 'Europe/Warsaw')).toBe('1:30 AM');
    });

    it('says the day and the month for an instant earlier in the year', () => {
        const later = Date.parse('2026-10-15T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', later, 'Europe/Warsaw')).toBe('08/31');
        expect(wordRecentInstant(instant, 'pl', later, 'Europe/Warsaw')).toBe('31.08');
    });

    it('says the whole date for an instant of another year', () => {
        const nextYear = Date.parse('2027-01-05T10:00:00+00:00');

        expect(wordRecentInstant(instant, 'en', nextYear, 'Europe/Warsaw')).toBe('8/31/26');
    });

    // A day is the calendar's rather than a subtraction over an instant, which is what stops the hour a zone moves its
    // clock forward from turning yesterday into two days ago.
    it('says yesterday across the night the clocks go back', () => {
        const beforeTheChange = '2026-10-24T20:00:00+00:00';
        const afterIt = Date.parse('2026-10-25T12:00:00+00:00');

        expect(wordRecentInstant(beforeTheChange, 'en', afterIt, 'Europe/Warsaw')).toBe('yesterday');
    });

    it('answers with nothing where the message carries no instant this client can read', () => {
        expect(wordRecentInstant(null, 'en', readAt, 'Europe/Warsaw')).toBeNull();
        expect(wordRecentInstant('the day before yesterday', 'en', readAt, 'Europe/Warsaw')).toBeNull();
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

describe('wordDueDay', () => {
    // A Monday morning in Warsaw, which is where the reader is for all of these.
    const readingAt = Date.parse('2026-09-21T09:00:00+02:00');

    it('words the reader’s own day rather than numbering it', () => {
        expect(wordDueDay('2026-09-21', 'en', readingAt, 'Europe/Warsaw')).toBe('today');
        expect(wordDueDay('2026-09-21', 'pl', readingAt, 'Europe/Warsaw')).toBe('dzisiaj');
    });

    // The one thing this wording has to get right: which day is the reader's is a question about the zone their record
    // states, so one instant is two different days for two readers — just past midnight in Warsaw is still the
    // afternoon before in Los Angeles, and a task due on the 21st is not yet due today for the second of them.
    it('answers one instant differently for two readers on two different days', () => {
        const justPastMidnight = Date.parse('2026-09-21T00:30:00+02:00');

        expect(wordDueDay('2026-09-21', 'en', justPastMidnight, 'Europe/Warsaw')).toBe('today');
        expect(wordDueDay('2026-09-21', 'en', justPastMidnight, 'America/Los_Angeles')).not.toBe('today');
    });

    // The record decides which day is theirs, so a runtime reporting another zone changes nothing — the same defect
    // the instant wordings above close, reaching the one value that is a day rather than a moment.
    it('takes today from the record’s zone rather than from the one the runtime reports', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        const justPastMidnight = Date.parse('2026-09-21T00:30:00+02:00');

        expect(wordDueDay('2026-09-21', 'en', justPastMidnight, 'Europe/Warsaw')).toBe('today');
    });

    // Each language numbers a day its own way, which is the whole reason this asks `Intl` rather than composing the
    // two parts itself.
    it('numbers any other day of the reader’s own year', () => {
        expect(wordDueDay('2026-09-24', 'en', readingAt, 'Europe/Warsaw')).toBe('09/24');
        expect(wordDueDay('2026-09-24', 'pl', readingAt, 'Europe/Warsaw')).toBe('24.09');
    });

    it('writes a day in another year as a whole short date', () => {
        expect(wordDueDay('2027-01-04', 'en', readingAt, 'Europe/Warsaw')).toBe('1/4/27');
    });

    it('shows a day it cannot read as the value it was given rather than as an invalid date', () => {
        expect(wordDueDay('sometime in August', 'en', readingAt, 'Europe/Warsaw')).toBe('sometime in August');
    });
});
