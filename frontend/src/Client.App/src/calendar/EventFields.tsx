// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId } from 'react';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { useLocalization } from '../localization/useLocalization';
import type { EventDraft } from './eventDraft';

// The four fields an event is, drawn once for the two dialogs that hold them: writing one down and amending one are
// the same four values, and two arrangements of them is how one form comes to accept what the other refuses.
//
// **The day and the two times are the platform's own fields.** A date input and a time input already know the
// reader's own calendar and clock, offer the platform's picker on a phone, and are reachable from a keyboard without
// this client writing a line of it — which is the whole of what the design project's two free-text boxes, *Day (e.g.
// 28)* and *Time (e.g. 14:00)*, were standing in for. That is a correction owed to the design rather than a pair of
// text boxes to reproduce.

export function EventFields({
    draft,
    onDraft,
}: {
    readonly draft: EventDraft;
    readonly onDraft: (draft: EventDraft) => void;
}) {
    const { translate } = useLocalization();
    const titles = useId();
    const days = useId();
    const starts = useId();
    const ends = useId();
    const allDay = useId();

    return (
        <>
            <div className="flex flex-col gap-1.5">
                <label htmlFor={titles} className={dialogFieldLabel}>
                    {translate('calendar.eventTitle')}
                </label>

                <input
                    id={titles}
                    type="text"
                    value={draft.title}
                    className={dialogField}
                    onChange={(typing) => {
                        onDraft({ ...draft, title: typing.target.value });
                    }}
                />
            </div>

            <div className="flex flex-col gap-1.5">
                <label htmlFor={days} className={dialogFieldLabel}>
                    {translate('calendar.eventDay')}
                </label>

                <input
                    id={days}
                    type="date"
                    value={draft.day}
                    className={dialogField}
                    onChange={(typing) => {
                        onDraft({ ...draft, day: typing.target.value });
                    }}
                />
            </div>

            <div className="flex items-center gap-2.25">
                <input
                    id={allDay}
                    type="checkbox"
                    checked={draft.allDay}
                    className="size-4.5 accent-accent"
                    onChange={(ticking) => {
                        // A day carries no clock reading, so turning it on empties the two fields rather than leaving
                        // values the record would not state and the form would then have to explain.
                        onDraft(
                            ticking.target.checked
                                ? { ...draft, allDay: true, start: '', end: '' }
                                : { ...draft, allDay: false },
                        );
                    }}
                />

                <label htmlFor={allDay} className="text-sm text-text-soft">
                    {translate('calendar.eventAllDay')}
                </label>
            </div>

            {draft.allDay ? null : (
                <div className="flex flex-wrap gap-2.75">
                    <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                        <label htmlFor={starts} className={dialogFieldLabel}>
                            {translate('calendar.eventStart')}
                        </label>

                        <input
                            id={starts}
                            type="time"
                            value={draft.start}
                            className={dialogField}
                            onChange={(typing) => {
                                onDraft({ ...draft, start: typing.target.value });
                            }}
                        />
                    </div>

                    <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                        <label htmlFor={ends} className={dialogFieldLabel}>
                            {translate('calendar.eventEnd')}
                        </label>

                        <input
                            id={ends}
                            type="time"
                            value={draft.end}
                            className={dialogField}
                            onChange={(typing) => {
                                onDraft({ ...draft, end: typing.target.value });
                            }}
                        />
                    </div>
                </div>
            )}
        </>
    );
}
