// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readOwnTasks,
    readProposedTasks,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type PersonalTaskPage,
} from '@mailfathom/client-backend';
import { inDueOrder } from './taskGrouping';

// The signed-in person's list as the screen holds it: the pages of both halves read so far, where each walk continues,
// and whether either is still going.
//
// **Both halves are read together and drawn as one list.** The surface serves what somebody committed to and what mail
// proposed as two routes, because they are two things to a deployment; the design draws one list with a mark on the
// proposals, because they are one list to a reader. So this reads both, and `inDueOrder` is where the two orders
// become one.
//
// **A page asks both halves for their next page.** Two walks that advanced separately would put a proposal due on
// Tuesday after a commitment due in November, because one of them had been read further than the other. A half that
// has reached its end is simply not asked again.
//
// **Every write is answered by reading the list again rather than by correcting what is held.** Where a task sorts is
// the deployment's, accepting one moves it between the two halves entirely, and completing one changes what the
// capacity panel counts — so a list edited in place would be this client's own opinion of an ordering it does not own.
//
// **What is held names the credential it was read for**, and a reading for another one is derived away during the
// render that observes it rather than cleared by an effect.

/** What the list holds, and the two ways a screen asks it for more. */
export interface TasksInForce {
    /** Both halves as one list, soonest due first and the undated last. */
    readonly tasks: readonly PersonalTask[];

    /** Whether nothing has answered yet, which is the wait the list says it is in. */
    readonly reading: boolean;

    /** Whether a further page is on its way, which is the wait drawn at the foot of the rows already there. */
    readonly paging: boolean;

    /** Why the list did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether both walks have reached their end, so there is nothing further to ask for. */
    readonly complete: boolean;

    /** Asks for the page after the ones held, which a reader reaching the foot of the list is what calls. */
    readonly readMore: () => void;

    /** Reads the list again from the leading end: the way out of a failure, and what every write is followed by. */
    readonly readAgain: () => void;
}

// One walk as it stands: the pages read, where it continues, and whether it has anywhere left to go.
interface HeldHalf {
    readonly tasks: readonly PersonalTask[];
    readonly cursor: string | null;
}

interface HeldTasks {
    readonly session: ClientSession | null;
    readonly committed: HeldHalf;
    readonly proposed: HeldHalf;
    readonly reading: boolean;
    readonly paging: boolean;
    readonly failure: ClientFailureReason | null;
}

const nowhereRead: HeldHalf = { tasks: [], cursor: null };

function nothingRead(session: ClientSession | null): HeldTasks {
    return {
        session,
        committed: nowhereRead,
        proposed: nowhereRead,
        reading: session !== null,
        paging: false,
        failure: null,
    };
}

/**
 * Holds the signed-in person's own task list, both halves of it.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — in which case nothing is read
 * and the screen draws a list that never answered rather than an empty one.
 * @param transport How a request reaches the deployment.
 * @returns What is held, and the two ways a screen asks for more of it.
 */
export function useTasks(session: ClientSession | null, transport: MailFathomTransport): TasksInForce {
    const [held, setHeld] = useState<HeldTasks>(() => nothingRead(session));

    // Which read is the current one. A credential replaced, or a list read again while a page is still in flight, each
    // leave an answer on its way that must not be drawn: the ordering is not guaranteed, and a stale page appended to
    // a list somebody has since changed reads as a rendering defect rather than as a race.
    const latest = useRef(0);

    const readFrom = useCallback(
        async (standing: { readonly committed: string | null; readonly proposed: string | null } | null) => {
            if (session === null) {
                return;
            }

            const asked = ++latest.current;

            // A half that has reached its end is not asked again; the first read asks both from the leading end.
            const [committed, proposed] = await Promise.all([
                standing !== null && standing.committed === null
                    ? null
                    : readOwnTasks(session, transport, { cursor: standing?.committed ?? null }),
                standing !== null && standing.proposed === null
                    ? null
                    : readProposedTasks(session, transport, { cursor: standing?.proposed ?? null }),
            ]);

            if (asked !== latest.current) {
                return;
            }

            setHeld((current) => {
                const carried = current.session === session ? current : nothingRead(session);

                // Either half failing fails the read: half a list drawn as a whole one is a person told they owe less
                // than they do, which is the one thing this screen may not get wrong.
                const failure =
                    committed?.outcome === 'failed'
                        ? committed.failure.reason
                        : proposed?.outcome === 'failed'
                          ? proposed.failure.reason
                          : null;

                if (failure !== null) {
                    return { ...carried, reading: false, paging: false, failure };
                }

                return {
                    session,
                    committed: halfAfter(carried.committed, committed, standing !== null),
                    proposed: halfAfter(carried.proposed, proposed, standing !== null),
                    reading: false,
                    paging: false,
                    failure: null,
                };
            });
        },
        [session, transport],
    );

    // The read that starts the list, which is a request going out and therefore the one thing an effect is for. It
    // runs again when the credential changes; nothing is cleared here, because what is held names the credential it
    // was read for and a reading for another one is derived away below.
    useEffect(() => {
        void readFrom(null);
    }, [readFrom]);

    const standing = held.session === session ? held : nothingRead(session);
    const complete = standing.committed.cursor === null && standing.proposed.cursor === null;

    return {
        tasks: inDueOrder(standing.committed.tasks, standing.proposed.tasks),
        reading: standing.reading,
        paging: standing.paging,
        failure: standing.failure,
        complete,
        readMore: () => {
            if (standing.paging || standing.reading || complete) {
                return;
            }

            setHeld({ ...standing, paging: true, failure: null });
            void readFrom({ committed: standing.committed.cursor, proposed: standing.proposed.cursor });
        },
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void readFrom(null);
        },
    };
}

// What one half stands at after an answer: appended where this was a further page, replaced where it was the first,
// and left alone where the walk had already ended and nothing was asked. A failure is left alone as well, because the
// caller has already turned it into the failure the whole read reports.
function halfAfter(carried: HeldHalf, answer: ClientResult<PersonalTaskPage> | null, continuing: boolean): HeldHalf {
    if (answer === null || answer.outcome === 'failed') {
        return carried;
    }

    return {
        tasks: continuing ? [...carried.tasks, ...answer.value.tasks] : answer.value.tasks,
        cursor: answer.value.nextCursor,
    };
}
