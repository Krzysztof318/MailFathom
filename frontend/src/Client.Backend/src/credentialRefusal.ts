// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failureReasonForStatus } from './failure';
import type { MailFathomTransport } from './transport';

// Noticing that the deployment has stopped accepting the credential, whichever request it said so to. Every operation
// already answers such a refusal as `unauthenticated`, but each to the screen that asked — a folder that could not be
// read, a message that could not be opened — and a screen is not where a sign-in ends. So the refusal is heard once,
// at the one place every request passes on its way back, rather than by each of forty screens remembering to pass it
// upwards.

/**
 * A transport that reports every answer refusing the credential the caller holds now, and answers exactly as it would.
 *
 * A refusal of a credential the caller has since replaced is not reported: a renewal destroys the token it was handed
 * the moment it answers with the next, so a request that went out with the old one is refused for having been
 * replaced rather than for the person behind it, and the caller already holds what the next request will present.
 *
 * @param transport How a request reaches the deployment.
 * @param holding The finished header value the caller holds at the moment an answer arrives.
 * @param onRefused What to do where the deployment has stopped accepting it.
 */
export function reportingRefusedCredential(
    transport: MailFathomTransport,
    holding: () => string | null,
    onRefused: () => void,
): MailFathomTransport {
    return async (request) => {
        const answer = await transport(request);

        if (
            failureReasonForStatus(answer.status) === 'unauthenticated' &&
            request.headers['Authorization'] === holding()
        ) {
            onRefused();
        }

        return answer;
    };
}
