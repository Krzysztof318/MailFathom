// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What the deployment will accept as a record's reminders, which is a fact about the contract rather than about a
// screen — so it is stated here once and the panel asks rather than carrying a second copy of the same three rules.
//
// One module for both the records that carry reminders, because the deployment holds one rule for them: a calendar
// event and a task's due date differ in what a lead is measured back from and in nothing else, so a second copy of
// these three would be two answers to one contract.
//
// Reading and writing the records themselves arrives with their own screens: an operation nothing calls would be a
// promise about a screen that does not exist, and the reminders a panel edits reach the deployment through whatever
// that screen writes the record with.

/** The most reminders one record carries, which is the deployment's own ceiling and a refusal rather than a clamp. */
export const mostRemindersOnOneRecord = 16;

/** The longest lead a reminder may state, in minutes before what it announces, which is four weeks. */
export const longestReminderLead = 28 * 24 * 60;

/**
 * Reports whether a set of leads is one a record may carry.
 *
 * The same three rules the deployment applies — how many, how far ahead, and each one once — so a panel refuses a
 * lead as it is added rather than only once the record is written.
 */
export function isStatableReminderSet(reminders: readonly number[]): boolean {
    return (
        reminders.length <= mostRemindersOnOneRecord &&
        reminders.every(isStatableReminderLead) &&
        new Set(reminders).size === reminders.length
    );
}

/** Reports whether one lead, in minutes before what it announces, is one a reminder may state. */
export function isStatableReminderLead(minutesBefore: number): boolean {
    return Number.isSafeInteger(minutesBefore) && minutesBefore >= 0 && minutesBefore <= longestReminderLead;
}
