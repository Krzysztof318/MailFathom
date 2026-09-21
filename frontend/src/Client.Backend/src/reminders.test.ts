// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
    dueDayOffsetMinutes,
    isReminderInstantList,
    isReminderSet,
    isStatableReminderLead,
    isStatableReminderSet,
    longestReminderLead,
    mostRemindersOnOneRecord,
} from './reminders';

describe('isStatableReminderLead', () => {
    it('accepts the event itself and the longest lead, which are the two ends the deployment allows', () => {
        expect(isStatableReminderLead(0)).toBe(true);
        expect(isStatableReminderLead(longestReminderLead)).toBe(true);
    });

    it.each([-1, longestReminderLead + 1, 1.5, Number.NaN, Number.POSITIVE_INFINITY])(
        'refuses %s, which no reminder may state',
        (minutesBefore) => {
            expect(isStatableReminderLead(minutesBefore)).toBe(false);
        },
    );
});

describe('isStatableReminderSet', () => {
    it('accepts an event that announces nothing, which is a decision rather than an omission', () => {
        expect(isStatableReminderSet([])).toBe(true);
    });

    it('accepts as many as an event may carry and refuses one more', () => {
        const full = Array.from({ length: mostRemindersOnOneRecord }, (_, at) => at + 1);

        expect(isStatableReminderSet(full)).toBe(true);
        expect(isStatableReminderSet([...full, 0])).toBe(false);
    });

    it('refuses one lead stated twice, because asking for it twice asked for one thing', () => {
        expect(isStatableReminderSet([15, 15])).toBe(false);
    });

    it('refuses a set carrying a lead no reminder may state', () => {
        expect(isStatableReminderSet([15, longestReminderLead + 1])).toBe(false);
    });
});

describe('isReminderSet', () => {
    it('reads a set a record may carry off an answer', () => {
        expect(isReminderSet([1440, 0])).toBe(true);
    });

    it.each([
        ['a record where a set belongs', { first: 60 }],
        ['a lead written down rather than counted', ['60']],
        ['more leads than a record may carry', Array.from({ length: mostRemindersOnOneRecord + 1 }, (_, at) => at)],
        ['a lead no reminder may state', [longestReminderLead + 1]],
        ['the same lead twice', [60, 60]],
        ['nothing at all', null],
    ])('refuses %s', (_, value) => {
        expect(isReminderSet(value)).toBe(false);
    });
});

describe('isReminderInstantList', () => {
    it('reads the instants a deployment resolved its leads to', () => {
        expect(isReminderInstantList(['2026-09-20T09:00:00+02:00'])).toBe(true);
    });

    it.each([
        ['an instant counted rather than written down', [1758351600]],
        ['more instants than a record may carry', Array.from({ length: mostRemindersOnOneRecord + 1 }, () => 'x')],
        ['nothing at all', null],
    ])('refuses %s', (_, value) => {
        expect(isReminderInstantList(value)).toBe(false);
    });
});

describe('dueDayOffsetMinutes', () => {
    afterEach(() => {
        vi.unstubAllEnvs();
    });

    it('reads the offset the due day runs in rather than the one today runs in', () => {
        // Summer time ends on the twenty-fifth of October 2026, so these two days run an hour apart — which is the
        // whole reason the offset is read at the day the reminders are anchored on rather than at the moment of
        // writing.
        expect(dueDayOffsetMinutes('2026-10-24', 'Europe/Warsaw')).toBe(120);
        expect(dueDayOffsetMinutes('2026-10-26', 'Europe/Warsaw')).toBe(60);
    });

    it('reads a zone behind Greenwich as the negative offset it is', () => {
        expect(dueDayOffsetMinutes('2026-09-21', 'America/New_York')).toBe(-240);
    });

    it('reads the zone the record states rather than the one this machine runs in', () => {
        vi.stubEnv('TZ', 'America/New_York');

        expect(dueDayOffsetMinutes('2026-09-21', 'Europe/Warsaw')).toBe(120);
    });

    it('reads the offset at the anchor rather than at the instant a first guess from UTC lands on', () => {
        // Summer time begins in the Aleutians on the eighth of March 2026, and nine in the morning there is the
        // previous evening in UTC — so a reading taken once, from UTC, would state the offset that ran the night
        // before the change rather than the one the reminder is actually anchored in.
        expect(dueDayOffsetMinutes('2026-03-08', 'America/Adak')).toBe(-540);
    });

    it('reads a zone at a half-hour offset as the whole minutes it is', () => {
        expect(dueDayOffsetMinutes('2026-09-21', 'Asia/Kolkata')).toBe(330);
    });

    it('falls back to this runtime where the record names a zone it does not carry', () => {
        vi.stubEnv('TZ', 'Europe/Warsaw');

        expect(dueDayOffsetMinutes('2026-09-21', 'Somewhere/Nowhere')).toBe(120);
    });

    it('falls back to this runtime where the record states no zone at all', () => {
        vi.stubEnv('TZ', 'Europe/Warsaw');

        expect(dueDayOffsetMinutes('2026-09-21', null)).toBe(120);
    });

    it('answers nothing for a task nobody dated, which is what a record stating no lead carries', () => {
        expect(dueDayOffsetMinutes(null, 'Europe/Warsaw')).toBeNull();
    });

    it('answers nothing for a day this client cannot place', () => {
        expect(dueDayOffsetMinutes('the week after next', 'Europe/Warsaw')).toBeNull();
    });
});
