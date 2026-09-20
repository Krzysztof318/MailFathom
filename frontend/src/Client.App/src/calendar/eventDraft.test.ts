// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { CalendarEvent } from '@mailfathom/client-backend';
import { draftedFrom, draftOf, draftOn, nothingDrafted, recordOf } from './eventDraft';

// A day and a clock reading are resolved against the reader's own zone, so the zone is pinned: in UTC the whole of
// this module is the identity function and a test run there would prove none of it.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

const meeting: CalendarEvent = {
    id: 'meeting',
    title: 'Review with Anna',
    start: '2026-09-24T09:00:00+02:00',
    end: '2026-09-24T10:30:00+02:00',
    isAllDay: false,
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-01T09:00:00+02:00',
    amendedAt: '2026-09-01T09:00:00+02:00',
};

describe('draftOf', () => {
    it('reads an event back into the fields that would have produced it', () => {
        expect(draftOf(meeting)).toEqual({
            title: 'Review with Anna',
            day: '2026-09-24',
            start: '09:00',
            end: '10:30',
            allDay: false,
            reminders: [],
        });
    });

    it('reads a day back with no clock reading, because a day carries none', () => {
        const day = { ...meeting, start: '2026-09-24T00:00:00+02:00', end: null, isAllDay: true, reminders: [60] };

        expect(draftOf(day)).toEqual({
            title: 'Review with Anna',
            day: '2026-09-24',
            start: '',
            end: '',
            allDay: true,
            reminders: [60],
        });
    });

    it('reads an instant sent in another offset against the reader"s own day', () => {
        // Twenty-two o'clock UTC is midnight in Warsaw, so the day somebody sees is the twenty-fifth.
        expect(draftOf({ ...meeting, start: '2026-09-24T22:30:00Z', end: null }).day).toBe('2026-09-25');
    });

    it('states no end as an empty field rather than as a time', () => {
        expect(draftOf({ ...meeting, end: null }).end).toBe('');
    });

    it('empties the fields an unreadable instant would otherwise fill with nonsense', () => {
        expect(draftOf({ ...meeting, start: 'whenever' })).toEqual({
            ...nothingDrafted,
            title: 'Review with Anna',
        });
    });

    it('reads a day as one, with no clock reading in either time field', () => {
        expect(draftOf({ ...meeting, isAllDay: true })).toEqual({
            ...nothingDrafted,
            title: 'Review with Anna',
            day: '2026-09-24',
            allDay: true,
        });
    });

    it('reads back what announces it, which is what an amendment then carries', () => {
        expect(draftOf({ ...meeting, reminders: [15] }).reminders).toEqual([15]);
    });
});

describe('draftOn', () => {
    it('opens on the day the reader is standing on and fills nothing else', () => {
        expect(draftOn(new Date(2026, 8, 24, 15))).toEqual({ ...nothingDrafted, day: '2026-09-24' });
    });
});

describe('recordOf', () => {
    const filled = { ...nothingDrafted, title: 'Review with Anna', day: '2026-09-24', start: '09:00', end: '10:30' };

    it('resolves the day and the clock reading against the reader"s own zone', () => {
        expect(recordOf(filled)).toEqual({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24, 9).toISOString(),
            end: new Date(2026, 8, 24, 10, 30).toISOString(),
            isAllDay: false,
            reminders: [],
        });
    });

    it('takes the spaces off a title rather than writing them down', () => {
        expect(recordOf({ ...filled, title: '  Review with Anna  ' })?.title).toBe('Review with Anna');
    });

    it('states no end where none was picked', () => {
        expect(recordOf({ ...filled, end: '' })?.end).toBeNull();
    });

    it('writes a day down as midnight in the reader"s own zone and states that it is a day', () => {
        expect(recordOf({ ...filled, allDay: true, start: '', end: '' })).toEqual({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24).toISOString(),
            end: null,
            isAllDay: true,
            reminders: [],
        });
    });

    it('carries what is to announce it rather than dropping it on the way to the record', () => {
        expect(recordOf({ ...filled, reminders: [15, 60] })?.reminders).toEqual([15, 60]);
    });

    it.each([
        ['a title nobody typed', { ...filled, title: '   ' }],
        ['a day nobody picked', { ...filled, day: '' }],
        ['a beginning nobody picked', { ...filled, start: '' }],
        ['a day that is not one', { ...filled, day: '2026-02-31x' }],
        ['an end before the beginning', { ...filled, end: '08:00' }],
        ['an end at the beginning', { ...filled, end: '09:00' }],
    ])('refuses %s', (_, draft) => {
        expect(recordOf(draft)).toBeNull();
    });
});

describe('draftedFrom', () => {
    const standing = { ...nothingDrafted, title: 'Typed by hand', day: '2026-09-24', start: '08:00', end: '' };

    it('puts what the deployment read into the fields', () => {
        expect(
            draftedFrom(
                { title: 'Lunch with Anna', start: '2026-09-24T13:00:00+02:00', end: '2026-09-24T14:00:00+02:00' },
                standing,
            ),
        ).toEqual({ ...standing, title: 'Lunch with Anna', day: '2026-09-24', start: '13:00', end: '14:00' });
    });

    it('leaves a field the deployment said nothing about as the person left it', () => {
        expect(draftedFrom({ title: null, start: null, end: null }, standing)).toEqual(standing);
    });

    it('leaves a field an unreadable instant would otherwise empty', () => {
        expect(draftedFrom({ title: 'Lunch', start: 'whenever', end: null }, standing)).toEqual({
            ...standing,
            title: 'Lunch',
        });
    });
});
