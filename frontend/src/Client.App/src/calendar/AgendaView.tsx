// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { daysOf, eventsOn, inTimeOrder, sameDay, type CalendarSpan } from './calendarSpan';
import { wordAgendaDay, wordDay, wordWeekday } from './calendarWording';
import { EventEntry } from './EventEntry';
import type { EventActs } from './eventActs';

// The span as a list of the days that have something in them, which is the design project's agenda: a date down one
// side and what is on it beside. A day with nothing on it is not a row, because a list of empty days is the calendar
// telling a reader at length that there is nothing to tell them.
//
// It is also what the week and the month are drawn as below the width seven columns need, which is what makes it the
// one view every composition has. `CalendarSpace.tsx` is where that substitution is decided and said out loud.

export function AgendaView({
    span,
    events,
    today,
    acts,
}: {
    readonly span: CalendarSpan;
    readonly events: readonly CalendarEvent[];
    readonly today: Date;
    readonly acts: EventActs;
}) {
    const { locale, translate } = useLocalization();

    const days = daysOf(span)
        .map((day) => ({ day, ofTheDay: inTimeOrder(eventsOn(events, day)) }))
        .filter(({ ofTheDay }) => ofTheDay.length > 0);

    if (days.length === 0) {
        return (
            <div className="flex min-w-0 flex-1 flex-col px-4 py-4 panes:min-h-0 panes:overflow-y-auto workspace:px-6">
                <p className="text-base text-muted text-pretty">{translate('calendar.nothingInSpan')}</p>
            </div>
        );
    }

    return (
        <div className="flex min-w-0 flex-1 flex-col gap-3.5 px-4 py-3.5 panes:min-h-0 panes:overflow-y-auto workspace:px-6">
            {days.map(({ day, ofTheDay }) => (
                <div key={day.getTime()} className="flex min-w-0 items-start gap-3.5">
                    <p className="flex w-16 shrink-0 flex-col">
                        <span className={`text-base font-semibold ${sameDay(day, today) ? 'text-accent-deep' : ''}`}>
                            {wordAgendaDay(day, locale)}
                        </span>
                        <span className="text-2xs tracking-widest text-muted uppercase">
                            {wordWeekday(day, locale)}
                        </span>
                    </p>

                    <ul
                        role="listbox"
                        aria-multiselectable={true}
                        aria-label={translate('calendar.dayEvents', { day: wordDay(day, locale) })}
                        className="flex min-w-0 flex-1 flex-col gap-1.5"
                    >
                        {ofTheDay.map((event) => (
                            <EventEntry
                                key={event.id}
                                event={event}
                                shape="agenda"
                                selected={acts.selected.includes(event.id)}
                                picking={acts.selected.length > 0}
                                onOpen={() => {
                                    acts.onOpen(event);
                                }}
                                onToggle={() => {
                                    acts.onToggle(event);
                                }}
                                onPress={(at) => {
                                    acts.onPress(event, at);
                                }}
                            />
                        ))}
                    </ul>
                </div>
            ))}
        </div>
    );
}
