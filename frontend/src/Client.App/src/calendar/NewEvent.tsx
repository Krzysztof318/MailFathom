// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useRef, useState, type RefObject } from 'react';
import {
    describedAt,
    draftCalendarEvent,
    type CalendarEventRecord,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { EventFields } from './EventFields';
import { draftOn, draftedFrom, recordOf, type EventDraft } from './eventDraft';

// Putting an event on the calendar, which this client does two ways and saves one: the fields filled by hand and the
// fields filled from a sentence somebody typed. Both end at the same form, and **nothing is saved until the person has
// read what is in it** — which is the whole arrangement, because an event a model wrote and nobody looked at is the one
// thing this feature must not produce.
//
// **The description field is drawn only where the deployment reads one.** A deployment that declared no chat endpoint,
// and one whose operator turned the reading off, are the same answer to a client — there is nothing to send — and a
// field promising to take a description over a deployment that reads none would fail a person at the one moment they
// trusted it.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it.

export function NewEvent({
    asked,
    session,
    transport,
    on,
    readsDescriptions,
    onSave,
}: {
    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The day the reader is standing on, which the date field opens on rather than on nothing. */
    readonly on: Date;

    /** Whether this deployment turns a typed description into an event at all. */
    readonly readsDescriptions: boolean;

    /** Writes what is in the form, run while the dialog is still open so a refusal is reported over the fields. */
    readonly onSave: (record: CalendarEventRecord) => void;
}) {
    const { translate } = useLocalization();
    const describes = useId();

    const [described, setDescribed] = useState('');
    const [draft, setDraft] = useState<EventDraft>(() => draftOn(on));
    const [reading, setReading] = useState(false);
    const [said, setSaid] = useState<MessageKey | null>(null);

    // Which reading is the current one. The dialog can be left and opened again while a sentence is still being read,
    // and an answer arriving into a form somebody has since emptied would fill it with an event they had abandoned.
    const latest = useRef(0);

    const record = recordOf(draft);

    function emptied(): void {
        setDescribed('');
        setDraft(draftOn(on));
        setReading(false);
        setSaid(null);
        latest.current += 1;
    }

    function readDescription(): void {
        const sentence = described.trim();

        if (sentence === '' || reading) {
            return;
        }

        const asking = ++latest.current;

        setReading(true);
        setSaid(null);

        void draftCalendarEvent(session, transport, {
            description: sentence,
            writtenAt: describedAt(new Date()),
        }).then((answer) => {
            if (asking !== latest.current) {
                return;
            }

            setReading(false);

            if (answer.outcome === 'failed') {
                setSaid('calendar.describeFailed');

                return;
            }

            if (answer.value.spent) {
                setSaid('calendar.describeSpent');

                return;
            }

            if (!answer.value.drafted) {
                setSaid('calendar.describeNothing');

                return;
            }

            setDraft((standing) => draftedFrom(answer.value, standing));
            setSaid('calendar.describeDrafted');
        });
    }

    return (
        <dialog
            ref={asked}
            aria-label={translate('calendar.newEvent')}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={emptied}
        >
            <form
                className="flex flex-col"
                onSubmit={(submitting) => {
                    // Closed by hand rather than by `method="dialog"`, because Enter in a field submits with no button
                    // behind it — and a form closed that way answers with nothing, which would drop a save somebody
                    // made from the keyboard.
                    submitting.preventDefault();

                    if (record !== null) {
                        // An event written here is the reader's own, which is what `sourceMessage` being absent
                        // states: a calendar entry carrying a message is one the deployment read out of that message.
                        onSave({ ...record, sourceMessage: null });
                        asked.current?.close();
                    }
                }}
            >
                <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <Icon name="event" className="size-5 shrink-0 text-muted" />

                    <h2 className="min-w-0 flex-1 truncate text-lg font-semibold">{translate('calendar.newEvent')}</h2>

                    <SurfaceControl
                        label={translate('calendar.closeNewEvent')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                    {!readsDescriptions ? null : (
                        <div className="flex flex-col gap-2.25 rounded-xl bg-accent-soft px-3.25 py-3">
                            <label htmlFor={describes} className="flex items-center gap-2.25 text-sm text-accent-deep">
                                <span className="rounded-sm bg-accent px-1.5 py-0.5 text-2xs text-on-accent">
                                    {translate('calendar.authored')}
                                </span>
                                {translate('calendar.describeEvent')}
                            </label>

                            <input
                                id={describes}
                                type="text"
                                value={described}
                                placeholder={translate('calendar.describeExample')}
                                className={dialogField}
                                onChange={(typing) => {
                                    setDescribed(typing.target.value);
                                }}
                            />

                            <SecondaryButton
                                label={translate(reading ? 'calendar.describing' : 'calendar.describeAct')}
                                shape="compact"
                                onActivate={readDescription}
                            />

                            {said === null ? null : (
                                <p role="status" className="text-sm text-accent-deep text-pretty">
                                    {translate(said)}
                                </p>
                            )}
                        </div>
                    )}

                    <EventFields draft={draft} onDraft={setDraft} />
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
                        disabled={record === null}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100`}
                    >
                        {translate('calendar.saveEvent')}
                    </button>
                </div>
            </form>
        </dialog>
    );
}
