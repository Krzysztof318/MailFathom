// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { calendarDayOf, type PersonalTask } from '@mailfathom/client-backend';

// When work is due, as the three headings the design project draws the list under. It is a rendering decision and
// therefore the application's: the deployment answers one ordered walk, soonest due first with the undated last, and
// which heading a day falls under is what a reader is shown rather than something the service has an opinion about.
//
// **A day is read against the reader's own day.** A task's due date is a calendar day somebody picked rather than an
// instant, so it is compared as one — the same reading `wordCalendarDay` gives it, and the reason neither passes a
// timezone to anything: the reader's runtime is where their day is.
//
// **Overdue work stands under *Today*.** The design draws three groups and none of them is *Overdue*, and today is
// when something that was due on Tuesday has to be done. A fourth heading would be a screen this project does not
// draw.
//
// **A task nobody has dated stands under *Later***, which is the same reading the service walks it in: last, after
// everything that has a day. *Later* is therefore *not today and not this week* rather than *further off than this
// week*, and that is what keeps the three headings exhaustive over a list whose days are optional.

/** Which heading a task stands under. */
export type TaskGroupName = 'today' | 'thisWeek' | 'later';

/** One heading and the tasks under it, in the order the deployment walked them. */
export interface TaskGroup {
    readonly name: TaskGroupName;
    readonly tasks: readonly PersonalTask[];
}

// The order the design draws the headings in, which is also the order the walk arrives in.
const groupOrder: readonly TaskGroupName[] = ['today', 'thisWeek', 'later'];

// How far past today *this week* reaches. Six days after today, so the heading covers a rolling week rather than the
// remainder of a calendar one — which is what stops a task moving from *This week* to *Today* because it is Sunday
// evening rather than because anything about the task changed.
const daysInTheWeekAhead = 6;

/**
 * Two halves of the list read as one, soonest due first and the undated last.
 *
 * The surface serves what somebody committed to and what mail proposed as two walks, and the design draws one list
 * with a mark on the proposals — so the two are put back into one order here. Each walk already arrives in this order,
 * so what this does is interleave them, and a sort that keeps equal days in the order they were given is what makes a
 * proposal and a commitment due on one day draw in a stable place rather than swapping between renders.
 *
 * @param committed What the person has committed to, as the deployment walked it.
 * @param proposed What mail proposed and nobody has accepted, as the deployment walked it.
 * @returns One list in the order a reader is shown it.
 */
export function inDueOrder(
    committed: readonly PersonalTask[],
    proposed: readonly PersonalTask[],
): readonly PersonalTask[] {
    return [...committed, ...proposed].sort((left, right) => {
        if (left.dueOn === right.dueOn) {
            return 0;
        }

        // A task nobody has dated is last rather than first, which is where the deployment walks it and where a
        // reader looks for work nobody has promised a day for.
        if (left.dueOn === null) {
            return 1;
        }

        return right.dueOn === null ? -1 : left.dueOn.localeCompare(right.dueOn);
    });
}

/**
 * The tasks under the three headings the design draws, with a heading nothing falls under left out.
 *
 * @param tasks The tasks, in the order the deployment walked them.
 * @param now When the reader is reading, which decides what *today* is.
 * @returns The headings that have something under them, in the order they are drawn.
 */
export function groupedTasks(tasks: readonly PersonalTask[], now: number): readonly TaskGroup[] {
    const at = new Date(now);
    const today = calendarDayOf(at);

    // Built by moving the day rather than by adding milliseconds, so a week that crosses a daylight-saving change is
    // still six days rather than six days less an hour.
    const weekAhead = calendarDayOf(new Date(at.getFullYear(), at.getMonth(), at.getDate() + daysInTheWeekAhead));

    return groupOrder
        .map((name) => ({ name, tasks: tasks.filter((task) => groupOf(task, today, weekAhead) === name) }))
        .filter((group) => group.tasks.length > 0);
}

/**
 * The heading one task stands under.
 *
 * @param task The task.
 * @param today The reader's own day, as `yyyy-mm-dd`.
 * @param weekAhead The last day *this week* reaches, as `yyyy-mm-dd`.
 * @returns Which heading it is drawn under.
 */
export function groupOf(task: PersonalTask, today: string, weekAhead: string): TaskGroupName {
    if (task.dueOn === null) {
        return 'later';
    }

    // Compared as text, which is what `yyyy-mm-dd` is for: the spelling sorts in the order the days do, so neither end
    // of this needs a date to be built and no reading of one can drift by a zone.
    if (task.dueOn <= today) {
        return 'today';
    }

    return task.dueOn <= weekAhead ? 'thisWeek' : 'later';
}
