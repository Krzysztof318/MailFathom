// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    followRun,
    readAgentConversation,
    type AgentConversationEntry,
    type ClientFailureReason,
    type ClientSession,
    type FollowedRun,
    type MailFathomTransport,
    type RunFollowingSchedule,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';

// How the Agent screen comes by a conversation. It is the follower `runFollowing.ts` states once for both surfaces that
// compose an answer, keyed on the conversation rather than on one answer: a conversation carries a new answer per
// question, and a decision on a proposal advances it with no answer at all, so the signal is matched on the
// conversation it names. A follower settles once nothing is being composed, so every write this screen makes asks for
// a new one from the cursor already held — which is what `revision` is.

/** What the client holds about one conversation. */
export interface FollowedConversation {
    readonly conversation: string | null;
    readonly entries: readonly AgentConversationEntry[];

    /** Why the last read did not answer, and `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether nothing has been read yet, which is what draws the conversation as loading rather than empty. */
    readonly reading: boolean;

    /**
     * The last sequence the first read answered with, so what arrived after it is what arrived while somebody was
     * looking — the entries that animate in rather than the history that was already there.
     */
    readonly settledThrough: number;
}

/** What is held, and whether any read of it has answered — which is what tells a first read from a later one. */
interface HeldConversation extends FollowedConversation {
    readonly answered: boolean;
}

const nothingFollowed: HeldConversation = {
    conversation: null,
    entries: [],
    failure: null,
    reading: false,
    settledThrough: 0,
    answered: false,
};

/**
 * Follows one conversation for as long as the screen is drawing it.
 *
 * @param conversation The conversation, or `null` where the screen is drawing a new one nothing has been asked in.
 * @param revision Bumped by the screen after each write, so a follower that settled reads again from its cursor.
 */
export function useFollowedConversation(
    session: ClientSession | null,
    transport: MailFathomTransport,
    conversation: string | null,
    revision: number,
    schedule: RunFollowingSchedule,
): FollowedConversation {
    const changes = useSignalledChanges();
    const [followed, setFollowed] = useState<HeldConversation>(nothingFollowed);

    // The cursor of the conversation in front, which the next follower starts from. Held beside the state rather than
    // read out of it, because it is the effect's input rather than something the screen draws.
    const cursor = useRef<{ readonly conversation: string | null; readonly through: number }>({
        conversation: null,
        through: 0,
    });

    useEffect(() => {
        if (session === null || conversation === null) {
            return;
        }

        const from = cursor.current.conversation === conversation ? cursor.current.through : 0;

        function stopFollowing(): void {
            following.close();
        }

        const following: FollowedRun = followRun<AgentConversationEntry>({
            run: conversation,
            from,
            read: (since) => readAgentConversation(session, transport, conversation, since),
            told: (tail) => {
                if (tail.outcome === 'failed') {
                    const reason = tail.failure.reason;

                    setFollowed((before) => ({
                        ...(before.conversation === conversation ? before : { ...nothingFollowed, conversation }),
                        failure: reason,
                        reading: false,
                    }));

                    if (reason === 'missing') {
                        stopFollowing();
                    }

                    return;
                }

                const arrived = tail.value.events;
                const through = arrived.reduce((last, entry) => Math.max(last, entry.sequence), from);
                cursor.current = { conversation, through: Math.max(cursor.current.through, through) };

                setFollowed((before) => {
                    const held = before.conversation === conversation && before.answered ? before : null;
                    const known = new Set(held?.entries.map((entry) => entry.sequence));

                    return {
                        conversation,
                        entries: [...(held?.entries ?? []), ...arrived.filter((entry) => !known.has(entry.sequence))],
                        failure: null,
                        reading: false,
                        settledThrough: held === null ? through : held.settledThrough,
                        answered: true,
                    };
                });
            },
            schedule,
        });

        const heard = changes.listen((change) => {
            if (change.kind === 'run.advanced' && change.conversation === conversation) {
                following.advanced(conversation, change.sequence);
            } else if (change.kind === 'refresh') {
                following.reconnected();
            }
        });

        return () => {
            heard();
            following.close();
        };
    }, [session, transport, conversation, revision, schedule, changes]);

    if (conversation === null) {
        return nothingFollowed;
    }

    // A conversation the screen has moved off is never drawn against the one in front. Derived rather than cleared in
    // an effect, which would draw the old conversation for a frame first.
    return followed.conversation === conversation ? followed : { ...nothingFollowed, conversation, reading: true };
}
