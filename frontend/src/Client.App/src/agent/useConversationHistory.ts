// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    listAgentConversations,
    type AgentConversationSummary,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';

/** The history as far as it has been read. */
export interface ConversationHistory {
    readonly conversations: readonly AgentConversationSummary[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

/**
 * Reads this person's conversations, again after every write the screen makes and whenever the connection says an
 * advance may have been missed.
 *
 * @param revision Bumped by the screen after each write, which is what reads the history again.
 */
export function useConversationHistory(
    session: ClientSession | null,
    transport: MailFathomTransport,
    revision: number,
): ConversationHistory {
    const changes = useSignalledChanges();
    const [refreshed, setRefreshed] = useState(0);
    const [history, setHistory] = useState<ConversationHistory>({ conversations: [], reading: true, failure: null });

    useEffect(
        () =>
            changes.listen((change) => {
                if (change.kind === 'refresh') {
                    setRefreshed((count) => count + 1);
                }
            }),
        [changes],
    );

    useEffect(() => {
        if (session === null) {
            return;
        }

        let listening = true;

        void listAgentConversations(session, transport).then((listed) => {
            if (!listening) {
                return;
            }

            setHistory((before) =>
                listed.outcome === 'read'
                    ? { conversations: listed.value, reading: false, failure: null }
                    : { ...before, reading: false, failure: listed.failure.reason },
            );
        });

        return () => {
            listening = false;
        };
    }, [session, transport, revision, refreshed]);

    return history;
}
