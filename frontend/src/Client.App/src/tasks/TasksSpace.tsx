// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    acceptTask,
    arrangesDays,
    recordCalendarEvent,
    eraseTask,
    layOutToday,
    recordTask,
    setTaskCompletion,
    type CalendarEventWrite,
    type ClientResult,
    type ClientSession,
    type DayLayout,
    type MailFathomTransport,
    type PersonalTask,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { DayCapacity } from './DayCapacity';
import { NewTask, type TaskDraft } from './NewTask';
import { TaskList } from './TaskList';
import { TaskSelectionBar } from './TaskSelectionBar';
import { TodayCalendar } from './TodayCalendar';
import { dayAround, endAfter, startOfDay } from './dayInstants';
import { groupedTasks } from './taskGrouping';
import { useTasks } from './useTasks';
import { useTodayCalendar } from './useTodayCalendar';

// The Tasks space as the design project composes it: the grouped list down the middle, the day's own calendar and the
// day's capacity beside it, and the acts that change the list held here rather than in any of them.
//
// **The sidebar stands beside the list where there is room and under it where there is not.** It is never dropped: a
// reader on a phone scrolls past the list to it, which is what `frontend/src/AGENTS.md` means by content that a narrow
// width cannot show at once being reached rather than hidden.
//
// **Every write is answered by reading the list again rather than by correcting what is held.** Where a task sorts is
// the deployment's, accepting one moves it between the two halves entirely, and completing one changes what the
// capacity panel counts — so a list edited in place would be this client's own opinion of an ordering it does not own.
// What a write leaves behind instead is one sentence saying what happened.
//
// **What the toolbar carries is two acts rather than the five the design draws.** *Find in mail* reaches the Agent
// space, which is still a placeholder; *Export* has no route on this surface, exactly as exporting the address book
// has none and for the same reason. Each is left out rather than drawn as a control that would do nothing, which is a
// correction owed to the design rather than a toolbar to fill.
//
// **Putting a task in the calendar and arranging the day are two different acts.** A task states a day and not a time,
// so *schedule in the calendar* writes the day it is due — an event with no hours claimed — and nothing here invents a
// time for it. *Lay out today* is the other one: the deployment composes where each thing could go, this draws the
// offer, and the calendar is written only once somebody accepts it.

// What one write said, worded as what happened rather than as an outcome name.
interface WriteSaid {
    readonly said: MessageKey;
    readonly names?: Readonly<Record<string, string>>;
}

// How many writes in a batch the deployment refused, in the forms a language has for the noun, and how many tasks a
// question is about. Both count something a person is reading, which is what `Intl.PluralRules` is for.
const refusedCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.someRefused.other',
    one: 'tasks.someRefused.one',
    two: 'tasks.someRefused.other',
    few: 'tasks.someRefused.few',
    many: 'tasks.someRefused.many',
    other: 'tasks.someRefused.other',
};

const erasureQuestions: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.eraseQuestion.other',
    one: 'tasks.eraseQuestion.one',
    two: 'tasks.eraseQuestion.other',
    few: 'tasks.eraseQuestion.few',
    many: 'tasks.eraseQuestion.many',
    other: 'tasks.eraseQuestion.other',
};

// Whether one event actually reached the calendar. A `ClientResult` says the request was answered; what the calendar
// did with the record is the answer's own outcome, and every value but `Written` is a row this screen must not draw as
// scheduled.
function wasWritten(answer: ClientResult<CalendarEventWrite>): boolean {
    return answer.outcome === 'read' && answer.value.outcome === 'Written';
}

export function TasksSpace({
    session,
    transport,
    asksDeployment,
    onOpenMessage,
}: {
    /** Who is asking and where, or `null` where there is nothing to ask with. */
    readonly session: ClientSession | null;

    readonly transport: MailFathomTransport;

    /** Whether this credential may ask the deployment anything, which the arranged day is reached under. */
    readonly asksDeployment: boolean;

    /** Opens the message a task cites, which is the Mail space's to draw and therefore the frame's to perform. */
    readonly onOpenMessage: (messageId: string) => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();

    // Read once for the life of the screen, which is what `controls/ReceivedAt.tsx` does and for its reason: a heading
    // that recomputed on every render would move a task from *This week* to *Today* under a reader's cursor. Every act
    // that needs the actual moment reads it in the handler that runs, which is where a clock belongs.
    const [now] = useState(() => Date.now());

    const [selected, setSelected] = useState<readonly string[]>([]);
    const [scheduled, setScheduled] = useState<readonly string[]>([]);
    const [showDone, setShowDone] = useState(false);
    const [erasing, setErasing] = useState<readonly PersonalTask[]>([]);
    const [said, setSaid] = useState<WriteSaid | null>(null);
    const [arranges, setArranges] = useState<{ readonly asked: ClientSession | null; readonly answered: boolean }>({
        asked: null,
        answered: false,
    });
    const [arranging, setArranging] = useState(false);
    const [applying, setApplying] = useState(false);
    const [layout, setLayout] = useState<DayLayout | null>(null);

    const asked = useRef<HTMLDialogElement | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);

    const reading = useTasks(session, transport);
    const day = useTodayCalendar(session, transport, now);

    // Whether this deployment arranges a day at all, which decides whether the panel offers the act rather than
    // whether the act then works. A request going out is the one thing an effect is for, and the answer is dropped
    // where the screen has stopped listening. Nothing is written here where there is nothing to ask with: what the
    // answer was asked under is kept beside it and read against the current credential below, so a screen that has
    // changed hands offers nothing until the new credential has answered for itself.
    useEffect(() => {
        if (session === null || !asksDeployment) {
            return;
        }

        let listening = true;

        void arrangesDays(session, transport).then((answer) => {
            if (listening && answer.outcome === 'read') {
                setArranges({ asked: session, answered: answer.value });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asksDeployment]);

    const arrangesThisDay = session !== null && asksDeployment && arranges.asked === session && arranges.answered;

    // What is done is out of the way unless somebody asks for it, which is what the design's own toolbar switch
    // decides. It is a filter over what is held rather than a second read: the surface serves one list and which of it
    // a reader wants to look at is a rendering decision.
    const shown = showDone ? reading.tasks : reading.tasks.filter((task) => !task.completed);

    // The tasks picked out, read back out of the list rather than held as records: the selection holds identities and
    // the list holds what each of them is, so a selection cannot outlive a read that no longer names one of them.
    const picked = shown.filter((task) => selected.includes(task.id));

    // What the capacity panel counts: work due today that is still open, read off the same headings the list draws so
    // that the number under the panel and the rows under *Today* can never disagree.
    const dueToday =
        groupedTasks(
            reading.tasks.filter((task) => !task.completed),
            now,
        ).find((group) => group.name === 'today')?.tasks.length ?? 0;

    function after(answers: readonly { readonly failed: boolean }[], done: MessageKey): void {
        const refused = answers.filter((answer) => answer.failed).length;

        setSaid(
            refused === 0
                ? { said: done }
                : refused === answers.length
                  ? { said: 'tasks.writeFailed' }
                  : {
                        said: refusedCounted[new Intl.PluralRules(locale).select(refused)],
                        names: { count: new Intl.NumberFormat(locale).format(refused) },
                    },
        );

        reading.readAgain();
    }

    function toggleCompleted(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        void setTaskCompletion(session, transport, task.id, !task.completed).then((answer) => {
            after([{ failed: answer.outcome === 'failed' }], task.completed ? 'tasks.markedNotDone' : 'tasks.markedDone');
        });
    }

    function complete(tasks: readonly PersonalTask[]): void {
        if (session === null || tasks.length === 0) {
            return;
        }

        void Promise.all(
            tasks.map((task) =>
                setTaskCompletion(session, transport, task.id, true).then((answer) => ({
                    failed: answer.outcome === 'failed',
                })),
            ),
        ).then((answers) => {
            setSelected([]);
            after(answers, 'tasks.markedDone');
        });
    }

    function accept(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        void acceptTask(session, transport, task.id).then((answer) => {
            after([{ failed: answer.outcome === 'failed' }], 'tasks.accepted');
        });
    }

    // Erasing several tasks is several writes rather than one, so the answers are counted rather than reduced to
    // whether any of them failed: a batch in which one refusal stood beside four erasures would otherwise say nothing
    // was changed while the list came back four rows shorter.
    function erase(tasks: readonly PersonalTask[]): void {
        if (session === null || tasks.length === 0) {
            return;
        }

        void Promise.all(
            tasks.map((task) =>
                eraseTask(session, transport, task.id).then((answer) => ({ failed: answer.outcome === 'failed' })),
            ),
        ).then((answers) => {
            setSelected([]);
            setErasing([]);
            after(answers, 'tasks.erased');
        });
    }

    // Putting tasks in the calendar on the days they are due. A task states a day rather than a time, so what is
    // written claims no hours — the day-long event the calendar already has a shape for — and a task nobody has dated
    // has no day to be put on, which is why the control is absent on one and this says so for a selection.
    function schedule(tasks: readonly PersonalTask[]): void {
        const dated = tasks.filter((task) => task.dueOn !== null);

        if (session === null || dated.length === 0) {
            setSaid({ said: 'tasks.nothingToSchedule' });

            return;
        }

        void Promise.all(
            dated.map((task) => {
                const start = startOfDay(task.dueOn ?? '');

                return start === null
                    ? Promise.resolve({ failed: true, id: task.id })
                    : recordCalendarEvent(session, transport, {
                          title: task.title,
                          start,
                          end: null,
                          isAllDay: true,
                          reminders: [],
                          sourceMessage: task.sourceMessageId,
                      }).then((answer) => ({ failed: !wasWritten(answer), id: task.id }));
            }),
        ).then((answers) => {
            setScheduled((standing) => [
                ...standing,
                ...answers.filter((answer) => !answer.failed).map((answer) => answer.id),
            ]);
            setSelected([]);
            day.readAgain();
            after(answers, 'tasks.scheduled');
        });
    }

    function arrange(): void {
        if (session === null || arranging) {
            return;
        }

        const window = dayAround(Date.now());

        setArranging(true);
        setSaid(null);

        // From now rather than from this morning: what is being arranged is the rest of the day, and hours already
        // past are not free time anybody can be given.
        void layOutToday(session, transport, { from: new Date().toISOString(), until: window.until }).then((answer) => {
            setArranging(false);
            setLayout(answer.outcome === 'failed' ? null : answer.value);

            if (answer.outcome === 'failed') {
                setSaid({ said: 'tasks.arrangeFailed' });
            }
        });
    }

    function apply(): void {
        if (session === null || layout === null || applying) {
            return;
        }

        const placements = layout.placements;

        setApplying(true);

        void Promise.all(
            placements.map((placement) => {
                const end = endAfter(placement.startAt, placement.minutes);
                const task = reading.tasks.find((held) => held.id === placement.taskId);

                return task === undefined || end === null
                    ? Promise.resolve({ failed: true, id: placement.taskId })
                    : recordCalendarEvent(session, transport, {
                          title: task.title,
                          start: placement.startAt,
                          end,
                          isAllDay: false,
                          reminders: [],
                          sourceMessage: task.sourceMessageId,
                      }).then((answer) => ({ failed: !wasWritten(answer), id: placement.taskId }));
            }),
        ).then((answers) => {
            setApplying(false);
            setLayout(null);
            setScheduled((standing) => [
                ...standing,
                ...answers.filter((answer) => !answer.failed).map((answer) => answer.id),
            ]);
            day.readAgain();
            after(answers, 'tasks.scheduled');
        });
    }

    function record(draft: TaskDraft): void {
        if (session === null) {
            return;
        }

        void recordTask(session, transport, {
            title: draft.title,
            dueOn: draft.dueOn === '' ? null : draft.dueOn,
            sourceMessageId: null,
        }).then((answer) => {
            after([{ failed: answer.outcome === 'failed' }], 'tasks.written');
        });
    }

    function askErasure(tasks: readonly PersonalTask[]): void {
        if (tasks.length === 0) {
            return;
        }

        setErasing(tasks);
        asked.current?.showModal();
    }

    return (
        <div className="relative flex min-h-0 flex-1 flex-col">
            {selected.length > 0 ? (
                <TaskSelectionBar
                    selected={picked}
                    onClear={() => {
                        setSelected([]);
                    }}
                    onComplete={() => {
                        complete(picked);
                    }}
                    onSchedule={() => {
                        schedule(picked);
                    }}
                    onAskErasure={() => {
                        askErasure(picked);
                    }}
                />
            ) : (
                <div className="flex shrink-0 flex-wrap items-center gap-2.25 border-b border-line bg-panel px-3 py-2">
                    {wide ? (
                        <Control
                            label={translate('tasks.newTask')}
                            icon="add"
                            shape="primary"
                            onPress={() => {
                                setSaid(null);
                                asking.current?.showModal();
                            }}
                        />
                    ) : null}

                    <Control
                        label={translate('tasks.showDone')}
                        icon="check_circle"
                        shape={showDone ? 'selected' : 'labelled'}
                        pressed={showDone}
                        onPress={() => {
                            setShowDone(!showDone);
                        }}
                    />
                </div>
            )}

            {said === null ? null : (
                <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                    {translate(said.said, said.names)}
                </p>
            )}

            <div className="flex min-h-0 flex-1 flex-col overflow-y-auto panes:flex-row panes:overflow-visible">
                <TaskList
                    tasks={shown}
                    reading={reading.reading}
                    paging={reading.paging}
                    failure={reading.failure}
                    everythingDone={!showDone && shown.length === 0 && reading.tasks.length > 0}
                    now={now}
                    selected={selected}
                    scheduled={scheduled}
                    onSelected={setSelected}
                    onToggleCompleted={toggleCompleted}
                    onAccept={accept}
                    onOpenSource={onOpenMessage}
                    onSchedule={schedule}
                    onAskErasure={askErasure}
                    onReadMore={reading.readMore}
                    onReadAgain={reading.readAgain}
                />

                {/* Beside the list where there is room and under it where there is not, and reached by scrolling
                    either way — a panel dropped at a narrow width would take the day off the screen entirely. */}
                <aside className="flex shrink-0 flex-col gap-3.5 border-line bg-sunken px-5 py-5 panes:w-tasks-side panes:overflow-y-auto panes:border-s">
                    <TodayCalendar day={day} />

                    <DayCapacity
                        dueToday={dueToday}
                        eventsToday={day.events.length}
                        offered={arrangesThisDay}
                        arranging={arranging}
                        layout={layout}
                        placed={reading.tasks}
                        applying={applying}
                        onArrange={arrange}
                        onApply={apply}
                        onPutDown={() => {
                            setLayout(null);
                        }}
                    />
                </aside>
            </div>

            {/* The narrow composition's own way to write a task down, which is where a thumb reaches rather than at the
                top of a column: the toolbar the wide shape carries has nowhere to stand once bottom navigation has the
                foot of the window. */}
            {!wide && selected.length === 0 ? (
                <Control
                    label={translate('tasks.newTask')}
                    icon="add_task"
                    shape="floating"
                    className="absolute end-4 bottom-4"
                    onPress={() => {
                        setSaid(null);
                        asking.current?.showModal();
                    }}
                />
            ) : null}

            <NewTask asked={asking} onSave={record} />

            <Confirmation
                asked={asked}
                mark="delete"
                question={translate(erasureQuestions[new Intl.PluralRules(locale).select(erasing.length)], {
                    count: new Intl.NumberFormat(locale).format(erasing.length),
                })}
                consequence={
                    <>
                        {erasing.map((task) => (
                            <span key={task.id} className="block truncate">
                                {task.title}
                            </span>
                        ))}
                    </>
                }
                reversal={{ kind: 'permanent', said: translate('tasks.erasePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('tasks.eraseAct'),
                        manner: 'destroy',
                        run: () => {
                            erase(erasing);
                        },
                    },
                ]}
            />
        </div>
    );
}
