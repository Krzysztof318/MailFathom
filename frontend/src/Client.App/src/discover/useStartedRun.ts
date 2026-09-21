// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    startDiscoveryRun,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { AskedQuestion } from '../workspace/askScope';
import { runAskedFor } from './runAsk';

// How a question becomes a run. The intent field records what was asked and this is what asks the deployment, because
// the field is drawn in every space and a run belongs to the one that answers.
//
// **What starts a run is the newest recorded question, by its identity rather than by its words.** Asking the same
// sentence twice moves the entry the field already holds to the front of the list, so nothing about the value changes
// and comparing words would leave the second press unanswered. The entry the field writes is a new object on every
// press, which is exactly the question *this* press asked.
//
// **One run per press, including under the development double-render.** The entry a run was already asked for is held
// in a ref, so an effect invoked twice for one press asks once: a second request here would not be a wasted read but a
// second run, spending a provider call somebody never asked for.
//
// **A run already in flight is left to end on its own.** Asking again draws the new run, and the older one is stopped
// by nothing here — what the reader asked for is the answer in front of them, and a client that cancelled the previous
// run would make asking a wider question twice in a row cost the first answer as well.

/** The run the newest question is being answered by, as far as starting one has got. */
export interface StartedRun {
    /** The run to follow, or `null` where none has been accepted yet. */
    readonly run: string | null;

    /** Whether the deployment has been asked and has not yet answered, which is what a screen waits under. */
    readonly starting: boolean;

    /** Why the question was not accepted, and `null` where it was or where nothing has been asked. */
    readonly failure: ClientFailureReason | null;
}

const nothingAsked: StartedRun = { run: null, starting: false, failure: null };

// What a question that has been asked and not yet sent reads as. It is the render between the press and the effect that
// sends it, and it is stated as waiting rather than as nothing having been asked: the alternative draws the idle screen
// for one frame after somebody pressed *Ask*, which reads as the press having been lost.
const asking: StartedRun = { run: null, starting: true, failure: null };

/**
 * Starts a run for the question that was asked last, and holds it for as long as that is the question.
 *
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How the request goes out.
 * @param asked The newest question the intent field recorded, or `null` where nothing has been asked. It is the entry
 * the workspace holds rather than one composed for the call: the identity of that object is what a question *is* here,
 * so an entry built during a render would be a new question on every render and a run started for each of them.
 * @returns The run answering it, as far as starting one has got.
 */
export function useStartedRun(
    session: ClientSession | null,
    transport: MailFathomTransport,
    asked: AskedQuestion | null,
): StartedRun {
    const [started, setStarted] = useState<{ readonly asked: AskedQuestion | null; readonly run: StartedRun }>({
        asked: null,
        run: nothingAsked,
    });

    const requested = useRef<AskedQuestion | null>(null);

    useEffect(() => {
        if (session === null || asked === null || requested.current === asked) {
            return;
        }

        requested.current = asked;
        setStarted({ asked, run: asking });

        void startDiscoveryRun(session, transport, runAskedFor(asked)).then((answered) => {
            // Held against the question it was asked for, so an answer arriving after somebody asked something else
            // never draws the older run under the newer question — two starts have no guaranteed order, and the
            // failure reads as a screen drawing the wrong answer rather than as a race.
            setStarted((before) =>
                before.asked !== asked
                    ? before
                    : {
                          asked,
                          run:
                              answered.outcome === 'read'
                                  ? { run: answered.value, starting: false, failure: null }
                                  : { run: null, starting: false, failure: answered.failure.reason },
                      },
            );
        });
    }, [session, transport, asked]);

    if (asked === null || session === null) {
        return nothingAsked;
    }

    return started.asked === asked ? started.run : asking;
}
