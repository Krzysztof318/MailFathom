// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DeploymentSession } from '@mailfathom/client-backend';

/**
 * What the deployment has said about forwarding this client's own records: an address it forwards them to, that it
 * forwards none, or — until it has answered — nothing either way.
 *
 * The third case is not the second, and it is a type rather than an absent string because collapsing the two was a
 * defect. The permission the frame computes reads an unanswered deployment as permission rather than as a refusal, so
 * that a cold start and a failed sign-in are recorded rather than thrown away; a screen drawing "there is nothing to
 * turn off" over that same state would therefore tell somebody nothing is being sent at exactly the moment something
 * is.
 */
export type TelemetryForwarding =
    { readonly answered: false } | { readonly answered: true; readonly destination: string | null };

/**
 * Reads it off the session the deployment answered with, where there is one.
 *
 * A session that has not been read has answered nothing whatever the reason — the round trip is still out, the
 * machine has no network, or the credential was refused — so all three are the unanswered case rather than three
 * states a screen would have to word separately.
 */
export function telemetryForwardedBy(
    session: DeploymentSession | null,
    baseAddress: string | null,
): TelemetryForwarding {
    if (session === null || baseAddress === null) {
        return { answered: false };
    }

    return { answered: true, destination: session.telemetryForwarded ? baseAddress : null };
}
