// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readCalendarWindow,
    type CalendarEvent,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { dayAround } from './dayInstants';

// What the reader's day already holds, read through the calendar's own route rather than through a copy of it. This
// screen keeps no calendar state: it asks for one window, draws it, and asks again when something it did puts a new
// event in that window.
//
// **The window is the reader's own day**, opened and closed at their local midnights. The deployment keeps no timezone
// for anybody, so which hours are somebody's day is a thing only their client knows — which is why the route takes two
// instants and why nothing here names a zone.
//
// **Only what is on the calendar is drawn.** A proposed event is an offer rather than something the day holds, and a
// panel saying how spoken-for a day is must not count offers nobody has accepted; the Calendar screen is where a
// proposal is answered.

/** What the panel holds, and the way a screen asks for it again. */
export interface TodayCalendarInForce {
    /** Today's events, earliest first, as the deployment walked them. */
    readonly events: readonly CalendarEvent[];

    readonly reading: boolean;

    /** Why the day did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Reads the day again: the way out of a failure, and what putting something on the calendar is followed by. */
    readonly readAgain: () => void;
}

// How many of a day's events the panel asks for. Far above anything a person's day holds and far below the bound the
// route enforces: what it guards against is an answer that was never one day.
const eventsInADay = 100;

interface HeldDay {
    readonly session: ClientSession | null;
    readonly day: string;
    readonly events: readonly CalendarEvent[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(session: ClientSession | null, day: string): HeldDay {
    return { session, day, events: [], reading: session !== null, failure: null };
}

/**
 * Holds the events on the reader's own day.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with.
 * @param transport How a request reaches the deployment.
 * @param now When the reader is reading, which decides which day is asked for.
 * @returns What the day holds, and the way to ask for it again.
 */
export function useTodayCalendar(
    session: ClientSession | null,
    transport: MailFathomTransport,
    now: number,
): TodayCalendarInForce {
    const window = dayAround(now);
    const [held, setHeld] = useState<HeldDay>(() => nothingRead(session, window.from));

    // Which read is the current one, for the reason the task list keeps one: an answer on its way when the credential
    // is replaced or the day is read again must not be drawn over the one that replaced it.
    const latest = useRef(0);

    const read = useCallback(
        async (from: string, until: string): Promise<void> => {
            if (session === null) {
                return;
            }

            const asked = ++latest.current;
            const answer = await readCalendarWindow(session, transport, {
                from,
                until,
                origin: 'Asserted',
                count: eventsInADay,
            });

            if (asked !== latest.current) {
                return;
            }

            setHeld(
                answer.outcome === 'failed'
                    ? { session, day: from, events: [], reading: false, failure: answer.failure.reason }
                    : { session, day: from, events: answer.value.events, reading: false, failure: null },
            );
        },
        [session, transport],
    );

    useEffect(() => {
        void read(window.from, window.until);
    }, [read, window.from, window.until]);

    const standing = held.session === session && held.day === window.from ? held : nothingRead(session, window.from);

    return {
        events: standing.events,
        reading: standing.reading,
        failure: standing.failure,
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void read(window.from, window.until);
        },
    };
}
