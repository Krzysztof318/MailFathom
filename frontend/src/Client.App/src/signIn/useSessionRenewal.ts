// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { renewSession, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';
import { resolveSessionCredential } from './credentialEntry';
import type { KeptSession } from './keptSession';

// Keeping somebody signed in while the session they signed in with expires underneath them.
//
// A session lives for a length the deployment decides, and the client is what has to notice: presenting the token it
// holds to the exchange answers a fresh one, so a client that is open renews and a person types nothing. The whole of
// the contract is the instant the deployment stated — read it, renew before it, keep what comes back.

/** How long before a session ends that renewing it becomes due. */
export const renewalMargin = 3_600_000;

/** How often the instant is read against the clock, which is what makes a failed renewal try again rather than being the end of it. */
export const renewalCheckInterval = 60_000;

/**
 * Renews the session this client holds before it expires, and reports a session the deployment has stopped accepting.
 *
 * It reads the clock rather than counting down from one reading, so a machine that was asleep across the margin renews
 * on the tick after it wakes instead of on a timer that did not run. That is also what makes a renewal that could not
 * be delivered try again: nothing here is scheduled once, so a network that came back a minute later is a renewal a
 * minute later rather than a sign-out at the token's own expiry.
 *
 * @param session The address to reach and the credential to present, or `null` where nobody is signed in.
 * @param kept What is held about the session, whose instant decides when renewing is due.
 * @param transport How the renewal goes out.
 * @param online Whether this machine has a network, since a renewal it cannot deliver is one worth not attempting.
 * @param onRenewed What to do with the session that came back, which is both to hold it and to keep it.
 * @param onRefused What to do where the deployment has stopped accepting this session, which is the same path a revoked one takes.
 */
export function useSessionRenewal(
    session: ClientSession | null,
    kept: KeptSession | null,
    transport: MailFathomTransport,
    online: boolean,
    onRenewed: (renewed: KeptSession) => void,
    onRefused: () => void,
): void {
    useEffect(() => {
        if (session === null || kept === null || !online) {
            return;
        }

        // One renewal at a time, and abandoned rather than applied where the session it was started for is no longer
        // the one being held: a renewal that answered after a sign-out would put a live credential back into a client
        // the person has left.
        let renewing = false;
        let abandoned = false;

        const renewIfDue = (): void => {
            if (renewing || Date.parse(kept.expiresAt) - Date.now() > renewalMargin) {
                return;
            }

            renewing = true;

            void renewSession(session, transport)
                .then((answer) => {
                    if (abandoned) {
                        return;
                    }

                    if (answer.outcome === 'read') {
                        onRenewed({
                            authorization: resolveSessionCredential(answer.value.token),
                            expiresAt: answer.value.expiresAt,
                            person: kept.person,
                        });

                        return;
                    }

                    // A session the deployment will not renew is one it has stopped accepting, which is what a
                    // revoked one and an expired one both answer. Anything else — an unreachable deployment, a
                    // failure at the far end — is retried by the next tick rather than treated as a sign-out.
                    if (answer.failure.reason === 'unauthenticated') {
                        onRefused();
                    }
                })
                .finally(() => {
                    renewing = false;
                });
        };

        renewIfDue();

        const ticking = window.setInterval(renewIfDue, renewalCheckInterval);

        return () => {
            abandoned = true;
            window.clearInterval(ticking);
        };
    }, [session, kept, transport, online, onRenewed, onRefused]);
}
