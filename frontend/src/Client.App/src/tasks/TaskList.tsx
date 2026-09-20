// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState } from 'react';
import type { ClientFailureReason, PersonalTask } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { TaskRow } from './TaskRow';
import { TaskRowMenu } from './TaskRowMenu';
import { groupedTasks, type TaskGroupName } from './taskGrouping';

// The list the design project draws down the middle of the Tasks screen: work grouped by when it is due, each heading
// carrying how much stands under it.
//
// **A page is asked for from the scroll that reaches the end of what is read**, which is the design project's own
// scrolling rather than a control at the foot. The wait for it is drawn under the rows instead of replacing them,
// because a reader who has scrolled to the end of what is read has not left what they were reading.
//
// ponytail: every row read so far is in the document. The two halves page fifty at a time and the headings are runs
// of one ordered walk, so windowing this means a window over headings and rows of two different heights rather than
// the one height `messageRows/rowWindow.ts` is arithmetic over. Window it when a list somebody actually holds passes
// the two hundred rows `frontend/src/AGENTS.md` names — a task list is tens of rows where a mailbox is hundreds of
// thousands, which is why the mail list was windowed on the day it was written and this is not.

const groupLabels: Readonly<Record<TaskGroupName, MessageKey>> = {
    today: 'tasks.groupToday',
    thisWeek: 'tasks.groupThisWeek',
    later: 'tasks.groupLater',
};

// Why the list did not answer, each said as what it is with a next step rather than as a status code. A task the
// deployment no longer holds is not one of the five a paged read produces, so it is worded as the deployment not
// having answered — which is what a reader would do about it either way.
const listFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'tasks.failedUnauthenticated',
    unauthorized: 'tasks.failedUnauthorized',
    unavailable: 'tasks.failedUnavailable',
    unreadable: 'tasks.failedUnreadable',
    missing: 'tasks.failedUnavailable',
};

export function TaskList({
    tasks,
    reading,
    paging,
    failure,
    everythingDone,
    now,
    selected,
    scheduled,
    onSelected,
    onToggleCompleted,
    onAccept,
    onOpenSource,
    onSchedule,
    onAskErasure,
    onReadMore,
    onReadAgain,
}: {
    /** What to draw, which is what the list holds narrowed by whatever the toolbar is filtering out. */
    readonly tasks: readonly PersonalTask[];

    /** Whether nothing has answered yet, which is the wait the list says it is in. */
    readonly reading: boolean;

    /** Whether a further page is on its way, which is the wait drawn at the foot of the rows already there. */
    readonly paging: boolean;

    /** Why the list did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether the list holds work that is all done and being filtered out, which is a different empty from no work. */
    readonly everythingDone: boolean;

    /** When the reader is reading, which decides which heading each task stands under. */
    readonly now: number;

    /** Which tasks are picked out, in the order this list draws them. The selection is this list's own. */
    readonly selected: readonly string[];

    /** Which tasks this screen has already put in the day. */
    readonly scheduled: readonly string[];

    readonly onSelected: (selected: readonly string[]) => void;
    readonly onToggleCompleted: (task: PersonalTask) => void;
    readonly onAccept: (task: PersonalTask) => void;

    /** Opens the message a task cites, which is the Mail space's to draw and therefore the frame's to perform. */
    readonly onOpenSource: (messageId: string) => void;

    readonly onSchedule: (tasks: readonly PersonalTask[]) => void;

    /** Raises the question an erasure stands behind, over the tasks it is about. */
    readonly onAskErasure: (tasks: readonly PersonalTask[]) => void;

    /** Asks for the page after the ones held, which reaching the foot of the list is what calls. */
    readonly onReadMore: () => void;

    /** Reads the list again from the leading end, which is the way out of a failure. */
    readonly onReadAgain: () => void;
}) {
    const { locale, translate } = useLocalization();
    const headings = useId();
    const [menu, setMenu] = useState<{ readonly task: PersonalTask; readonly at: MenuPoint } | null>(null);

    const groups = groupedTasks(tasks, now);
    const counted = new Intl.NumberFormat(locale);

    // Whether a scroll has reached the rows already read, which is what asks the deployment for the page after them.
    // It is read off the scroll rather than off a render, because a read going out is something a person's gesture
    // caused rather than something a commit should start.
    function scrolled(element: HTMLDivElement): void {
        if (element.scrollHeight - element.scrollTop - element.clientHeight <= element.clientHeight) {
            onReadMore();
        }
    }

    function acts(task: PersonalTask) {
        const source = task.sourceMessageId;

        return {
            onToggleCompleted: () => {
                onToggleCompleted(task);
            },
            onAccept:
                task.origin === 'Proposed'
                    ? () => {
                          onAccept(task);
                      }
                    : undefined,
            onOpenSource:
                source === null
                    ? undefined
                    : () => {
                          onOpenSource(source);
                      },
            onSchedule: () => {
                onSchedule([task]);
            },
        };
    }

    return (
        <div
            className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-4 py-5 workspace:px-6"
            onScroll={(event) => {
                scrolled(event.currentTarget);
            }}
        >
            {groups.map((group) => (
                <section key={group.name} aria-labelledby={`${headings}-${group.name}`} className="flex flex-col gap-2.25">
                    <div className="flex items-center gap-2.25">
                        <h2
                            id={`${headings}-${group.name}`}
                            className="text-xs tracking-widest text-muted uppercase"
                        >
                            {translate(groupLabels[group.name])}
                        </h2>
                        <span className="rounded-4xl border border-line bg-rail px-1.75 text-xs text-muted">
                            {counted.format(group.tasks.length)}
                        </span>
                    </div>

                    <ul className="flex flex-col gap-2">
                        {group.tasks.map((task) => (
                            <TaskRow
                                key={task.id}
                                task={task}
                                now={now}
                                selected={selected.includes(task.id)}
                                selecting={selected.length > 0}
                                scheduled={scheduled.includes(task.id)}
                                onToggleSelected={() => {
                                    onSelected(withToggled(selected, task.id));
                                }}
                                onPress={(at) => {
                                    setMenu({ task, at });
                                }}
                                {...acts(task)}
                            />
                        ))}
                    </ul>
                </section>
            ))}

            {reading ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.reading')}
                </p>
            ) : null}

            {paging ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.readingMore')}
                </p>
            ) : null}

            {failure === null ? null : (
                <div className="flex flex-col items-start gap-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(listFailures[failure])}
                    </p>
                    <SecondaryButton label={translate('tasks.readAgain')} shape="compact" onActivate={onReadAgain} />
                </div>
            )}

            {/* Two empties rather than one: a list with nothing in it and a list whose every row is done and being
                filtered out are different states, and a reader told the first when it is the second would go looking
                for work the deployment is holding. */}
            {!reading && failure === null && tasks.length === 0 ? (
                <p className="text-sm text-muted text-pretty">
                    {translate(everythingDone ? 'tasks.allDone' : 'tasks.empty')}
                </p>
            ) : null}

            {menu === null ? null : (
                <TaskRowMenu
                    task={menu.task}
                    at={menu.at}
                    onSelect={() => {
                        onSelected(onlySelected(menu.task.id));
                    }}
                    onAskErasure={() => {
                        onAskErasure([menu.task]);
                    }}
                    onClose={() => {
                        setMenu(null);
                    }}
                    {...acts(menu.task)}
                />
            )}
        </div>
    );
}
