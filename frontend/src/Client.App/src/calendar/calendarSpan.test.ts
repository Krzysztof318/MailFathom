// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { CalendarEvent } from '@mailfathom/client-backend';
import {
    addDays,
    daysOf,
    eventsInHour,
    eventsOn,
    hoursOfDay,
    inTimeOrder,
    isoWeek,
    movedBy,
    sameDay,
    spanOf,
    startOfDay,
    startOfMonth,
    startOfWeek,
} from './calendarSpan';

// Every answer here is a day in the reader's own zone, so the zone is pinned rather than inherited from the machine
// the suite runs on: a test that passed in UTC and nowhere else would be proving the opposite of what this module is
// for. Europe/Warsaw is the one used throughout, being both east of Greenwich and on daylight saving, which is what
// makes the two boundary cases below real.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

function event(id: string, start: string, end: string | null = null, title = id): CalendarEvent {
    return {
        id,
        title,
        start,
        end,
        isAllDay: false,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        sourceMessage: null,
        recordedAt: '2026-09-01T09:00:00+02:00',
        amendedAt: '2026-09-01T09:00:00+02:00',
    };
}

describe('startOfDay', () => {
    it('answers midnight in the reader"s own zone rather than in UTC', () => {
        // Twenty-two o'clock UTC is midnight in Warsaw, so a boundary built out of UTC would answer the day before.
        expect(startOfDay(new Date('2026-09-20T22:30:00Z')).getDate()).toBe(21);
    });
});

describe('startOfWeek', () => {
    it('opens the week on Monday', () => {
        expect(startOfWeek(new Date(2026, 8, 24)).getDay()).toBe(1);
    });

    it('leaves a Monday where it is', () => {
        expect(startOfWeek(new Date(2026, 8, 21))).toEqual(new Date(2026, 8, 21));
    });

    it('takes a Sunday back to the Monday six days before it rather than forward', () => {
        expect(startOfWeek(new Date(2026, 8, 27))).toEqual(new Date(2026, 8, 21));
    });
});

describe('addDays', () => {
    it('keeps midnight midnight across the end of daylight saving', () => {
        // Europe/Warsaw goes back an hour on the last Sunday of October, so seven days measured in milliseconds would
        // land at twenty-three o'clock on the Sunday rather than at midnight on the Monday.
        expect(addDays(new Date(2026, 9, 24), 7)).toEqual(new Date(2026, 9, 31));
    });
});

describe('spanOf', () => {
    it('covers one day for the day view', () => {
        expect(spanOf('day', new Date(2026, 8, 24, 15))).toEqual({
            from: new Date(2026, 8, 24),
            until: new Date(2026, 8, 25),
        });
    });

    it('covers Monday to Monday for the week view', () => {
        expect(spanOf('week', new Date(2026, 8, 24))).toEqual({
            from: new Date(2026, 8, 21),
            until: new Date(2026, 8, 28),
        });
    });

    it('covers whole weeks for the month view, so a cell either side of the month is not drawn empty', () => {
        const span = spanOf('month', new Date(2026, 8, 15));

        expect(span.from.getDay()).toBe(1);
        expect(span.from <= startOfMonth(new Date(2026, 8, 15))).toBe(true);
        expect(daysOf(span).length % 7).toBe(0);
    });

    it('covers the month itself for the agenda, which is a list rather than a grid', () => {
        expect(spanOf('agenda', new Date(2026, 8, 15))).toEqual({
            from: new Date(2026, 8, 1),
            until: new Date(2026, 9, 1),
        });
    });
});

describe('movedBy', () => {
    it('moves the day view one day', () => {
        expect(movedBy('day', new Date(2026, 8, 24, 11), 1)).toEqual(new Date(2026, 8, 25));
    });

    it('moves the week view a whole week back from wherever in the week the anchor stood', () => {
        expect(movedBy('week', new Date(2026, 8, 24), -1)).toEqual(new Date(2026, 8, 14));
    });

    it('moves the month view a month and lands on the first', () => {
        expect(movedBy('month', new Date(2026, 8, 24), 1)).toEqual(new Date(2026, 9, 1));
    });

    it('moves the agenda by a month too, both covering one', () => {
        expect(movedBy('agenda', new Date(2026, 8, 24), -2)).toEqual(new Date(2026, 6, 1));
    });
});

describe('daysOf', () => {
    it('answers every day of the span, earliest first', () => {
        const days = daysOf(spanOf('week', new Date(2026, 8, 24)));

        expect(days).toHaveLength(7);
        expect(days[0]).toEqual(new Date(2026, 8, 21));
        expect(days[6]).toEqual(new Date(2026, 8, 27));
    });
});

describe('hoursOfDay', () => {
    it('answers all twenty-four, so nothing is drawn outside the grid', () => {
        const hours = hoursOfDay(new Date(2026, 8, 24, 13));

        expect(hours).toHaveLength(24);
        expect(hours[0]?.getHours()).toBe(0);
        expect(hours[23]?.getHours()).toBe(23);
    });
});

describe('sameDay', () => {
    it('reads two instants on one local day as one day', () => {
        expect(sameDay(new Date(2026, 8, 24, 0, 1), new Date(2026, 8, 24, 23, 59))).toBe(true);
    });

    it('reads the same clock time a year apart as different days', () => {
        expect(sameDay(new Date(2026, 8, 24), new Date(2025, 8, 24))).toBe(false);
    });
});

describe('isoWeek', () => {
    it.each([
        ['the first Thursday of the year', new Date(2026, 0, 1), 1],
        ['a day in the middle of the year', new Date(2026, 8, 24), 39],
        ['the last day of the year', new Date(2026, 11, 31), 53],
        ['a January day belonging to the previous year"s last week', new Date(2027, 0, 3), 53],
    ])('counts %s', (_, day, week) => {
        expect(isoWeek(day)).toBe(week);
    });
});

describe('eventsOn', () => {
    const meeting = event('meeting', '2026-09-24T09:00:00+02:00', '2026-09-24T10:00:00+02:00');
    const overnight = event('overnight', '2026-09-24T22:00:00+02:00', '2026-09-25T01:00:00+02:00');
    const instant = event('instant', '2026-09-25T08:00:00+02:00');

    it('answers what falls on that day', () => {
        expect(eventsOn([meeting, instant], new Date(2026, 8, 24))).toEqual([meeting]);
    });

    it('answers an event in both of the days it runs across', () => {
        expect(eventsOn([overnight], new Date(2026, 8, 24))).toEqual([overnight]);
        expect(eventsOn([overnight], new Date(2026, 8, 25))).toEqual([overnight]);
    });

    it('reads an event stating no end as lasting an instant', () => {
        expect(eventsOn([instant], new Date(2026, 8, 24))).toEqual([]);
        expect(eventsOn([instant], new Date(2026, 8, 25))).toEqual([instant]);
    });

    it('leaves out an event whose beginning cannot be read rather than drawing it somewhere arbitrary', () => {
        expect(eventsOn([event('broken', 'whenever')], new Date(2026, 8, 24))).toEqual([]);
    });
});

describe('eventsInHour', () => {
    const morning = event('morning', '2026-09-24T09:15:00+02:00', '2026-09-24T11:00:00+02:00');
    const overnight = event('overnight', '2026-09-23T22:00:00+02:00', '2026-09-24T01:00:00+02:00');

    it('draws an event in the hour it begins in and in no other', () => {
        expect(eventsInHour([morning], new Date(2026, 8, 24, 9))).toEqual([morning]);
        expect(eventsInHour([morning], new Date(2026, 8, 24, 10))).toEqual([]);
    });

    it('draws an event that began before the day in the day"s first hour rather than nowhere', () => {
        expect(eventsInHour([overnight], new Date(2026, 8, 24, 0))).toEqual([overnight]);
    });
});

describe('inTimeOrder', () => {
    it('orders by when each begins', () => {
        const later = event('later', '2026-09-24T14:00:00+02:00');
        const earlier = event('earlier', '2026-09-24T09:00:00+02:00');

        expect(inTimeOrder([later, earlier]).map((one) => one.id)).toEqual(['earlier', 'later']);
    });

    it('orders two that begin together by their titles, so the list does not shuffle between reads', () => {
        const beta = event('beta', '2026-09-24T09:00:00+02:00', null, 'Beta');
        const alpha = event('alpha', '2026-09-24T09:00:00+02:00', null, 'Alpha');

        expect(inTimeOrder([beta, alpha]).map((one) => one.id)).toEqual(['alpha', 'beta']);
    });

    it('leaves what it was given alone', () => {
        const events = [event('later', '2026-09-24T14:00:00+02:00'), event('earlier', '2026-09-24T09:00:00+02:00')];

        inTimeOrder(events);

        expect(events.map((one) => one.id)).toEqual(['later', 'earlier']);
    });
});
