// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { isStatableReminderLead, isStatableReminderSet } from '@mailfathom/client-backend';

// What a reminder lead *is* on a screen: which leads are offered, what one is called, and what a set of them counts
// as. The deployment holds the leads and the instants they fall at; everything here is the rendering half, which is
// why it is a module of its own rather than state inside the panel — a rule nothing can call on its own is a rule
// nothing can be asserted about either.
//
// The numbers are the design project's own. A timed event offers the moment itself and the five leads somebody
// reaches for around a meeting; a record stated as a day — an all-day event, and a task's due date — offers the
// wider set, because a day names no hour to be five minutes ahead of.

/** How many minutes there are in an hour and in a day, named so the thresholds below read as what they are. */
const minutesPerHour = 60;
const minutesPerDay = 24 * minutesPerHour;

/**
 * What a lead is measured back from, which is the one thing the panel needs to know about what it is attached to.
 *
 * Three rather than two, and the third is not a third set of presets: the design draws a task's due date with the
 * same leads and the same word for a lead of none as an all-day event, and differs only in the sentence that says
 * which hour they count back from. So the anchor is what the panel reads, and every difference between the three
 * follows from it in one place rather than from a flag each caller has to get right.
 */
export type ReminderAnchor = 'eventTime' | 'eventDay' | 'taskDueDate';

/** The leads offered on an event that names a clock time. */
export const timedReminderPresets: readonly number[] = [0, 5, 15, 30, minutesPerHour, minutesPerDay];

/** The leads offered on a record stated as a day, which has no hour for a five-minute warning to precede. */
export const datedReminderPresets: readonly number[] = [
    0,
    minutesPerHour,
    4 * minutesPerHour,
    minutesPerDay,
    2 * minutesPerDay,
];

/** The lead a new event starts with, which is the one the design project draws already pressed. */
export const defaultReminderLeads: readonly number[] = [15];

/** The units the custom entry states a lead in, in the order the design draws them. */
export const reminderUnits = ['minutes', 'hours', 'days'] as const;

/** One of the three units a lead is typed in. */
export type ReminderUnit = (typeof reminderUnits)[number];

/** How a lead reads: the whole unit it states exactly, and how many of that unit. */
export interface ReminderLeadReading {
    /** `atTheTime` is the event itself, which is a lead of none rather than one unit of anything. */
    readonly unit: 'atTheTime' | ReminderUnit;

    /** How many of that unit, and zero at the event itself. */
    readonly count: number;
}

/**
 * Reads one lead as the coarsest whole unit that states it exactly.
 *
 * The thresholds are the design project's: under an hour is minutes, under a day is hours where the lead divides into
 * them and minutes where it does not, and a whole number of days is days. A lead that divides into no coarser unit
 * stays in the finer one rather than being rounded, because a person who typed ninety minutes did not ask for an hour
 * and a half of anything.
 */
export function readReminderLead(minutesBefore: number): ReminderLeadReading {
    if (minutesBefore === 0) {
        return { unit: 'atTheTime', count: 0 };
    }

    if (minutesBefore % minutesPerDay === 0) {
        return { unit: 'days', count: minutesBefore / minutesPerDay };
    }

    if (minutesBefore % minutesPerHour === 0) {
        return { unit: 'hours', count: minutesBefore / minutesPerHour };
    }

    return { unit: 'minutes', count: minutesBefore };
}

/**
 * Turns a number and a unit somebody typed into a lead, or `null` where it is not one an event may carry.
 *
 * A fractional or non-positive number is refused rather than rounded: the field states whole units, and a person who
 * typed nothing usable is told so by the control staying where it was rather than by a lead appearing that they did
 * not ask for. Zero is reached by the preset for the event itself, which is why it is not typed here.
 */
export function reminderLeadFrom(count: number, unit: ReminderUnit): number | null {
    if (!Number.isSafeInteger(count) || count < 1) {
        return null;
    }

    const minutesBefore = unit === 'days' ? count * minutesPerDay : unit === 'hours' ? count * minutesPerHour : count;

    return isStatableReminderLead(minutesBefore) ? minutesBefore : null;
}

/** The leads the panel offers, which is the wider set for everything a clock time is not named on. */
export function reminderPresetsFor(anchor: ReminderAnchor): readonly number[] {
    return anchor === 'eventTime' ? timedReminderPresets : datedReminderPresets;
}

/**
 * Reports whether an anchor names a clock time, which is what a lead of none reads as and which presets are offered.
 *
 * A day and a due day answer alike here: both are announced at nine in the morning, and neither has a moment for a
 * reminder to be five minutes ahead of.
 */
export function reminderAnchorNamesATime(anchor: ReminderAnchor): boolean {
    return anchor === 'eventTime';
}

/**
 * Puts a set of leads into the order a person reads their own reminders in, which is the earliest warning first.
 *
 * The same order the deployment answers with, so a set written and read back does not reorder itself under the
 * reader's eye.
 */
export function orderedReminderLeads(reminders: readonly number[]): readonly number[] {
    return [...reminders].sort((one, other) => other - one);
}

/**
 * Adds a lead to a set, or answers the set unchanged where it is already there or the set is full.
 *
 * Unchanged rather than refused, because the two acts the panel performs — pressing a preset and typing a lead — are
 * both idempotent from a person's point of view: what they asked for is that the lead be set, and it is.
 */
export function withReminderLead(reminders: readonly number[], minutesBefore: number): readonly number[] {
    if (reminders.includes(minutesBefore)) {
        return reminders;
    }

    const widened = orderedReminderLeads([...reminders, minutesBefore]);

    return isStatableReminderSet(widened) ? widened : reminders;
}

/** Removes a lead from a set, which is what a chip's own control and a pressed preset both do. */
export function withoutReminderLead(reminders: readonly number[], minutesBefore: number): readonly number[] {
    return reminders.filter((lead) => lead !== minutesBefore);
}
