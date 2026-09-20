// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { eventsInHour, eventsOn, hoursOfDay, inTimeOrder, type CalendarSpan } from './calendarSpan';
import { wordHour } from './calendarWording';
import { EventEntry } from './EventEntry';
import type { EventActs } from './eventActs';

// One day down its own hours, which is the view the design project draws for a reader who has stopped on a day rather
// than scanning a week. It is the one view that works at every width, a single column being what a phone has.
//
// **Every hour of the day is drawn rather than the working ones.** The design project's own grid runs from eight to
// five, which is what its mock data happens to hold; a client that drew only those would put an event at seven in the
// morning nowhere at all, which is the one thing a calendar may not do. That is a correction owed to the design rather
// than an hour range to copy.

export function DayView({
    span,
    events,
    acts,
}: {
    readonly span: CalendarSpan;
    readonly events: readonly CalendarEvent[];
    readonly acts: EventActs;
}) {
    const { locale, translate } = useLocalization();

    const ofTheDay = inTimeOrder(eventsOn(events, span.from));

    return (
        <div className="flex min-w-0 flex-1 flex-col px-4 py-2.75 panes:min-h-0 panes:overflow-y-auto workspace:px-6">
            {hoursOfDay(span.from).map((hour) => (
                <div
                    key={hour.getTime()}
                    className="flex min-h-11.5 items-stretch gap-3.5 border-t border-line-soft py-1.75"
                >
                    <p className="w-13 shrink-0 pt-0.5 text-xs text-faint">{wordHour(hour, locale)}</p>

                    <ul
                        role="listbox"
                        aria-multiselectable={true}
                        aria-label={translate('calendar.hourEvents', { hour: wordHour(hour, locale) })}
                        className="flex min-w-0 flex-1 flex-col gap-1.5"
                    >
                        {eventsInHour(ofTheDay, hour).map((event) => (
                            <EventEntry
                                key={event.id}
                                event={event}
                                shape="hour"
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
            ))}
        </div>
    );
}
