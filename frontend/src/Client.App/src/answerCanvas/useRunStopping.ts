// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import { stopDiscoveryRun, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';

// Stopping a run, which is a different thing from stopping reading one and is therefore stated apart from the follower
// beside it. Ending a read is looking away: the run goes on calling the provider and drawing mail for nobody, and costs
// exactly what not stopping costs. This asks the deployment to stop, which is what makes the control mean what somebody
// takes it to mean.
//
// What it holds is only how far that asking has got. How the run then ended is the run's own account, read back by the
// follower like everything else about it — so a stop that landed and a run that finished on its own reach the screen by
// the same route, and nothing here has to guess which happened.

/**
 * How far asking the deployment to stop the run has got.
 *
 * `refused` is one member rather than the failure model, because the deployment being out of reach, a session that
 * expired, and an answer this client could not read are already carried — by the *read* of the same run, which is
 * failing for the same reason at the same moment and is where a screen says what is wrong. What this adds is the one
 * fact that read cannot carry: the run is still going because the stop did not land.
 */
export type RunStopping = 'idle' | 'asking' | 'refused';

/** Where the asking stands, and what asking does. */
export interface StoppableRun {
    readonly stopping: RunStopping;

    /** Asks the deployment to stop the run, which does nothing where there is no run or nobody signed in. */
    readonly stop: () => void;
}

/** What the hook holds, which is the asking together with the run it was about. */
interface AskedRun {
    readonly run: string | null;
    readonly stopping: RunStopping;
}

/**
 * Stops the run a screen is drawing, and says so from the moment it is asked for.
 *
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How the request goes out.
 * @param run The run the screen is drawing, or `null` where it is drawing none.
 * @returns How far the asking has got, and what asks.
 */
export function useRunStopping(
    session: ClientSession | null,
    transport: MailFathomTransport,
    run: string | null,
): StoppableRun {
    const [asked, setAsked] = useState<AskedRun>({ run: null, stopping: 'idle' });

    function stop(): void {
        if (session === null || run === null) {
            return;
        }

        setAsked({ run, stopping: 'asking' });

        void stopDiscoveryRun(session, transport, run).then((answered) => {
            // A run the deployment no longer holds is stopped as far as anybody asking is concerned, and the follower
            // is already reading that same answer and ending the run on the screen. Only a stop that did not reach the
            // deployment at all leaves a run going with nobody having been told.
            if (answered.outcome === 'read' || answered.failure.reason === 'missing') {
                return;
            }

            setAsked((before) => (before.run === run ? { run, stopping: 'refused' } : before));
        });
    }

    // A run the screen has moved off was never this run's asking, so what was asked for it is never drawn against the
    // one in front. Derived rather than cleared in an effect, which would draw the old state for a frame first.
    return { stopping: asked.run === run ? asked.stopping : 'idle', stop };
}
