// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { daysOf, eventsOn, inTimeOrder, sameDay, type CalendarSpan } from './calendarSpan';
import { wordDay, wordDayOfMonth, weekdayNames } from './calendarWording';
import { EventEntry } from './EventEntry';
import type { EventActs } from './eventActs';

// The month as a grid of whole weeks, which is the view the design project draws for a reader looking for a shape
// rather than for an hour. The days either side of the month are in the grid and are drawn quieter, because a week is
// seven days whichever month they belong to and a cell left blank would hide an event that is there.
//
// The date in each cell is a control rather than a label: pressing it opens that day. That is the design project's own
// gesture — it draws a day's own list under the grid when a cell is picked on a phone — answered at every width by the
// view the client already has for exactly that reading, rather than by a second list that only one composition draws.
//
// Like the week, it is drawn only where the composition has room for seven columns.

export function MonthView({
    span,
    anchor,
    events,
    today,
    acts,
    onOpenDay,
}: {
    readonly span: CalendarSpan;

    /** The month being drawn, which is what decides which cells belong to it. */
    readonly anchor: Date;

    readonly events: readonly CalendarEvent[];
    readonly today: Date;
    readonly acts: EventActs;

    /** Opens one day of the grid, which is what the date in a cell is. */
    readonly onOpenDay: (day: Date) => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <div className="flex min-w-0 flex-1 flex-col px-4 py-2.75 panes:min-h-0 panes:overflow-y-auto workspace:px-6">
            <div aria-hidden="true" className="grid grid-cols-7 gap-1">
                {weekdayNames(span, locale).map((weekday) => (
                    <p key={weekday} className="px-1.5 pb-1.5 text-2xs tracking-widest text-muted uppercase">
                        {weekday}
                    </p>
                ))}
            </div>

            <div className="grid grid-cols-7 gap-1">
                {daysOf(span).map((day) => {
                    const inMonth = day.getMonth() === anchor.getMonth();
                    const standing = sameDay(day, today);

                    return (
                        <div
                            key={day.getTime()}
                            className={`flex min-h-month-cell min-w-0 flex-col gap-1 rounded-lg border border-line p-1.5 ${
                                standing ? 'bg-accent-soft' : inMonth ? 'bg-panel' : 'bg-sunken'
                            }`}
                        >
                            <button
                                type="button"
                                aria-label={translate('calendar.openDay', { day: wordDay(day, locale) })}
                                className={`self-start rounded-md px-1.5 py-0.75 text-sm font-semibold hover:bg-hover ${
                                    inMonth ? '' : 'text-faint'
                                }`}
                                onClick={() => {
                                    onOpenDay(day);
                                }}
                            >
                                {wordDayOfMonth(day, locale)}
                            </button>

                            <ul
                                role="listbox"
                                aria-multiselectable={true}
                                aria-label={translate('calendar.dayEvents', { day: wordDay(day, locale) })}
                                className="flex min-w-0 flex-col gap-0.75"
                            >
                                {inTimeOrder(eventsOn(events, day)).map((event) => (
                                    <EventEntry
                                        key={event.id}
                                        event={event}
                                        shape="cell"
                                        selected={acts.selected.includes(event.id)}
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
                    );
                })}
            </div>
        </div>
    );
}
