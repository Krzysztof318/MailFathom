// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { MailFathomTransport } from '@mailfathom/client-backend';
import { renewOAuthGrant, renewalIsDue, type OAuthGrant } from './oauthGrant';

// Keeping somebody signed in while the access token they signed in with expires underneath them, which is the same job
// `useSessionRenewal.ts` does for a password sign-in and is written the same way: read the clock rather than count down
// from one reading, so a machine that slept across the expiry renews on the tick after it wakes.
//
// What differs is who is asked and what a refusal means. A session is renewed by the deployment that minted it, and an
// access token by the authorization server that issued it — so a deployment that is unreachable does not stop this, and
// a server that refuses the refresh token has ended the sign-in in a way no retry undoes.

/** How often the instant is read against the clock, which is what makes a failed renewal try again rather than being the end of it. */
export const grantRenewalCheckInterval = 60_000;

/**
 * Renews the OAuth grant this client holds before its access token expires, and reports one the server has ended.
 *
 * @param grant What is held, or `null` where nobody signed in with an authorization server.
 * @param transport How the renewal goes out.
 * @param online Whether this machine has a network, since a renewal it cannot deliver is one worth not attempting.
 * @param onRenewed What to do with the grant that came back, which is both to hold it and to keep it.
 * @param onEnded What to do where the server will not renew it, which is the same path a revoked session takes.
 */
export function useGrantRenewal(
    grant: OAuthGrant | null,
    transport: MailFathomTransport,
    online: boolean,
    onRenewed: (renewed: OAuthGrant) => void,
    onEnded: () => void,
): void {
    // What this client holds right now, read when an answer arrives rather than closed over when the request went out.
    // A server that rotates refresh tokens withdraws the presented one as it answers, so an answer dropped because a
    // dependency changed mid-flight would leave the client holding a token the server has already refused.
    const held = useRef(grant);

    // Which refresh token a renewal is on the wire for, held across re-runs for the same reason: presenting a rotated
    // token twice is what a server rotating them reads as a stolen one.
    const renewingFor = useRef<string | null>(null);

    useEffect(() => {
        held.current = grant;
    }, [grant]);

    useEffect(() => {
        if (grant === null) {
            return;
        }

        const renewIfDue = (): void => {
            const presented = held.current?.refreshToken ?? null;

            if (renewingFor.current !== null || !renewalIsDue(grant)) {
                return;
            }

            if (presented === null) {
                // The server issued no refresh token, so this grant ends with its access token and nothing here can
                // replace it. Saying so is what puts the person back on the sign-in screen rather than in front of a
                // frame waiting on a read the expired token would never be made with.
                onEnded();

                return;
            }

            // A renewal this machine cannot deliver is one worth not attempting; the tick after the network comes back
            // is what makes it. The reading above is not gated on that, because a grant that has run out has run out
            // whether or not anything can be reached.
            if (!online) {
                return;
            }

            renewingFor.current = presented;

            void renewOAuthGrant(grant, transport)
                .then((answer) => {
                    // Applied while the token it replaced is still the one being held, which is what says nobody has
                    // signed out or signed in as somebody else in the meantime.
                    if (held.current?.refreshToken !== presented) {
                        return;
                    }

                    if (answer.outcome === 'renewed') {
                        onRenewed(answer.grant);
                    } else if (answer.outcome === 'ended') {
                        onEnded();
                    }
                })
                .finally(() => {
                    if (renewingFor.current === presented) {
                        renewingFor.current = null;
                    }
                });
        };

        renewIfDue();

        const ticking = window.setInterval(renewIfDue, grantRenewalCheckInterval);

        return () => {
            window.clearInterval(ticking);
        };
    }, [grant, transport, online, onRenewed, onEnded]);
}
