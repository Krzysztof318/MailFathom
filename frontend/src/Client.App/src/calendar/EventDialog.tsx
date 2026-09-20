// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type RefObject } from 'react';
import type { CalendarEvent, CalendarEventAmendment } from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { EventFields } from './EventFields';
import { ReminderPanel } from './ReminderPanel';
import { draftOf, recordOf, type EventDraft } from './eventDraft';
import { wordReminderCount } from './reminderWords';

// One event opened, which is where it is read and where it is changed. The design project draws both in one surface —
// the record, and an *Edit* beside *Delete* — so this holds the two rather than opening a second dialog over the
// first: a reader amending a time is looking at the event they are amending.
//
// **What it says about the event is what the deployment holds and nothing derived.** The design draws a sentence about
// where the event came from; MailFathom writes none, so the mark saying the event was read out of mail is what is left
// of it. What announces the event is the deployment's own, and the panel #1610 built is what edits it — opened from
// here rather than reproduced, and answered with the whole set, which is why a change is written at once.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the entry that opened it. Whether it is open is therefore the element's
// state rather than a second copy of it, which is why the caller hands over the reference.

export function EventDialog({
    event,
    asked,
    said,
    onAmend,
    onAskDeletion,
    onOpenSource,
    onLeft,
    remindersAsked = false,
}: {
    readonly event: CalendarEvent;

    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** What the last write about this event said, or `null` where none has been made. */
    readonly said: MessageKey | null;

    readonly onAmend: (amendment: CalendarEventAmendment) => void;

    /** Raises the question the deletion stands behind, over this one event. */
    readonly onAskDeletion: () => void;

    /** Opens the message the event was read out of, which is the Mail space's to draw. */
    readonly onOpenSource: (messageId: string) => void;

    /** Says the dialog has been left, which is what lets the screen stop holding the event it was drawing. */
    readonly onLeft: () => void;

    /** Whether what opened the event asked for its reminders, which the menu's own item does. */
    readonly remindersAsked?: boolean;
}) {
    const { locale, translate } = useLocalization();

    const [amending, setAmending] = useState(false);
    const [draft, setDraft] = useState<EventDraft>(() => draftOf(event));
    const panel = useRef<HTMLDialogElement>(null);

    const record = recordOf(draft);

    // Opening the panel is an imperative call on an element, which is the one thing an effect is for here. It runs on
    // arrival because the menu item that asked for it is what mounted this dialog.
    useEffect(() => {
        if (remindersAsked) {
            panel.current?.showModal();
        }
    }, [remindersAsked]);

    function stateReminders(reminders: readonly number[]): void {
        const changed = { ...draft, reminders };
        setDraft(changed);

        // Outside the fields there is no *Save* to press, so what the panel answered is written as it is answered: the
        // route takes the whole record, and what the reader saw on leaving the panel is what the calendar then holds.
        const written = amending ? null : recordOf(changed);

        if (written !== null) {
            onAmend(written);
        }
    }

    return (
        <dialog
            ref={asked}
            aria-label={event.title}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            // The screen lets go of the event as the dialog is left, which takes this component down with it — so
            // nothing half-typed outlives the question it was typed into and there is nothing here to put back.
            onClose={onLeft}
        >
            <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                <span className="rounded-full border border-line px-2.25 py-0.5 text-2xs tracking-widest text-muted uppercase">
                    {translate(event.sourceMessage === null ? 'calendar.kindOwn' : 'calendar.kindFromMail')}
                </span>

                <span className="min-w-0 flex-1" />

                <SurfaceControl
                    label={translate('calendar.closeEvent')}
                    icon="close"
                    onActivate={() => {
                        asked.current?.close();
                    }}
                />
            </div>

            <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                {amending ? (
                    <EventFields draft={draft} onDraft={setDraft} />
                ) : (
                    <>
                        <h2 className="text-lg font-semibold tracking-tight text-pretty">{event.title}</h2>

                        <p className="text-base text-text-soft">
                            {event.isAllDay
                                ? translate('calendar.eventAllDay')
                                : wordInstantRange(event.start, event.end, locale, 'full')}
                        </p>

                        <button
                            type="button"
                            className="flex items-center gap-1.75 self-start rounded-lg px-2.75 py-1.5 text-sm text-text-soft hover:underline"
                            onClick={() => {
                                panel.current?.showModal();
                            }}
                        >
                            <Icon name="notifications" className="size-4" />
                            {wordReminderCount(draft.reminders.length, locale, translate)}
                        </button>

                        {event.sourceMessage === null ? null : (
                            <button
                                type="button"
                                className="flex items-center gap-1.75 self-start rounded-lg bg-accent-soft px-2.75 py-1.5 text-sm text-accent-deep hover:underline"
                                onClick={() => {
                                    const message = event.sourceMessage;

                                    if (message !== null) {
                                        asked.current?.close();
                                        onOpenSource(message);
                                    }
                                }}
                            >
                                <Icon name="open_in_new" className="size-4" />
                                {translate('calendar.openSource')}
                            </button>
                        )}
                    </>
                )}

                {said === null ? null : (
                    <p role="status" className="text-sm text-muted text-pretty">
                        {translate(said)}
                    </p>
                )}
            </div>

            <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                {amending ? (
                    <>
                        <SecondaryButton
                            label={translate('act.cancel')}
                            shape="form"
                            onActivate={() => {
                                setAmending(false);
                                setDraft(draftOf(event));
                            }}
                        />

                        <button
                            type="button"
                            disabled={record === null}
                            className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100`}
                            onClick={() => {
                                if (record !== null) {
                                    setAmending(false);
                                    onAmend(record);
                                }
                            }}
                        >
                            {translate('calendar.saveEvent')}
                        </button>
                    </>
                ) : (
                    <>
                        <SecondaryButton
                            label={translate('calendar.deleteEvent')}
                            shape="form"
                            onActivate={onAskDeletion}
                        />

                        <button
                            type="button"
                            className={mannerDrawn.act}
                            onClick={() => {
                                setAmending(true);
                            }}
                        >
                            {translate('calendar.amendEvent')}
                        </button>
                    </>
                )}
            </div>

            <ReminderPanel
                panel={panel}
                subject={event.title}
                allDay={event.isAllDay}
                reminders={draft.reminders}
                onRemindersChanged={stateReminders}
            />
        </dialog>
    );
}
