// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { wordReminderCount, wordReminderLead, wordRemainingBeforeEvent } from './reminderWords';

// A lead is a number of minutes and nothing else, so this is where it becomes a sentence — asserted as the literal
// words each language reads, in both of them, because a Polish reader served the English forms is exactly the defect
// a catalogue lookup hides.

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

function lead(minutesBefore: number, allDay: boolean, locale: Locale): string {
    return wordReminderLead(minutesBefore, allDay, locale, translating(locale));
}

describe('wordReminderLead', () => {
    it('says the event itself as a time on an event that names one', () => {
        expect(lead(0, false, 'en')).toBe('at the time');
        expect(lead(0, false, 'pl')).toBe('o tej godzinie');
    });

    it('says the event itself as a day on an event stated as one, which has no time to be at', () => {
        expect(lead(0, true, 'en')).toBe('on the day');
        expect(lead(0, true, 'pl')).toBe('w tym dniu');
    });

    it('says a lead under an hour in minutes', () => {
        expect(lead(15, false, 'en')).toBe('15 min before');
        expect(lead(15, false, 'pl')).toBe('15 min wcześniej');
    });

    it('says one hour in the singular form each language has for it', () => {
        expect(lead(60, false, 'en')).toBe('1 hour before');
        expect(lead(60, false, 'pl')).toBe('1 godzinę wcześniej');
    });

    it('says a count Polish inflects differently from English in Polish own form', () => {
        expect(lead(5 * 60, false, 'en')).toBe('5 hours before');
        expect(lead(5 * 60, false, 'pl')).toBe('5 godzin wcześniej');
    });

    it('says a whole number of days in days', () => {
        expect(lead(24 * 60, true, 'en')).toBe('1 day before');
        expect(lead(2 * 24 * 60, true, 'pl')).toBe('2 dni wcześniej');
    });
});

describe('wordRemainingBeforeEvent', () => {
    it('says the event is beginning where the reminder is the event itself', () => {
        expect(wordRemainingBeforeEvent(0, 'en', translating('en'))).toBe('Starting now.');
        expect(wordRemainingBeforeEvent(0, 'pl', translating('pl'))).toBe('Zaczyna się teraz.');
    });

    it('says what is left in the coarsest whole unit the lead states exactly', () => {
        expect(wordRemainingBeforeEvent(15, 'en', translating('en'))).toBe('15 minutes left.');
        expect(wordRemainingBeforeEvent(60, 'en', translating('en'))).toBe('1 hour left.');
        expect(wordRemainingBeforeEvent(24 * 60, 'en', translating('en'))).toBe('1 day left.');
    });

    it('agrees with the noun in Polish, which English hides', () => {
        expect(wordRemainingBeforeEvent(2 * 60, 'pl', translating('pl'))).toBe('Zostały 2 godziny.');
        expect(wordRemainingBeforeEvent(5 * 60, 'pl', translating('pl'))).toBe('Zostało 5 godzin.');
    });
});

describe('wordReminderCount', () => {
    it('says an event that announces nothing is off rather than counting none', () => {
        expect(wordReminderCount(0, 'en', translating('en'))).toBe('off');
        expect(wordReminderCount(0, 'pl', translating('pl'))).toBe('wyłączone');
    });

    it('counts what an event carries in the forms its language has', () => {
        expect(wordReminderCount(1, 'en', translating('en'))).toBe('1 reminder');
        expect(wordReminderCount(3, 'en', translating('en'))).toBe('3 reminders');
        expect(wordReminderCount(1, 'pl', translating('pl'))).toBe('1 przypomnienie');
        expect(wordReminderCount(3, 'pl', translating('pl'))).toBe('3 przypomnienia');
        expect(wordReminderCount(5, 'pl', translating('pl'))).toBe('5 przypomnień');
    });
});
