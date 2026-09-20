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
import type { CalendarSpan } from './calendarSpan';

// One span of the calendar as a screen holds it: what is on the calendar, what the mail proposed, and whether either
// has answered yet.
//
// **One read rather than two.** The deployment publishes the two halves as one window narrowed by an argument, and the
// screen draws both at once — the views draw what is on the calendar and the column beside them draws what was
// proposed — so asking twice would be two requests for one answer and two moments for the screen to be half drawn in.
// What follows from that is stated where it shows: the proposals a reader sees are the ones falling inside the span
// they are looking at, which is the honest reading of a window and not a sidebar that quietly ignores it.
//
// **Every write is answered by reading the span again rather than by correcting what is held.** Accepting a proposal
// moves it between the two halves, amending one moves it in time, and both are the deployment's account of the
// calendar rather than this client's — so a list edited in place would be this client's opinion of a record it does
// not own.

/** What one span of the calendar holds, and the way a screen asks for it again. */
export interface CalendarInForce {
    /** What is on the calendar in this span, earliest first. */
    readonly events: readonly CalendarEvent[];

    /** What the reader's mail proposed in this span and nobody has answered yet, earliest first. */
    readonly proposals: readonly CalendarEvent[];

    /** Whether nothing has answered yet, which is the wait the screen says it is in. */
    readonly reading: boolean;

    /** Why the span did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Reads the span again: the way out of a failure, and what every write is followed by. */
    readonly readAgain: () => void;
}

interface HeldSpan {
    readonly from: string;
    readonly until: string;
    readonly session: ClientSession | null;
    readonly events: readonly CalendarEvent[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(from: string, until: string, session: ClientSession | null): HeldSpan {
    return { from, until, session, events: [], reading: session !== null, failure: null };
}

/**
 * Holds one span of the signed-in person's calendar.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — in which case nothing is read
 * and the screen draws a calendar that never answered rather than an empty one.
 * @param transport How a request reaches the deployment.
 * @param span The days being drawn, which is the window the deployment is asked for.
 */
export function useCalendarWindow(
    session: ClientSession | null,
    transport: MailFathomTransport,
    span: CalendarSpan,
): CalendarInForce {
    const from = span.from.toISOString();
    const until = span.until.toISOString();

    const [held, setHeld] = useState<HeldSpan>(() => nothingRead(from, until, session));

    // Which read is the current one. A span moved or a credential replaced while a read is in flight leaves an answer
    // on its way that must not be drawn: the ordering is not guaranteed, and last week's events appearing under this
    // week's heading reads as a rendering defect rather than as a race.
    const latest = useRef(0);

    const read = useCallback(async (): Promise<void> => {
        if (session === null) {
            return;
        }

        const asked = ++latest.current;
        const answer = await readCalendarWindow(session, transport, { from, until });

        if (asked !== latest.current) {
            return;
        }

        setHeld((standing) => {
            if (answer.outcome !== 'failed') {
                return { from, until, session, events: answer.value.events, reading: false, failure: null };
            }

            // A read that failed says nothing about what was already drawn, so a span already on the screen stays
            // there behind the failure rather than emptying under it — which is the partial state § UX asks for, and
            // the case a re-read after a write is in.
            const drawn = standing.from === from && standing.until === until && standing.session === session;

            return {
                from,
                until,
                session,
                events: drawn ? standing.events : [],
                reading: false,
                failure: answer.failure.reason,
            };
        });
    }, [from, session, transport, until]);

    // The read itself, which is a request going out and therefore the one thing an effect is for. It runs again when
    // the span moves or the credential changes; nothing is cleared here, because what is held names the span it was
    // read for and a reading for another one is derived away below.
    useEffect(() => {
        void read();
    }, [read]);

    const standing =
        held.from === from && held.until === until && held.session === session
            ? held
            : nothingRead(from, until, session);

    return {
        events: standing.events.filter((event) => event.origin === 'Asserted'),
        proposals: standing.events.filter((event) => event.origin === 'Proposed'),
        reading: standing.reading,
        failure: standing.failure,
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void read();
        },
    };
}
