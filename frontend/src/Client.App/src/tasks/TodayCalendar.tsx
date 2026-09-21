// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientFailureReason } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import { addressOf } from '../routing/spaces';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { TodayCalendarInForce } from './useTodayCalendar';

// What the reader's day already holds, drawn beside the list so that what they owe and what they have promised to be
// somewhere for are read together. It is the calendar's own route and nothing derived: this screen keeps no calendar
// state, and the Calendar space is where an event is opened, amended or answered.
//
// The *Open* link in the heading is the design's own and reaches the Calendar space, which is where an event is
// opened, amended or answered. It is an address rather than a control for the reason every move between spaces is one:
// a space is reached at a fragment, so what takes somebody there is a link they can open the way they open any other.
//
// Every failure says what it is and offers the way out, which here is reading the day again — the same five sentences
// the list carries, worded for a day rather than for a list.

const dayFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'tasks.dayFailedUnauthenticated',
    unauthorized: 'tasks.dayFailedUnauthorized',
    unavailable: 'tasks.dayFailedUnavailable',
    unreadable: 'tasks.dayFailedUnreadable',
    missing: 'tasks.dayFailedUnavailable',
};

export function TodayCalendar({ day }: { readonly day: TodayCalendarInForce }) {
    const { locale, translate } = useLocalization();

    // The clock the reader keeps, in the zone their runtime reports: an event at nine is nine where they are rather
    // than where the deployment is, and nothing here names a zone for exactly that reason.
    const clock = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit' });

    return (
        <section aria-labelledby="tasks-today-calendar" className="flex flex-col gap-2.5">
            <div className="flex items-baseline justify-between gap-2">
                <h2 id="tasks-today-calendar" className="text-xs tracking-widest text-muted uppercase">
                    {translate('tasks.todaysCalendar')}
                </h2>

                <a
                    href={addressOf('calendar')}
                    aria-label={translate('tasks.openTheCalendar')}
                    className="rounded-sm text-sm text-accent transition hover:text-accent-strong"
                >
                    {translate('tasks.openCalendar')}
                </a>
            </div>

            {day.reading ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.dayReading')}
                </p>
            ) : null}

            {day.failure === null ? null : (
                <div className="flex flex-col items-start gap-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(dayFailures[day.failure])}
                    </p>
                    <SecondaryButton label={translate('tasks.readAgain')} shape="compact" onActivate={day.readAgain} />
                </div>
            )}

            {!day.reading && day.failure === null && day.events.length === 0 ? (
                <p className="text-sm text-muted text-pretty">{translate('tasks.dayEmpty')}</p>
            ) : null}

            {day.events.length === 0 ? null : (
                <ul className="flex flex-col gap-2">
                    {day.events.map((event) => (
                        <li
                            key={event.id}
                            className="flex items-baseline gap-2.5 rounded-lg border border-line bg-panel px-2.75 py-2.25"
                        >
                            <time dateTime={event.start} className="w-13 shrink-0 text-xs text-muted">
                                {clock.format(new Date(event.start))}
                            </time>
                            <span className="min-w-0 flex-1 text-sm text-pretty">{event.title}</span>
                        </li>
                    ))}
                </ul>
            )}
        </section>
    );
}
