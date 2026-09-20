// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { spanOf } from './calendarSpan';
import {
    weekdayNames,
    wordAgendaDay,
    wordDay,
    wordDayOfMonth,
    wordHour,
    wordMonth,
    wordSpan,
    wordWeekday,
} from './calendarWording';

// Every assertion here is a literal spelling rather than a comparison against a formatter built the same way, for the
// reason `localization/instants.test.ts` states: the second passes for a module that named a zone or a calendar of its
// own as happily as for one that asked the locale, which is the whole thing being tested.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

const thursday = new Date(2026, 8, 24);

describe('wordMonth', () => {
    it('names the month and the year in English', () => {
        expect(wordMonth(thursday, 'en')).toBe('September 2026');
    });

    it('names them in Polish, where the month is a word the catalogue does not carry', () => {
        expect(wordMonth(thursday, 'pl')).toBe('wrzesień 2026');
    });
});

describe('wordSpan', () => {
    it('says the whole day under the day view', () => {
        expect(wordSpan('day', spanOf('day', thursday), 'en')).toBe('Thursday, September 24');
    });

    // The thin spaces around the dash are the locale's own and are written here rather than trimmed away: a client
    // that assembled the range itself would have neither, which is exactly the difference this asserts.
    it('says the range under the week view, with the dash and the spacing the language uses', () => {
        expect(wordSpan('week', spanOf('week', thursday), 'en')).toBe('September 21\u2009\u2013\u200927');
    });

    it('says the range in Polish word order rather than in English order with Polish words', () => {
        expect(wordSpan('week', spanOf('week', thursday), 'pl')).toBe('21\u201327 września');
    });

    it.each(['month', 'agenda'] as const)('says nothing under the %s view, the heading having said it', (view) => {
        expect(wordSpan(view, spanOf(view, thursday), 'en')).toBeNull();
    });
});

describe('wordDayOfMonth', () => {
    it('answers the day"s own number', () => {
        expect(wordDayOfMonth(thursday, 'en')).toBe('24');
    });
});

describe('wordWeekday', () => {
    it('answers the short weekday in each language', () => {
        expect(wordWeekday(new Date(2026, 8, 21), 'en')).toBe('Mon');
        expect(wordWeekday(new Date(2026, 8, 21), 'pl')).toBe('pon.');
    });
});

describe('wordAgendaDay', () => {
    it('answers the day and the short month', () => {
        expect(wordAgendaDay(thursday, 'en')).toBe('Sep 24');
    });
});

describe('wordHour', () => {
    it('reads the hour the way each language reads a clock', () => {
        expect(wordHour(new Date(2026, 8, 24, 13), 'en')).toBe('1:00 PM');
        expect(wordHour(new Date(2026, 8, 24, 13), 'pl')).toBe('13:00');
    });
});

describe('wordDay', () => {
    it('says the whole day, which is what a region is named by', () => {
        expect(wordDay(thursday, 'en')).toBe('Thursday, September 24, 2026');
    });
});

describe('weekdayNames', () => {
    it('answers the seven names in the order the grid draws its columns, opening on Monday', () => {
        expect(weekdayNames(spanOf('month', thursday), 'en')).toEqual([
            'Mon',
            'Tue',
            'Wed',
            'Thu',
            'Fri',
            'Sat',
            'Sun',
        ]);
    });
});
