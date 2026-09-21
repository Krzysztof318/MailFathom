// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    followRun,
    readDiscoveryRunTail,
    type ClientSession,
    type FollowedRun,
    type MailFathomTransport,
    type RunFollowingSchedule,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';
import { answerAfter, nothingRead, type FollowedAnswer } from './followedRun';

// How the canvas comes by its blocks. The follower is the client's, stated once for both surfaces that produce an
// answer, and this is the half that belongs to a screen: it holds the run's state, subscribes to what the deployment
// says advanced it, and stops when the screen showing it goes away.
//
// **The read is what carries the answer and the signal only says how far the run has got.** So the first read is made
// whether or not a hub is there, a run already composed draws in full, and a deployment serving no channel costs the
// follower's own interval rather than the answer.

/** What the client holds about one run, and what it is for — so an answer read for a run nobody is on is never drawn. */
interface AnsweredRun {
    readonly run: string | null;
    readonly answer: FollowedAnswer;
}

/**
 * Follows one run for as long as a screen is drawing it.
 *
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How each read goes out.
 * @param run The run to follow, or `null` where the screen is drawing none.
 * @param schedule How the follower waits before reading a silent run again.
 * @returns The run as far as it has been read.
 */
export function useFollowedRun(
    session: ClientSession | null,
    transport: MailFathomTransport,
    run: string | null,
    schedule: RunFollowingSchedule,
): FollowedAnswer {
    const changes = useSignalledChanges();
    const [answered, setAnswered] = useState<AnsweredRun>({ run: null, answer: nothingRead });

    useEffect(() => {
        if (session === null || run === null) {
            return;
        }

        // Closing is reached through a function declared below rather than out of the subscription directly, because
        // the callback that closes on one answer in particular is written before the subscription exists. Every call
        // to it is made after an await inside the follower, so the subscription is always there by then.
        function stopFollowing(): void {
            following.close();
        }

        const following: FollowedRun = followRun({
            run,
            from: 0,
            read: (since) => readDiscoveryRunTail(session, transport, run, since),
            told: (tail) => {
                setAnswered((before) => ({
                    run,
                    answer: answerAfter(before.run === run ? before.answer : nothingRead, tail),
                }));

                // A run this person does not hold is the one answer no further read changes, and the follower has no
                // way of knowing it: a failed read leaves the run's state unknown, which it reads as still working.
                if (tail.outcome === 'failed' && tail.failure.reason === 'missing') {
                    stopFollowing();
                }
            },
            schedule,
        });

        const heard = changes.listen((change) => {
            if (change.kind === 'discovery.run.advanced') {
                following.advanced(change.run, change.sequence);
            } else if (change.kind === 'refresh') {
                // A refresh is the widest statement either side can make — a connection standing again after a gap, an
                // interval, or somebody pressing the control — and what it means here is that an advance may have been
                // missed, which is exactly what reading again settles.
                following.reconnected();
            }
        });

        return () => {
            heard();
            following.close();
        };
    }, [session, transport, run, schedule, changes]);

    // A run the screen has moved off is not this run's answer, so what was read for it is never drawn against the one
    // in front. Derived rather than cleared in an effect, which would draw the old answer for a frame first.
    return answered.run === run ? answered.answer : nothingRead;
}
