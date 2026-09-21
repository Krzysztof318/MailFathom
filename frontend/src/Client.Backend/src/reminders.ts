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
 * **The zone is the one the reader's record states**, and the runtime's own where it states none — the same zone every
 * date on the screen is placed in. A reminder resolved in the machine's zone while its due day was drawn in the
 * record's would announce itself at an hour the reader never chose, and nothing on the screen would say why.
 *
 * It is read at the anchor rather than at the moment of writing, because a due day three weeks out may fall on the
 * other side of a daylight-saving change from today, and stating today's offset would put every reminder on that task
 * an hour out. It is read twice for the same reason: the first reading places nine in the morning from UTC, which for
 * a zone far enough west lands on the day before, and the second places it from the offset the first one found.
 *
 * A zone this runtime's own database does not carry falls back to the runtime's own, on the reasoning
 * `Client.App/src/localization/instants.ts` states where it words a date in one: the deployment's database is what
 * validated the identifier, the two need not be the same build, and refusing here would leave a task unable to
 * announce anything at all rather than announcing an hour out.
 *
 * Answers `null` for a day this client cannot place, which is also what a caller states when it sets no reminder.
 */
export function dueDayOffsetMinutes(dueOn: string | null, timeZone: string | null): number | null {
    if (dueOn === null) {
        return null;
    }

    const fromUtc = Date.parse(`${dueOn}T09:00:00Z`);

    if (Number.isNaN(fromUtc)) {
        return null;
    }

    const named = offsetIn(fromUtc, timeZone);

    if (named === null) {
        const anchored = new Date(`${dueOn}T09:00:00`);

        return Number.isNaN(anchored.getTime()) ? null : -anchored.getTimezoneOffset();
    }

    return offsetIn(fromUtc - named * 60_000, timeZone) ?? named;
}

/** The whole-minute offset a named zone runs at one instant, or `null` where none was named or this runtime has none. */
function offsetIn(at: number, timeZone: string | null): number | null {
    if (timeZone === null) {
        return null;
    }

    let worded: string | undefined;

    try {
        worded = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'longOffset' })
            .formatToParts(at)
            .find((part) => part.type === 'timeZoneName')?.value;
    } catch {
        return null;
    }

    const stated = /^GMT(?:([+-])(\d{2}):(\d{2}))?$/u.exec(worded ?? '');

    if (stated === null) {
        return null;
    }

    if (stated[1] === undefined) {
        return 0;
    }

    const minutes = Number(stated[2]) * 60 + Number(stated[3]);

    return stated[1] === '-' ? -minutes : minutes;
}
