// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    readCitations,
    type CitationTarget,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type ResolvedCitation,
} from '@mailfathom/client-backend';

// Following one citation, for as long as somebody is looking at it. A plan declares where each of its sources is
// followed to and the deployment answers what is there, so this is the half between them: it asks for the one target
// in front of the reader and holds what came back.
//
// **One target at a time, deliberately.** The route follows ten at once and this asks for one, because what a reader
// is checking is the fact they pressed: resolving a plan's whole source list would draw out every message behind an
// answer to show one paragraph, which is mail read for nobody.
//
// **A source that resolved to nothing is not a failure**, and the three outcomes are kept apart all the way to the
// screen. What this reports as failed is a read that did not happen at all.

/** What is known about the source somebody is checking. */
export type CitedEvidence =
    /** The read is out, which is the state the inspector says it is waiting in. */
    | { readonly state: 'following' }

    /** The deployment answered, whichever of the three outcomes it answered with. */
    | { readonly state: 'followed'; readonly citation: ResolvedCitation }

    /** The read did not happen, which is a different thing from a source that resolved to nothing. */
    | { readonly state: 'failed'; readonly reason: ClientFailureReason };

const following: CitedEvidence = { state: 'following' };
const unauthenticated: CitedEvidence = { state: 'failed', reason: 'unauthenticated' };

/**
 * Follows one citation for as long as a screen is drawing it.
 *
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How the read goes out.
 * @param target Where the source is followed to, or `null` where nothing is being followed.
 * @returns What is known about that source, or `null` where nothing is being followed.
 */
export function useCitedEvidence(
    session: ClientSession | null,
    transport: MailFathomTransport,
    target: CitationTarget | null,
): CitedEvidence | null {
    // Held beside the target it was read for, so a citation pressed while another is still answering never draws the
    // older answer under the newer source — the ordering of two reads is not guaranteed, and the failure looks like a
    // rendering defect rather than a race.
    const [answered, setAnswered] = useState<{
        readonly target: CitationTarget | null;
        readonly evidence: CitedEvidence;
    }>({
        target: null,
        evidence: following,
    });

    useEffect(() => {
        if (target === null || session === null) {
            return;
        }

        let listening = true;

        void readCitations(session, transport, [target]).then((answer) => {
            if (!listening) {
                return;
            }

            const resolved = answer.outcome === 'read' ? answer.value[0] : undefined;

            setAnswered({
                target,
                evidence:
                    answer.outcome === 'failed'
                        ? { state: 'failed', reason: answer.failure.reason }
                        : resolved === undefined
                          ? // One target was asked about and the route answers one resolution per citation, so an answer
                            // holding none is one this client cannot read rather than a source that resolved to nothing.
                            { state: 'failed', reason: 'unreadable' }
                          : { state: 'followed', citation: resolved },
            });
        });

        return () => {
            listening = false;
        };
    }, [session, transport, target]);

    if (target === null) {
        return null;
    }

    // A client with nobody signed in has no read to make, so it is the one answer derived rather than read: the source
    // is not private and the deployment is not down — the session ended, and what to do about that is sign in again.
    if (session === null) {
        return unauthenticated;
    }

    // Derived rather than cleared in an effect, which would draw the previous source's answer for a frame first.
    return answered.target === target ? answered.evidence : following;
}
