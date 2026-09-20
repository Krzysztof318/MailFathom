// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { daysOf, eventsOn, inTimeOrder, sameDay, type CalendarSpan } from './calendarSpan';
import { wordDay, wordDayOfMonth, wordWeekday } from './calendarWording';
import { EventEntry } from './EventEntry';
import type { EventActs } from './eventActs';

// The week as the design project draws it: seven columns side by side, each headed by its weekday and its date, with
// the day the reader is standing on tinted.
//
// It is drawn only where the composition has room for seven columns. Below that width the screen draws the same span
// as an agenda and says so, which is `CalendarSpace.tsx`'s decision rather than this component's — a week squeezed into
// a phone is seven columns of about forty pixels, which is a grid nobody can read and every entry truncated to
// nothing.

export function WeekView({
    span,
    events,
    today,
    acts,
}: {
    readonly span: CalendarSpan;
    readonly events: readonly CalendarEvent[];

    /** The day the reader is standing on, which the column for it is drawn apart from the others. */
    readonly today: Date;

    readonly acts: EventActs;
}) {
    const { locale, translate } = useLocalization();

    return (
        <div className="grid min-w-0 flex-1 grid-cols-7 panes:min-h-0 panes:overflow-y-auto">
            {daysOf(span).map((day, column) => {
                const standing = sameDay(day, today);

                return (
                    <div
                        key={day.getTime()}
                        className={`flex min-h-0 min-w-0 flex-col gap-1.75 px-2.5 py-2.75 ${
                            column === 6 ? '' : 'border-e border-line-soft'
                        } ${standing ? 'bg-accent-soft' : ''}`}
                    >
                        <p className="flex items-baseline gap-1.75 border-b border-line-soft pb-1.5">
                            <span className="text-2xs tracking-widest text-muted uppercase">
                                {wordWeekday(day, locale)}
                            </span>
                            <span
                                className={`text-base font-semibold ${
                                    standing
                                        ? 'flex size-6.5 items-center justify-center rounded-full bg-accent text-on-accent'
                                        : ''
                                }`}
                            >
                                {wordDayOfMonth(day, locale)}
                            </span>
                        </p>

                        <ul
                            role="listbox"
                            aria-multiselectable={true}
                            aria-label={translate('calendar.dayEvents', { day: wordDay(day, locale) })}
                            className="flex min-w-0 flex-col gap-1.75"
                        >
                            {inTimeOrder(eventsOn(events, day)).map((event) => (
                                <EventEntry
                                    key={event.id}
                                    event={event}
                                    shape="column"
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
                );
            })}
        </div>
    );
}
