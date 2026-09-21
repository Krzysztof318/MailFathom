// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { newsletterId } from './messages';

// The signed-in person's task list, as the two halves the client surface publishes it as: what they committed to
// themselves and what MailFathom read out of their mail and has not been answered yet.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. What this file adds
// beside the resting list is the states the Tasks screen has to be looked at in and cannot be reached by scrolling: a
// list with nothing on it, a task nobody has dated, a task already done, a proposal citing the message it came out of,
// and a day the deployment would not arrange.
//
// **What is due is stated against a day the consumer names rather than against a fixed one**, which is the shape the
// first rule of that section allows: a list grouped under *Today*, *This week* and *Later* is a list whose grouping is
// the question, so a corpus fixed to one calendar day would draw every row under the last heading on every day but
// one. Which day was asked about is the consumer's to decide, exactly as how far into a folder somebody has read is.
//
// Nobody here exists. Every name is invented and every host is a reserved one, exactly as it is for the mail.

/** How many tasks one page of a half holds, which is what a cursor moves by. */
export const tasksPerPage = 50;

/** The day a number of days after the one named, as a calendar day is spelled. */
function dayAfter(day: string, days: number): string {
    const at = new Date(`${day}T00:00:00`);

    at.setDate(at.getDate() + days);

    return `${String(at.getFullYear())}-${String(at.getMonth() + 1).padStart(2, '0')}-${String(at.getDate()).padStart(2, '0')}`;
}

function task(id: string, title: string, dueOn: string | null, held: Record<string, unknown> = {}) {
    return { id, title, dueOn, origin: 'Asserted', completed: false, sourceMessageId: null, ...held };
}

/**
 * What the person committed to themselves, against the day they are reading on.
 *
 * Four rows rather than one, because the four are four different things to draw: work due today, work due later this
 * week, work nobody has dated, and work already done — and the last two cannot be reached from the first by any press.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns One page of the committed half, which is the whole of it.
 */
export function committedTasks(day: string) {
    return {
        tasks: [
            task('task-tender', 'Answer the Nordwind tender', day),
            task('task-return', 'File the quarterly return', dayAfter(day, 3)),
            task('task-racking', 'Decide what to do about the racking quote', null),
            task('task-invoice', 'Send the August invoice', dayAfter(day, -2), { completed: true }),
        ],
        nextCursor: null,
    };
}

/**
 * What MailFathom read out of the mail and nobody has answered yet.
 *
 * It cites the **message** it came out of rather than the thread that message stands in, which is what the row draws
 * the way back from: a proposal a reader cannot check against what was actually written is one they cannot honestly
 * take, and a thread is not what was written.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns One page of the proposed half, which is the whole of it.
 */
export function proposedTasks(day: string) {
    return {
        tasks: [
            task('task-signatures', 'Send back the signed addendum', dayAfter(day, 1), {
                origin: 'Proposed',
                sourceMessageId: newsletterId,
            }),
        ],
        nextCursor: null,
    };
}

/** Either half with nothing on it, which is the empty state both the list and the capacity panel are drawn in. */
export const emptyTaskPage = { tasks: [], nextCursor: null };

/** What writing a task down answers with, which the screen follows by reading the list again. */
export const taskWritten = task('task-written', 'Call the warehouse back', null);

/** What marking a task done answers with. */
export const taskCompleted = task('task-tender', 'Answer the Nordwind tender', '2026-09-21', { completed: true });

/** What taking a proposal on answers with: the same line, held now rather than offered. */
export const taskAccepted = task('task-signatures', 'Send back the signed addendum', '2026-09-22', {
    sourceMessageId: newsletterId,
});

/** What erasing a task answers with, for a task the deployment was still holding. */
export const taskErased = { id: 'task-racking', erased: true };

/** Whether this deployment arranges a day at all, which decides whether the panel offers the act. */
export const daysArranged = { arrangesDays: true };

/**
 * What the deployment suggests doing with what is left of the day.
 *
 * Something placed and something left over, because the panel has a sentence for each and a suggestion that fitted
 * everything would draw only half of it.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns The arrangement, as an offer nothing has accepted.
 */
export function dayArrangement(day: string) {
    return {
        arranged: true,
        placements: [
            { taskId: 'task-tender', startAt: `${day}T12:30:00+00:00`, minutes: 45 },
            { taskId: 'task-signatures', startAt: `${day}T14:00:00+00:00`, minutes: 30 },
        ],
        notToday: ['task-return'],
    };
}

/** A day the deployment had nothing left to arrange, which is its own sentence rather than a failure. */
export const dayNotArranged = { arranged: false, placements: [], notToday: [] };
