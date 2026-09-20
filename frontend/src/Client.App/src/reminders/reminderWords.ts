// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { reminderAnchorNamesATime, readReminderLead, type ReminderAnchor, type ReminderUnit } from './reminderLeads';

// What a reminder lead is called, in the language its reader has. The deployment holds a number of minutes and says
// nothing about how it reads; every sentence a lead appears in is here, so the panel, the chips, and the row a
// notification is drawn as cannot come to word the same lead three ways.
//
// Each sentence is one entry with a `{count}` hole rather than a number joined to a noun, because Polish inflects the
// noun after the count and English hides that it inflects anything. `Intl.PluralRules` selects the form.

/** The forms one counted sentence takes, which every counted sentence below declares the same way. */
type CountedForms = Readonly<Record<Intl.LDMLPluralRule, MessageKey>>;

/** How far ahead of what it announces a lead falls, said in each unit's own forms. */
function leadForms(unit: ReminderUnit): CountedForms {
    return {
        zero: `reminders.lead.${unit}.other`,
        one: `reminders.lead.${unit}.one`,
        two: `reminders.lead.${unit}.other`,
        few: `reminders.lead.${unit}.few`,
        many: `reminders.lead.${unit}.many`,
        other: `reminders.lead.${unit}.other`,
    };
}

/** How long is left before it, said in each unit's own forms. */
function remainingForms(unit: ReminderUnit): CountedForms {
    return {
        zero: `reminders.remaining.${unit}.other`,
        one: `reminders.remaining.${unit}.one`,
        two: `reminders.remaining.${unit}.other`,
        few: `reminders.remaining.${unit}.few`,
        many: `reminders.remaining.${unit}.many`,
        other: `reminders.remaining.${unit}.other`,
    };
}

const countForms: CountedForms = {
    zero: 'reminders.count.other',
    one: 'reminders.count.one',
    two: 'reminders.count.other',
    few: 'reminders.count.few',
    many: 'reminders.count.many',
    other: 'reminders.count.other',
};

/**
 * Says how long before what it announces one reminder falls.
 *
 * A lead of none is the thing itself, and what that is called depends on what it is anchored to: a record at a clock
 * time is reminded *at the time*, and one anchored to a day — an all-day event, or a task's due date — is reminded
 * *on the day*, because a day has no time to be at.
 *
 * @param minutesBefore The lead, in minutes before what it announces.
 * @param anchor What the lead is measured back from.
 */
export function wordReminderLead(
    minutesBefore: number,
    anchor: ReminderAnchor,
    locale: Locale,
    translate: Translate,
): string {
    const lead = readReminderLead(minutesBefore);

    if (lead.unit === 'atTheTime') {
        return translate(
            reminderAnchorNamesATime(anchor) ? 'reminders.lead.atTheTime' : 'reminders.lead.onTheDay',
        );
    }

    return translate(leadForms(lead.unit)[new Intl.PluralRules(locale).select(lead.count)], {
        count: new Intl.NumberFormat(locale).format(lead.count),
    });
}

/** Says how long is left, which is what a reminder that has come due says rather than how far ahead it was set. */
export function wordReminderRemaining(minutesBefore: number, locale: Locale, translate: Translate): string {
    const lead = readReminderLead(minutesBefore);

    if (lead.unit === 'atTheTime') {
        return translate('reminders.remaining.now');
    }

    return translate(remainingForms(lead.unit)[new Intl.PluralRules(locale).select(lead.count)], {
        count: new Intl.NumberFormat(locale).format(lead.count),
    });
}

/**
 * Says how many reminders a record carries, which is what the count beside the panel's heading reads.
 *
 * A record carrying none reads as *off* rather than as none, which is the design project's own word: what it states
 * is that nothing will be raised about it, and a zero would read as a number that might change on its own.
 */
export function wordReminderCount(count: number, locale: Locale, translate: Translate): string {
    return count === 0
        ? translate('reminders.count.off')
        : translate(countForms[new Intl.PluralRules(locale).select(count)], {
              count: new Intl.NumberFormat(locale).format(count),
          });
}
