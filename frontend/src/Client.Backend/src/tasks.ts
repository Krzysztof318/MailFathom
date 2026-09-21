// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The signed-in person's own task list, which this surface serves as two halves rather than as one listing with a
// filter: what somebody has committed to and what mail suggested and nobody has agreed to yet are different things to
// whoever is reading them, and the service publishes a route for each. A cursor is refused in the half it was not
// issued for, so the two walks cannot be spliced.
//
// Nothing here names a user. Which list is reached is resolved from the session, so a task somebody else holds answers
// exactly as one nobody holds.
//
// **Dismissing a proposal is the erasure rather than a route of its own.** Declining what mail suggested and deleting
// something a person owes leave the same list behind, so the surface publishes one act and a screen puts its own word
// on the control.
//
// **Arranging a day is the one thing here that leaves the deployment.** It is reached under the asking grant rather
// than the reading one, it is a capability a screen asks about before offering it at all, and a deployment that has
// spent what it allows a provider answers it with a value rather than a failure — the shape `mailReplyDrafting.ts`
// already stands in, and for its reason: somebody pressed a control and has to be told what became of it.

/** The route the acting person's committed tasks are listed and written to, relative to the client prefix. */
export const tasksRoute = '/tasks';

/** The route the tasks mail proposed and nobody has accepted are listed at, relative to the client prefix. */
export const proposedTasksRoute = '/tasks/proposed';

/** The route the acting person's day is arranged at, relative to the client prefix. */
export const todayLayoutRoute = '/tasks/today/layout';

/** The route one task is revised and erased at, relative to the client prefix. */
export function taskRoute(taskId: string): string {
    return `${tasksRoute}/${encodeURIComponent(taskId)}`;
}

/** The route one task's completion state is stated on, relative to the client prefix. */
export function taskCompletionRoute(taskId: string): string {
    return `${taskRoute(taskId)}/completion`;
}

/** The route a proposed task is taken on at, relative to the client prefix. */
export function taskAcceptanceRoute(taskId: string): string {
    return `${taskRoute(taskId)}/acceptance`;
}

/** Whether the person committed to a task themselves or mail proposed it to them. */
export type PersonalTaskOrigin = 'Asserted' | 'Proposed';

/** One thing a person owes. */
export interface PersonalTask {
    readonly id: string;
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or `null` where nobody has said when. */
    readonly dueOn: string | null;

    readonly origin: PersonalTaskOrigin;
    readonly completed: boolean;

    /** The message it was read out of or written beside, or `null` where it cites none. */
    readonly sourceMessageId: string | null;
}

/** One bounded page of one half of the list, and where the walk continues. */
export interface PersonalTaskPage {
    readonly tasks: readonly PersonalTask[];

    /**
     * The cursor the following page is asked with, or `null` at the end of the half.
     *
     * The absent cursor is the end of the walk rather than a page that happened to be short, so a caller stops when
     * this stops instead of comparing the count against the size it asked for.
     */
    readonly nextCursor: string | null;
}

/** The line and the day a person states for one task of their own. */
export interface PersonalTaskRecord {
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or `null` to say nothing about when. */
    readonly dueOn: string | null;

    /** The message the task cites, or `null` where it cites none. */
    readonly sourceMessageId: string | null;
}

/** Where one task is suggested to sit in the day. */
export interface DayLayoutPlacement {
    readonly taskId: string;

    /** When it is suggested to begin, as the instant the deployment stated. */
    readonly startAt: string;

    /** How long it is suggested to take. */
    readonly minutes: number;
}

/** How an arrangement of a day ended. */
export type DayLayoutOutcome = 'Arranged' | 'NotArranged' | 'AllowanceSpent';

/**
 * An arrangement of one day, offered and applied to nothing.
 *
 * `AllowanceSpent` is a value rather than a failure for the reason drafting a reply states one: it is the deployment
 * having spent what its operator allows a provider, which is neither a defect in this client nor something a reader
 * repairs by signing in again or retrying at once — and the person pressed a control, so a screen has to say what
 * became of it rather than draw nothing.
 */
export interface DayLayout {
    readonly outcome: DayLayoutOutcome;

    /** What to do and when, earliest first, which is empty unless the day was arranged. */
    readonly placements: readonly DayLayoutPlacement[];

    /** The tasks that do not realistically fit the day, by identity. */
    readonly notToday: readonly string[];
}

/** What erasing a task removed, which carries no title and no day by design. */
export interface PersonalTaskErasure {
    readonly id: string;
    readonly erased: boolean;
}

/** The most tasks one page may name, which is the deployment's own bound rather than a preference. */
export const mostTasksPerPage = 200;

/** How many a screen asks for where it states no window of its own, which is the size the service serves by default. */
export const tasksPerPage = 50;

/** The longest line a task may carry, which the deployment refuses rather than truncates. */
export const longestTaskTitle = 200;

// A task is a line, a day, two flags and two identifiers. Generous against that arithmetic over a full page and well
// under the transport's own backstop.
const longestTaskPage = 256 * 1024;

// One task and one erasure answer are each a single short record.
const longestTaskAnswer = 8 * 1024;

// An arrangement names identities, instants and durations and repeats no line of anybody's list, so it is smaller than
// a page of the list it arranges.
const longestLayoutAnswer = 64 * 1024;

const taskOrigins: readonly PersonalTaskOrigin[] = ['Asserted', 'Proposed'];

/** Reads one page of what the person has committed to, soonest due first. */
export function readOwnTasks(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null } = {},
): Promise<ClientResult<PersonalTaskPage>> {
    return readHalf(session, transport, tasksRoute, page);
}

/** Reads one page of what mail proposed and the person has not accepted, soonest due first. */
export function readProposedTasks(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null } = {},
): Promise<ClientResult<PersonalTaskPage>> {
    return readHalf(session, transport, proposedTasksRoute, page);
}

/** Records a task the person has just committed to, which is written as one they asserted. */
export function recordTask(
    session: ClientSession,
    transport: MailFathomTransport,
    record: PersonalTaskRecord,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}`, async () =>
        taskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, tasksRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(record),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/**
 * States whether one task stands completed.
 *
 * A `404` here is `missing` rather than `unavailable`, which is the reading `failureReasonForStatus` leaves to a route
 * that names one thing: a task erased while somebody had the list open is let go of, where a deployment that is down
 * is retried.
 */
export function setTaskCompletion(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
    completed: boolean,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}/{taskId}/completion`, async () =>
        taskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, taskCompletionRoute(taskId)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ completed }),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/** Takes on a task mail proposed, so it becomes one the person owes rather than one they were offered. */
export function acceptTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}/{taskId}/acceptance`, async () =>
        taskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, taskAcceptanceRoute(taskId)),
                headers: headersFor(session),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/** Erases one task, which is what both deleting something owed and declining something proposed reach. */
export function eraseTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
): Promise<ClientResult<PersonalTaskErasure>> {
    return spanned(`DELETE ${tasksRoute}/{taskId}`, async () => {
        const response = await send(transport, {
            method: 'DELETE',
            path: routeFor(session, taskRoute(taskId)),
            headers: headersFor(session),
            longestAnswer: longestTaskAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const erasure = parseErasure(bodyOf(response));

        return erasure === null ? failed('unreadable', response.status) : read(erasure);
    });
}

/**
 * Says whether this deployment arranges a day at all.
 *
 * It resolves a registration and calls no provider, so a screen asking it once costs nothing and reads nobody's list.
 * A screen asks it because a control promising an arrangement over a deployment that composes none fails a person at
 * the one moment they trusted it.
 */
export function arrangesDays(session: ClientSession, transport: MailFathomTransport): Promise<ClientResult<boolean>> {
    return spanned(`GET ${todayLayoutRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, todayLayoutRoute),
            headers: headersFor(session),
            longestAnswer: longestTaskAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const arranges = asRecord(bodyOf(response))?.['arrangesDays'];

        return typeof arranges === 'boolean' ? read(arranges) : failed('unreadable', response.status);
    });
}

/**
 * Asks for an arrangement of the day the caller's own client drew.
 *
 * The window is stated rather than derived, exactly as it is for reading the calendar and for the same reason: this
 * deployment keeps no timezone for a person, so which hours are their day is something only their client knows.
 *
 * Nothing is applied by this. What comes back is an offer, and writing any of it is the calendar's own act.
 */
export function layOutToday(
    session: ClientSession,
    transport: MailFathomTransport,
    day: { readonly from: string; readonly until: string },
): Promise<ClientResult<DayLayout>> {
    return spanned(`POST ${todayLayoutRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, todayLayoutRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({ from: day.from, until: day.until }),
            longestAnswer: longestLayoutAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 429) {
            return read<DayLayout>({ outcome: 'AllowanceSpent', placements: [], notToday: [] });
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const layout = parseLayout(bodyOf(response));

        return layout === null ? failed('unreadable', response.status) : read(layout);
    });
}

function readHalf(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    page: { readonly pageSize?: number; readonly cursor?: string | null },
): Promise<ClientResult<PersonalTaskPage>> {
    // The window the screen can render rather than whatever the service would serve, and never more than the bound the
    // route enforces — a request past it is served the maximum, which would leave the client refusing an answer larger
    // than the number it thought it had asked for.
    const pageSize = Math.min(page.pageSize ?? tasksPerPage, mostTasksPerPage);

    return spanned(`GET ${route}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, route)}?${pageArguments(pageSize, page.cursor ?? null)}`,
            headers: headersFor(session),
            longestAnswer: longestTaskPage,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const half = parsePage(bodyOf(response), pageSize);

        return half === null ? failed('unreadable', response.status) : read(half);
    });
}

// Written out rather than composed with `URLSearchParams`, which is a browser API and therefore one this package
// declares nothing of: the wire half of the client knows a route and a status code and nothing about a document.
function pageArguments(pageSize: number, cursor: string | null): string {
    const asked = [`pageSize=${pageSize.toFixed(0)}`];

    if (cursor !== null && cursor !== '') {
        asked.push(`cursor=${encodeURIComponent(cursor)}`);
    }

    return asked.join('&');
}

function bodyOf(response: ClientResponse): unknown {
    try {
        return JSON.parse(response.body);
    } catch {
        return null;
    }
}

// The three writes that answer with one task read the same way, including the `404` a route naming one task has its
// own reading of.
function taskOf(response: ClientResponse | null): ClientResult<PersonalTask> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const task = parseTask(bodyOf(response));

    return task === null ? failed('unreadable', response.status) : read(task);
}

function parsePage(value: unknown, asked: number): PersonalTaskPage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const entries = record['tasks'];
    const nextCursor = record['nextCursor'] ?? null;

    if (!Array.isArray(entries) || entries.length > asked) {
        return null;
    }

    if (nextCursor !== null && typeof nextCursor !== 'string') {
        return null;
    }

    const tasks: PersonalTask[] = [];
    for (const entry of entries) {
        const task = parseTask(entry);
        if (task === null) {
            return null;
        }

        tasks.push(task);
    }

    return { tasks, nextCursor };
}

function parseLayout(value: unknown): DayLayout | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const arranged = record['arranged'];
    const entries = record['placements'];
    const notToday = record['notToday'];

    if (typeof arranged !== 'boolean' || !Array.isArray(entries) || !Array.isArray(notToday)) {
        return null;
    }

    // Bounded during the walk rather than after it, and against the page bound the list itself carries: an arrangement
    // places tasks, so an answer naming more of them than a page of the list may hold was never an arrangement.
    if (entries.length > mostTasksPerPage || notToday.length > mostTasksPerPage) {
        return null;
    }

    const placements: DayLayoutPlacement[] = [];
    for (const entry of entries) {
        const placement = parsePlacement(entry);
        if (placement === null) {
            return null;
        }

        placements.push(placement);
    }

    for (const task of notToday) {
        if (typeof task !== 'string') {
            return null;
        }
    }

    return {
        outcome: arranged ? 'Arranged' : 'NotArranged',
        placements,
        notToday: notToday as readonly string[],
    };
}

function parsePlacement(value: unknown): DayLayoutPlacement | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const taskId = record['taskId'];
    const startAt = record['startAt'];
    const minutes = record['minutes'];

    if (typeof taskId !== 'string' || typeof startAt !== 'string') {
        return null;
    }

    return Number.isSafeInteger(minutes) && typeof minutes === 'number' && minutes > 0
        ? { taskId, startAt, minutes }
        : null;
}

function parseErasure(value: unknown): PersonalTaskErasure | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const erased = record['erased'];

    return typeof id === 'string' && typeof erased === 'boolean' ? { id, erased } : null;
}

/** Reads one task off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseTask(value: unknown): PersonalTask | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const title = record['title'];
    const dueOn = record['dueOn'] ?? null;
    const origin = record['origin'];
    const completed = record['completed'];
    const sourceMessageId = record['sourceMessageId'] ?? null;

    if (typeof id !== 'string' || typeof title !== 'string' || typeof completed !== 'boolean') {
        return null;
    }

    if (!isTaskOrigin(origin)) {
        return null;
    }

    if (dueOn !== null && typeof dueOn !== 'string') {
        return null;
    }

    if (sourceMessageId !== null && typeof sourceMessageId !== 'string') {
        return null;
    }

    // The line is held against the bound the deployment states for it, because a title past it was never a task this
    // deployment wrote — and a row drawn from one would be this client rendering an answer it should have refused.
    if (title.length > longestTaskTitle) {
        return null;
    }

    return { id, title, dueOn, origin, completed, sourceMessageId };
}

/** Whether the value is one of the two origins this surface publishes. */
export function isTaskOrigin(value: unknown): value is PersonalTaskOrigin {
    return typeof value === 'string' && taskOrigins.includes(value as PersonalTaskOrigin);
}
