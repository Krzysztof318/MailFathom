// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { isStatableReminderLead, isStatableReminderSet, longestReminderLead, mostRemindersOnAnEvent } from './calendar';

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
        const full = Array.from({ length: mostRemindersOnAnEvent }, (_, at) => at + 1);

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
