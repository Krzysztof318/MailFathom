// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What the deployment will accept as an event's reminders, which is a fact about the contract rather than about a
// screen — so it is stated here once and the panel asks rather than carrying a second copy of the same three rules.
//
// Reading and writing a calendar event arrives with the Calendar screen: an operation nothing calls would be a
// promise about a screen that does not exist, and the reminders a panel edits reach the deployment through whatever
// that screen writes the event with.

/** The most reminders one event carries, which is the deployment's own ceiling and a refusal rather than a clamp. */
export const mostRemindersOnAnEvent = 16;

/** The longest lead a reminder may state, in minutes before the event, which is four weeks. */
export const longestReminderLead = 28 * 24 * 60;

/**
 * Reports whether a set of leads is one an event may carry.
 *
 * The same three rules the deployment applies — how many, how far ahead, and each one once — so a panel refuses a
 * lead as it is added rather than only once the event is written.
 */
export function isStatableReminderSet(reminders: readonly number[]): boolean {
    return (
        reminders.length <= mostRemindersOnAnEvent &&
        reminders.every(isStatableReminderLead) &&
        new Set(reminders).size === reminders.length
    );
}

/** Reports whether one lead, in minutes before the event, is one a reminder may state. */
export function isStatableReminderLead(minutesBefore: number): boolean {
    return Number.isSafeInteger(minutesBefore) && minutesBefore >= 0 && minutesBefore <= longestReminderLead;
}
