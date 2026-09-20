// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    acceptCalendarEvent,
    amendCalendarEvent,
    deleteCalendarEvent,
    readsCalendarDescriptions,
    recordCalendarEvent,
    type CalendarEvent,
    type CalendarEventAmendment,
    type CalendarEventRecord,
    type CalendarWriteOutcome,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { Control } from '../controls/Control';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTwoPanes, useWideWorkspace } from '../shell/useWideWorkspace';
import { AgendaView } from './AgendaView';
import { CalendarNavigation } from './CalendarNavigation';
import { movedBy, spanOf, startOfDay, type CalendarView } from './calendarSpan';
import { DayView } from './DayView';
import { EventDialog } from './EventDialog';
import { EventMenu } from './EventMenu';
import { EventSelectionBar } from './EventSelectionBar';
import type { EventActs } from './eventActs';
import { MonthView } from './MonthView';
import { NewEvent } from './NewEvent';
import { ProposedDates } from './ProposedDates';
import { useCalendarWindow } from './useCalendarWindow';
import { WeekView } from './WeekView';

// The Calendar space as the design project composes it: a toolbar, the span somebody is looking at, one of four views
// over it, and the dates their mail proposed down the side.
//
// **This screen draws; it does not derive.** Every fact on it comes from the deployment, which is why three regions the
// design draws are absent rather than invented: the briefing bar over the views and its *Plan my day* control, the
// suggested blocks of time above the proposals, and the reminders on an event. `/api/client` answers none of the
// three, and a control with no destination is left out rather than drawn inert — each arrives with the change that
// gives it a route.
//
// **Four of the five toolbar acts are absent for the same reason.** The design draws *Today*, *Find a slot*,
// *Invitation*, *Export* and *Print*; *Today* is this screen's own navigation and stands where the reader moves
// between spans, and the other four reach nothing this deployment publishes.
//
// **Below the width seven columns need, the week and the month are drawn as the agenda**, which is what the design
// project's own one-pane composition is and what the screen says out loud rather than substituting quietly. The day
// view needs one column and is unchanged at every width.
//
// **Every write is answered by reading the span again rather than by correcting what is held**, for the reason
// `useCalendarWindow.ts` gives. The one thing corrected in place is a proposal somebody has just answered: it leaves
// the column the moment the write does, because a card still offering *Add* after somebody pressed it is a card they
// press twice.

// What one write to the calendar said, worded as what happened to the event rather than as an outcome name. Exhaustive
// by its own type, so an outcome added to the surface does not compile until this says what a reader is told about it.
const writeSaid: Readonly<Record<CalendarWriteOutcome, MessageKey>> = {
    Written: 'calendar.written',
    NotFound: 'calendar.writeNotFound',
    Refused: 'calendar.writeRefused',
    AlreadyOnTheCalendar: 'calendar.alreadyOnTheCalendar',
};

// Why the span did not answer, each said as what it is with a next step rather than as a status code. A calendar the
// deployment no longer holds is not one of the outcomes this route can produce, so it is worded as the deployment not
// having answered — which is what a reader would do about it either way.
const spanFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'calendar.failedUnauthenticated',
    unauthorized: 'calendar.failedUnauthorized',
    unavailable: 'calendar.failedUnavailable',
    unreadable: 'calendar.failedUnreadable',
    missing: 'calendar.failedUnavailable',
};

// Where a sentence about a write belongs: over the calendar for one the screen as a whole answered, and inside the
// dialog for one about the event that is open. One state rather than two, because only one write is in flight at a
// time and two would eventually disagree about which was the last.
/** The event a dialog is drawing, and whether what opened it asked for its reminders rather than for the event. */
interface OpenedEvent {
    readonly event: CalendarEvent;
    readonly reminders: boolean;
}

interface WriteSaid {
    readonly where: 'calendar' | 'event';
    readonly said: MessageKey;
}

export function CalendarSpace({
    session,
    transport,
    now = () => new Date(),
    onOpenMessage,
}: {
    /** Who is asking and where, or `null` where there is nothing to ask with. */
    readonly session: ClientSession | null;

    readonly transport: MailFathomTransport;

    /** What the clock says, which a test decides for itself rather than inheriting from the machine it runs on. */
    readonly now?: () => Date;

    /** Opens one message, which is the Mail space's to draw and therefore the frame's to perform. */
    readonly onOpenMessage: (messageId: string) => void;
}) {
    const { translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const wide = useWideWorkspace();

    // The day the screen opened on, read once rather than on every render: a value read while rendering is a value
    // that differs between two renders of one screen, and what this decides — which column is tinted, where *Today*
    // goes back to — has to be one answer for as long as the reader is looking at it.
    const [today] = useState(() => startOfDay(now()));

    const [view, setView] = useState<CalendarView>('week');
    const [anchor, setAnchor] = useState(today);
    const [selected, setSelected] = useState<readonly string[]>([]);
    // The event opened, and whether what opened it asked for its reminders. One piece of state rather than two,
    // because a question asked about an event that is no longer the open one is a question about nothing.
    const [opened, setOpened] = useState<OpenedEvent | null>(null);
    const [deleting, setDeleting] = useState<readonly CalendarEvent[]>([]);
    const [answered, setAnswered] = useState<readonly string[]>([]);
    const [menu, setMenu] = useState<{ readonly event: CalendarEvent; readonly at: MenuPoint } | null>(null);
    const [said, setSaid] = useState<WriteSaid | null>(null);
    const [readsDescriptions, setReadsDescriptions] = useState(false);

    const asked = useRef<HTMLDialogElement | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);
    const opening = useRef<HTMLDialogElement | null>(null);

    const span = spanOf(view, anchor);
    const reading = useCalendarWindow(session, transport, span);

    // Whether the deployment turns a typed description into an event, which decides whether the new-event dialog draws
    // the field at all. A request going out, and therefore the one thing an effect is for.
    useEffect(() => {
        if (session === null) {
            return;
        }

        let listening = true;

        void readsCalendarDescriptions(session, transport).then((answer) => {
            if (listening) {
                setReadsDescriptions(answer.outcome === 'read' && answer.value.readsDescriptions);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport]);

    // The events picked out, read back out of the span rather than held as records: the selection holds identities and
    // the span holds what each of them is, so a selection cannot outlive a read that no longer names one of them.
    const picked = reading.events.filter((event) => selected.includes(event.id));

    // The proposals still worth answering, which is what the deployment named less whatever this reader has just
    // answered. The list is emptied by the read that follows each write.
    const standing = reading.proposals.filter((proposal) => !answered.includes(proposal.id));

    // The dialog is the platform's own, so opening it is an imperative call on an element that has to exist first —
    // which is the other thing an effect is for. It runs when the opened event changes rather than on every commit.
    // A dialog already open is never opened again: `showModal` on one throws rather than doing nothing, and an
    // amendment written from inside it — a saved field, a reminder turned on — replaces the event it is drawing
    // without closing it, so this runs on the way in and on nothing else.
    useEffect(() => {
        if (opened !== null && opening.current?.open === false) {
            opening.current.showModal();
        }
    }, [opened]);

    function readAfter(write: Promise<{ readonly said: MessageKey; readonly where: WriteSaid['where'] }>): void {
        void write.then((answer) => {
            setSaid({ where: answer.where, said: answer.said });
            reading.readAgain();
        });
    }

    function record(stated: CalendarEventRecord): void {
        if (session === null) {
            return;
        }

        readAfter(
            recordCalendarEvent(session, transport, stated).then((answer) => ({
                where: 'calendar' as const,
                said: answer.outcome === 'failed' ? 'calendar.writeFailed' : writeSaid[answer.value.outcome],
            })),
        );
    }

    function amend(event: CalendarEvent, amendment: CalendarEventAmendment): void {
        if (session === null) {
            return;
        }

        readAfter(
            amendCalendarEvent(session, transport, event.id, amendment).then((answer) => {
                if (answer.outcome !== 'failed' && answer.value.event !== null) {
                    const written = answer.value.event;

                    setOpened((opening) => (opening === null ? null : { ...opening, event: written }));
                }

                return {
                    where: 'event' as const,
                    said: answer.outcome === 'failed' ? 'calendar.writeFailed' : writeSaid[answer.value.outcome],
                };
            }),
        );
    }

    // A proposal leaves the column the moment it is answered and comes back where the write did not happen, which is
    // the whole of what this correction is: a card still offering *Add* after somebody pressed it is a card they press
    // twice, and a card gone after a refusal is a date they were told nothing about and can no longer answer.
    function answerProposal(
        proposal: CalendarEvent,
        write: Promise<{ readonly failed: boolean; readonly said: MessageKey }>,
    ): void {
        setAnswered((held) => [...held, proposal.id]);

        void write.then((answer) => {
            if (answer.failed) {
                setAnswered((held) => held.filter((event) => event !== proposal.id));
            }

            setSaid({ where: 'calendar', said: answer.said });
            reading.readAgain();
        });
    }

    function accept(proposal: CalendarEvent): void {
        if (session === null) {
            return;
        }

        answerProposal(
            proposal,
            acceptCalendarEvent(session, transport, proposal.id).then((answer) => ({
                failed: answer.outcome === 'failed',
                said: answer.outcome === 'failed' ? 'calendar.writeFailed' : writeSaid[answer.value.outcome],
            })),
        );
    }

    function dismiss(proposal: CalendarEvent): void {
        if (session === null) {
            return;
        }

        answerProposal(
            proposal,
            deleteCalendarEvent(session, transport, proposal.id).then((answer) => ({
                failed: answer.outcome === 'failed',
                said: (answer.outcome === 'failed'
                    ? 'calendar.writeFailed'
                    : 'calendar.proposalDismissed') satisfies MessageKey,
            })),
        );
    }

    // Deleting several events is several writes rather than one, so the answers are sorted rather than reduced to
    // whether any of them failed: a batch in which one refusal stood beside four deletions would otherwise say nothing
    // was changed while the calendar came back four events shorter, and would drop the refused event out of the
    // selection with no way left to ask again about it.
    function remove(events: readonly CalendarEvent[]): void {
        if (session === null) {
            return;
        }

        void Promise.all(
            events.map((event) =>
                deleteCalendarEvent(session, transport, event.id).then((answer) => ({ event, answer })),
            ),
        ).then((answers) => {
            const refused = answers.filter(({ answer }) => answer.outcome === 'failed').map(({ event }) => event.id);
            const removed = answers.filter(({ answer }) => answer.outcome !== 'failed').map(({ event }) => event.id);

            setSaid({
                where: 'calendar',
                said: refused.length === 0 ? 'calendar.deleted' : 'calendar.deleteSomeRefused',
            });

            setSelected((held) => held.filter((event) => !removed.includes(event)));

            if (opened !== null && removed.includes(opened.event.id)) {
                asked.current?.close();
                opening.current?.close();
            }

            reading.readAgain();
        });
    }

    function askDeletion(events: readonly CalendarEvent[]): void {
        if (events.length === 0) {
            return;
        }

        setDeleting(events);
        asked.current?.showModal();
    }

    const acts: EventActs = {
        selected,
        onOpen: (event) => {
            setSaid(null);
            setOpened({ event, reminders: false });
        },
        onToggle: (event) => {
            setSelected(withToggled(selected, event.id));
        },
        onPress: (event, at) => {
            setMenu({ event, at });
        },
    };

    // Which view is actually drawn, as against which one the reader chose. Seven columns and a month grid each need a
    // width a phone does not have, and a week squeezed into one is seven columns of forty pixels with every entry
    // truncated to nothing — so the span stays the chosen one and the agenda is what draws it.
    const drawnAsAgenda = !twoPanes && (view === 'week' || view === 'month');

    function openDay(day: Date): void {
        setView('day');
        setAnchor(day);
        setSelected([]);
    }

    // Four views is a choice with four answers rather than a ternary chain inside the markup, which is where a reader
    // stops being able to see the structure of the screen.
    function drawnView() {
        if (view === 'day') {
            return <DayView span={span} events={reading.events} acts={acts} />;
        }

        if (view === 'agenda' || drawnAsAgenda) {
            return <AgendaView span={span} events={reading.events} today={today} acts={acts} />;
        }

        if (view === 'week') {
            return <WeekView span={span} events={reading.events} today={today} acts={acts} />;
        }

        return (
            <MonthView
                span={span}
                anchor={anchor}
                events={reading.events}
                today={today}
                acts={acts}
                onOpenDay={openDay}
            />
        );
    }

    return (
        <div className="relative flex min-h-0 flex-1 flex-col">
            {selected.length > 0 ? (
                <EventSelectionBar
                    selected={picked}
                    onClear={() => {
                        setSelected([]);
                    }}
                    onAskDeletion={() => {
                        askDeletion(picked);
                    }}
                />
            ) : wide ? (
                <div className="flex shrink-0 items-center gap-2.25 border-b border-line bg-panel px-3 py-2">
                    <Control
                        label={translate('calendar.newEvent')}
                        icon="add"
                        shape="primary"
                        onPress={() => {
                            setSaid(null);
                            asking.current?.showModal();
                        }}
                    />
                </div>
            ) : null}

            <CalendarNavigation
                view={view}
                anchor={anchor}
                span={span}
                onView={(chosen) => {
                    setView(chosen);
                    setSelected([]);
                }}
                onMove={(spans) => {
                    setAnchor(movedBy(view, anchor, spans));
                    setSelected([]);
                }}
                onToday={() => {
                    setAnchor(today);
                    setSelected([]);
                }}
            />

            {drawnAsAgenda ? (
                <p className="shrink-0 border-b border-line bg-sunken px-4 py-1.5 text-sm text-muted text-pretty">
                    {translate('calendar.drawnAsAgenda')}
                </p>
            ) : null}

            {said?.where === 'calendar' ? (
                <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                    {translate(said.said)}
                </p>
            ) : null}

            {reading.reading ? (
                <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                    {translate('calendar.reading')}
                </p>
            ) : null}

            {reading.failure === null ? null : (
                <div className="flex shrink-0 flex-col items-start gap-2 border-b border-line bg-sunken px-4 py-2.75">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(spanFailures[reading.failure])}
                    </p>
                    <SecondaryButton
                        label={translate('calendar.readAgain')}
                        shape="compact"
                        onActivate={reading.readAgain}
                    />
                </div>
            )}

            <div className="flex min-h-0 flex-1 flex-col overflow-y-auto panes:flex-row panes:overflow-hidden">
                {drawnView()}

                <ProposedDates proposals={standing} onAccept={accept} onDismiss={dismiss} />
            </div>

            {/* The narrow composition's own way to write an event down, which is where a thumb reaches rather than at
                the top of a column: the toolbar the wide shape carries has nowhere to stand once bottom navigation has
                the foot of the window. */}
            {!wide && selected.length === 0 ? (
                <Control
                    label={translate('calendar.newEvent')}
                    icon="event"
                    shape="floating"
                    className="absolute end-4 bottom-4"
                    onPress={() => {
                        setSaid(null);
                        asking.current?.showModal();
                    }}
                />
            ) : null}

            {session === null ? null : (
                <NewEvent
                    // Keyed on the day it opens on, because the draft is seeded once and this component outlives
                    // every move between spans: a reader who walks to another week before writing anything down
                    // would otherwise be handed the day the screen was first drawn on. The dialog is modal, so the
                    // day cannot move under a form somebody is typing into.
                    key={anchor.toISOString()}
                    asked={asking}
                    session={session}
                    transport={transport}
                    on={anchor}
                    now={now}
                    readsDescriptions={readsDescriptions}
                    onSave={record}
                />
            )}

            {opened === null ? null : (
                <EventDialog
                    key={opened.event.id}
                    event={opened.event}
                    asked={opening}
                    said={said?.where === 'event' ? said.said : null}
                    remindersAsked={opened.reminders}
                    onAmend={(amendment) => {
                        amend(opened.event, amendment);
                    }}
                    onAskDeletion={() => {
                        askDeletion([opened.event]);
                    }}
                    onOpenSource={onOpenMessage}
                    onLeft={() => {
                        setOpened(null);
                    }}
                />
            )}

            {menu === null ? null : (
                <EventMenu
                    event={menu.event}
                    at={menu.at}
                    onSelect={() => {
                        setSelected(onlySelected(menu.event.id));
                    }}
                    onOpen={() => {
                        setSaid(null);
                        setOpened({ event: menu.event, reminders: false });
                    }}
                    onAskReminders={() => {
                        setSaid(null);
                        setOpened({ event: menu.event, reminders: true });
                    }}
                    onAskDeletion={() => {
                        askDeletion([menu.event]);
                    }}
                    onClose={() => {
                        setMenu(null);
                    }}
                />
            )}

            <Confirmation
                asked={asked}
                mark="delete"
                question={translate(deleting.length === 1 ? 'calendar.deleteOne' : 'calendar.deleteMany', {
                    count: deleting.length.toFixed(0),
                })}
                consequence={
                    <>
                        {deleting.map((event) => (
                            <span key={event.id} className="block truncate">
                                {event.title}
                            </span>
                        ))}
                    </>
                }
                reversal={{ kind: 'permanent', said: translate('calendar.deletePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('calendar.deleteAct'),
                        manner: 'destroy',
                        run: () => {
                            remove(deleting);
                        },
                    },
                ]}
            />
        </div>
    );
}
