// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useMemo, useRef } from 'react';
import {
    openSignalStream,
    type ClientSession,
    type MailFathomSignalChannel,
    type MailFathomTransport,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';
import type { SignalledChanges, SignalListener } from './signalledChanges';

/**
 * Holds a connection to the deployment's signal channel for as long as one credential does.
 *
 * The connection is what this effect synchronizes with, which is what an effect is for. It opens once a session
 * exists, and signing out or being pointed at another deployment closes it and opens nothing — so nothing about one
 * deployment reaches a client that has left it.
 *
 * **A channel that never opens is silent.** Nothing here is rendered, nothing here fails, and no screen is told: a
 * deployment serving no hub, a proxy that will not pass the upgrade, and a connection that dropped are one thing to a
 * person reading their mail — a client on its own interval, which is what every screen already does.
 *
 * @param session Who is asking, or `null` where nobody is signed in or the credential may not read mail.
 * @param transport How the ticket each connection is opened against is minted.
 * @param channel How a connection is opened, which is the composition root's to supply.
 * @param schedule How the stream waits before opening again.
 * @returns What a screen subscribes to.
 */
export function useSignals(
    session: ClientSession | null,
    transport: MailFathomTransport,
    channel: MailFathomSignalChannel,
    schedule: SignalStreamSchedule,
): SignalledChanges {
    // The listeners rather than the last statement, because a signal is an instant: a screen acts on one and there is
    // nothing left to render. A ref because nothing on the screen is drawn from it and a screen subscribing must not
    // reopen the connection.
    const listeners = useRef(new Set<SignalListener>());

    const changes = useMemo<SignalledChanges>(
        () => ({
            listen: (listener) => {
                listeners.current.add(listener);

                return () => {
                    listeners.current.delete(listener);
                };
            },
        }),
        [],
    );

    useEffect(() => {
        if (session === null) {
            return;
        }

        const stream = openSignalStream(
            session,
            transport,
            channel,
            (signal) => {
                // A copy, because a screen that unmounts on what it was told would otherwise be removed from the set
                // being walked.
                for (const listener of [...listeners.current]) {
                    listener(signal);
                }
            },
            schedule,
        );

        return () => {
            void stream.close();
        };
    }, [session, transport, channel, schedule]);

    return changes;
}
