// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { PersonalTask } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { wordCalendarDay, wordDueDay } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';

// One thing a person owes, as the design project draws a task: the box that completes it, the line it is drawn with,
// the mark saying it came out of mail, the way back to the message it came out of, the day it is due, and the act that
// puts it in the day.
//
// **The mark follows the message rather than the proposal.** The design marks what a model wrote; what this client
// knows is which message a task cites, and that outlives the proposal being taken — which is when somebody is most
// likely to want to know where a line on their list came from. Whether it is still an offer is said by the act beside
// it instead.
//
// **It answers a press exactly as every other list in this client does**, through `contextMenu/rowPress.ts` rather
// than through a gesture of its own, and through `ContextMenu` above it rather than a second menu — the design gives
// all seven of its lists one behaviour.
//
// **The row is not an option, and that is a correctness decision rather than a preference.** Every other selectable
// list here is a `listbox` of rows carrying no controls; a task row carries four, and ARIA refuses interactive content
// inside an `option`. So this is an ordinary list item, and what conveys the selection is the design's own mark made
// pressable: while a selection is held every row draws it as a toggle naming the task, which is how somebody with a
// keyboard adds a fifth row to four and how a screen reader is told which rows are held. Starting the selection where
// no modifier key is being held is the menu's *Select tasks*, exactly as the design draws it.
//
// **Three things the design draws are not drawn here, and each is a record this deployment does not keep.** A task
// carries no estimate, so the duration beside the day is absent; it carries no reminders, which
// [#1572](https://github.com/Krzysztof318/MailFathom/issues/1572) is the issue for and which the panel #1610 already
// built is waiting on; and it cites a message by identity alone rather than by subject, so the link back is named for
// what it does instead of for the thread it reaches — the row deliberately carries no subject, because a task list is
// not a second place somebody's mail is drawn.
//
// **The day is written short and said long.** The design gives it the width of two words at the end of the row, which
// a spelled-out date does not fit, so what is drawn is `localization/instants.ts`'s own short wording. A bare *today*
// or *02.09* means the day something is due only because of where it sits, and position is exactly what a screen
// reader does not convey — so the sentence saying which day it is travels beside it for anybody listening rather than
// being drawn for everybody.

export function TaskRow({
    task,
    now,
    selected,
    selecting,
    scheduled,
    onToggleSelected,
    onToggleCompleted,
    onAccept,
    onOpenSource,
    onSchedule,
    onPress,
}: {
    readonly task: PersonalTask;

    /** The reader's own clock, which is what decides whether the day this is due is theirs. */
    readonly now: number;

    /** Whether this row is one of those picked out. */
    readonly selected: boolean;

    /** Whether a selection is being held at all, which is what draws the mark as something to press. */
    readonly selecting: boolean;

    /** Whether this screen has already put this task in the day, which is what the design draws in place of the act. */
    readonly scheduled: boolean;

    /** Puts this row into the selection or takes it out, which a modifier-held press and the mark both reach. */
    readonly onToggleSelected: () => void;

    readonly onToggleCompleted: () => void;

    /** Takes a task mail proposed on, or `undefined` for one the person already owes. */
    readonly onAccept: (() => void) | undefined;

    /** Opens the message this task cites, or `undefined` where it cites none. */
    readonly onOpenSource: (() => void) | undefined;

    readonly onSchedule: () => void;

    /** Opens this row's own menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const press = useRowPress(onPress);

    function pressed(event: KeyboardEvent): void {
        // The two the platform itself offers for a row's menu, so nothing this row holds is reachable only by gesture:
        // the dedicated key where a keyboard has one, and the chord where it does not.
        if (event.key !== 'ContextMenu' && !(event.key === 'F10' && event.shiftKey)) {
            return;
        }

        const box = event.currentTarget.getBoundingClientRect();

        event.preventDefault();
        onPress({ x: box.left + box.width / 2, y: box.top + box.height / 2 });
    }

    return (
        <li
            className={`flex flex-wrap items-center gap-x-3 gap-y-2.5 rounded-xl border p-3 transition ${
                selected ? 'border-accent-line bg-accent-soft' : 'border-line bg-panel hover:bg-hover'
            } ${task.completed ? 'opacity-60' : ''}`}
            onKeyDown={pressed}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(event) => {
                // The tap that follows a press which has already opened a menu does nothing, or the menu would be
                // closed by the same finger that asked for it.
                if (press.tapSuppressed()) {
                    return;
                }

                // A press on one of the row's own controls is that control's act and not a gesture on the row, so the
                // row acts only where the press landed on the row itself. Asked of the element the event came from
                // rather than of a flag a handler set, because the controls are several and a second one added later
                // would otherwise have to remember to say so.
                if (event.target instanceof Element && event.target.closest('button, input, a') !== null) {
                    return;
                }

                // A modifier held picks the row out, and so does a plain press while a selection is already held.
                // A plain press with nothing selected does nothing at all, which is what the design draws: a task has
                // nowhere to be opened to.
                if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                    onToggleSelected();
                }
            }}
        >
            <div className="flex min-w-60 flex-1 items-start gap-2.75">
                {selecting ? (
                    <button
                        type="button"
                        aria-pressed={selected}
                        aria-label={translate('tasks.selectRow', { title: task.title })}
                        className={`mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full border transition ${
                            selected
                                ? 'border-accent bg-accent text-on-accent'
                                : 'border-line-strong text-transparent hover:border-accent'
                        }`}
                        onClick={onToggleSelected}
                    >
                        <Icon name="check" className="size-3.5" />
                    </button>
                ) : null}

                <input
                    type="checkbox"
                    checked={task.completed}
                    aria-label={translate('tasks.completeRow', { title: task.title })}
                    className="mt-0.5 size-5 shrink-0 accent-accent"
                    onChange={onToggleCompleted}
                />

                <div className="flex min-w-0 flex-col gap-1.5">
                    <div className="flex flex-wrap items-baseline gap-2">
                        <span className={`text-base text-pretty ${task.completed ? 'text-muted line-through' : ''}`}>
                            {task.title}
                        </span>

                        {task.sourceMessageId === null ? null : (
                            <span className="rounded-sm bg-accent-soft px-1.5 py-0.5 text-xs text-accent-deep">
                                {translate('tasks.fromMail')}
                            </span>
                        )}
                    </div>

                    {onOpenSource === undefined ? null : (
                        <button
                            type="button"
                            className="flex items-center gap-1.5 self-start rounded-md px-1.5 py-1 text-sm text-muted transition hover:bg-hover hover:text-text"
                            onClick={onOpenSource}
                        >
                            <Icon name="open_in_new" className="size-4" />
                            {translate('tasks.openSource')}
                        </button>
                    )}
                </div>
            </div>

            <div className="flex flex-wrap items-center gap-2.5">
                {task.dueOn === null ? (
                    <span className="text-sm whitespace-nowrap text-faint">{translate('tasks.noDay')}</span>
                ) : (
                    <time dateTime={task.dueOn} className="text-sm whitespace-nowrap text-faint">
                        <span aria-hidden="true">{wordDueDay(task.dueOn, locale, now)}</span>
                        <span className="sr-only">
                            {translate('tasks.dueOn', { day: wordCalendarDay(task.dueOn, locale) })}
                        </span>
                    </time>
                )}

                {onAccept === undefined ? null : (
                    <Control label={translate('tasks.accept')} icon="check" shape="primary" onPress={onAccept} />
                )}

                {task.dueOn === null || task.completed ? null : scheduled ? (
                    <span className="rounded-4xl border border-line bg-rail px-2.5 py-1 text-sm text-muted">
                        {translate('tasks.inTheCalendar')}
                    </span>
                ) : (
                    <Control
                        label={translate('tasks.schedule')}
                        icon="calendar_month"
                        shape="accentPill"
                        onPress={onSchedule}
                    />
                )}
            </div>
        </li>
    );
}
