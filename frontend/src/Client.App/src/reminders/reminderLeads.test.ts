// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    datedReminderPresets,
    defaultReminderLeads,
    orderedReminderLeads,
    readReminderLead,
    reminderLeadFrom,
    reminderPresetsFor,
    timedReminderPresets,
    withReminderLead,
    withoutReminderLead,
} from './reminderLeads';

describe('readReminderLead', () => {
    it('reads no lead at all as the event itself rather than as a count of anything', () => {
        expect(readReminderLead(0)).toEqual({ unit: 'atTheTime', count: 0 });
    });

    it('reads a lead under an hour in minutes', () => {
        expect(readReminderLead(45)).toEqual({ unit: 'minutes', count: 45 });
    });

    it('reads a whole number of hours in hours', () => {
        expect(readReminderLead(120)).toEqual({ unit: 'hours', count: 2 });
    });

    it('keeps a lead that divides into no whole hour in minutes rather than rounding it', () => {
        expect(readReminderLead(90)).toEqual({ unit: 'minutes', count: 90 });
    });

    it('reads a whole number of days in days', () => {
        expect(readReminderLead(2 * 24 * 60)).toEqual({ unit: 'days', count: 2 });
    });
});

describe('reminderLeadFrom', () => {
    it('reads a number of minutes as itself', () => {
        expect(reminderLeadFrom(45, 'minutes')).toBe(45);
    });

    it('reads a number of hours as the minutes it is', () => {
        expect(reminderLeadFrom(3, 'hours')).toBe(180);
    });

    it('reads a number of days as the minutes it is', () => {
        expect(reminderLeadFrom(2, 'days')).toBe(2 * 24 * 60);
    });

    it('refuses a lead longer than an event may carry rather than clamping it', () => {
        expect(reminderLeadFrom(29, 'days')).toBeNull();
    });

    it.each([0, -5, 1.5, Number.NaN])('refuses %s, which is not a whole count of anything', (typed) => {
        expect(reminderLeadFrom(typed, 'minutes')).toBeNull();
    });
});

describe('reminderPresetsFor', () => {
    it('offers the leads around a clock time on an event that names one', () => {
        expect(reminderPresetsFor('eventTime')).toEqual(timedReminderPresets);
    });

    it('offers the wider leads on an event stated as a day, which has no hour to precede', () => {
        expect(reminderPresetsFor('eventDay')).toEqual(datedReminderPresets);
    });

    it('offers a due date the same leads as a day, which is what the design draws', () => {
        expect(reminderPresetsFor('taskDueDate')).toEqual(datedReminderPresets);
    });

    it('offers the due-date leads the design names, in the design order', () => {
        expect(datedReminderPresets).toEqual([0, 60, 4 * 60, 24 * 60, 2 * 24 * 60]);
    });

    it('starts a new event with the one lead the design draws already pressed', () => {
        expect(defaultReminderLeads).toEqual([15]);
    });
});

describe('a set of leads', () => {
    it('reads earliest warning first, which is the order the deployment answers with', () => {
        expect(orderedReminderLeads([15, 1440, 0, 60])).toEqual([1440, 60, 15, 0]);
    });

    it('adds a lead into that order', () => {
        expect(withReminderLead([60, 15], 1440)).toEqual([1440, 60, 15]);
    });

    it('answers unchanged where the lead is already set, because asking for it twice asked for one thing', () => {
        const set = [60, 15];

        expect(withReminderLead(set, 60)).toBe(set);
    });

    it('answers unchanged where the event already carries as many as it may', () => {
        const full = orderedReminderLeads(Array.from({ length: 16 }, (_, at) => at + 1));

        expect(withReminderLead(full, 100)).toBe(full);
    });

    it('removes the lead named and leaves the rest where they were', () => {
        expect(withoutReminderLead([1440, 60, 15], 60)).toEqual([1440, 15]);
    });

    it('removes nothing where the lead was never set', () => {
        expect(withoutReminderLead([1440, 15], 60)).toEqual([1440, 15]);
    });
});
