// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState, type RefObject } from 'react';
import { longestTaskTitle } from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';

// Writing a task down, which is the one way this client puts something into the list. What it records is asserted by
// whoever typed it, which is not a field on this form and never could be: the surface reads the origin off who is
// writing rather than off what they send.
//
// **Two fields rather than the five the design project draws.** A line and a day are what a task record holds on this
// deployment. A duration is not a field of one, so the estimate beside the day is absent; the reminders block is
// [#1572](https://github.com/Krzysztof318/MailFathom/issues/1572)'s, the panel that edits them being built already and
// waiting on a route to write them with; and the block that would read a task out of a typed sentence has no route on
// this surface — the calendar has one for an event and the task list has none, which is a correction owed to the design
// rather than a field to invent here.
//
// **The save is refused before the request where the client can tell**, which is an empty line and one past the bound
// the deployment states. Everything else the list judges is an outcome the screen reports rather than a rule restated
// here.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.
//
// **Nothing here asks for focus**, which is `confirmation/Confirmation.tsx`'s arrangement and is deliberate: a dialog
// that is mounted whether or not it is open, carrying an element that asks for focus on mount, takes focus from
// whatever the reader was on the moment the screen it stands in is drawn. `showModal` places focus for itself.

/** What a person has typed into the form, which is the whole of what a task this client writes carries. */
export interface TaskDraft {
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or the empty string where nobody has said when. */
    readonly dueOn: string;
}

const nothingTyped: TaskDraft = { title: '', dueOn: '' };

export function NewTask({
    asked,
    onSave,
}: {
    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** Records what was typed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (draft: TaskDraft) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();
    const titles = useId();
    const days = useId();

    const [draft, setDraft] = useState<TaskDraft>(nothingTyped);

    const line = draft.title.trim();
    const savable = line !== '' && line.length <= longestTaskTitle;

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would write the task again.
                dialog.returnValue = '';

                if (answer === 'save' && line !== '' && line.length <= longestTaskTitle) {
                    onSave({ title: line, dueOn: draft.dueOn });
                }

                // The form is emptied on the way out rather than on the way in, so a dialog opened again is a blank
                // one and nothing of what was typed outlives the question it was typed into.
                setDraft(nothingTyped);
            }}
        >
            <form
                className="flex flex-col"
                onSubmit={(submitting) => {
                    // Closed by hand rather than by `method="dialog"`, because Enter in a field submits with no button
                    // behind it — and a form closed that way answers with nothing, which would drop a save somebody
                    // made from the keyboard.
                    submitting.preventDefault();

                    if (savable) {
                        asked.current?.close('save');
                    }
                }}
            >
                <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <Icon name="task_alt" className="size-5 shrink-0 text-muted" />

                    <h2 id={asks} className="min-w-0 flex-1 truncate text-lg font-semibold">
                        {translate('tasks.newTask')}
                    </h2>

                    <SurfaceControl
                        label={translate('tasks.closeNewTask')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={titles} className={dialogFieldLabel}>
                            {translate('tasks.taskTitle')}
                        </label>

                        <input
                            id={titles}
                            type="text"
                            maxLength={longestTaskTitle}
                            value={draft.title}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, title: typing.target.value });
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={days} className={dialogFieldLabel}>
                            {translate('tasks.taskDueOn')}
                        </label>

                        {/* The platform's own day field rather than a picker package, which is the rule
                            `frontend/AGENTS.md` states about a component library: what it answers with is already the
                            `yyyy-mm-dd` the surface takes, in whatever spelling the reader's own locale draws. */}
                        <input
                            id={days}
                            type="date"
                            value={draft.dueOn}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, dueOn: typing.target.value });
                            }}
                        />
                    </div>
                </div>

                <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>

                    <button
                        type="submit"
                        disabled={!savable}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100`}
                    >
                        {translate('tasks.saveTask')}
                    </button>
                </div>
            </form>
        </dialog>
    );
}
