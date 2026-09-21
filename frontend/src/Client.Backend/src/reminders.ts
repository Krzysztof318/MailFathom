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
// Reading and writing the records themselves belongs to the modules that own those records; what is here is what
// both of them would otherwise state twice — the bounds, the two readings of an answer, and the offset a due day
// runs in.

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

/**
 * Reads a set of leads off an answer, or refuses one this deployment would not have written.
 *
 * An answer is untrusted input at a trust boundary like any other, and the bound is part of the reading rather than a
 * check after it, so a record claiming a thousand leads is refused instead of walked.
 */
export function isReminderSet(value: unknown): value is readonly number[] {
    return (
        Array.isArray(value) &&
        value.length <= mostRemindersOnOneRecord &&
        value.every((lead) => typeof lead === 'number') &&
        isStatableReminderSet(value)
    );
}

/** Reads the instants a deployment resolved those leads to, which stay text here because no screen computes one. */
export function isReminderInstantList(value: unknown): value is readonly string[] {
    return (
        Array.isArray(value) && value.length <= mostRemindersOnOneRecord && value.every((at) => typeof at === 'string')
    );
}

/**
 * The whole-minute offset from UTC that a due day runs in, read at the hour the deployment anchors reminders at.
 *
 * The deployment keeps no timezone for a person, so a task's due day is anchored in the offset its client states
 * beside the leads. It is read at that anchor rather than at the moment of writing, because a due day three weeks out
 * may fall on the other side of a daylight-saving change from today — stating today's offset would put every reminder
 * on that task an hour out.
 *
 * Answers `null` for a day this client cannot place, which is also what a caller states when it sets no reminder.
 */
export function dueDayOffsetMinutes(dueOn: string | null): number | null {
    if (dueOn === null) {
        return null;
    }

    const anchored = new Date(`${dueOn}T09:00:00`);

    return Number.isNaN(anchored.getTime()) ? null : -anchored.getTimezoneOffset();
}
