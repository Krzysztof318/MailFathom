// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientResponse } from './transport';

// What this client does about a request the deployment refused for its rate rather than for anything the request said.
// The transport's limiter answers a bare `429` the moment one user has more requests in flight, or has spent more of
// them, than the deployment allows, and that refusal says nothing about the request: the same request a moment later is
// answered. Reading it as a deployment that is not answering is what turned a burst the client caused itself into a
// screen asking somebody to press *Retry*.

/** The most times one request is put on the wire again after the deployment throttled it. */
export const mostThrottledRetries = 3;

const firstThrottledDelay = 250;

/**
 * The longest `Retry-After` this client waits out rather than giving up on the request.
 *
 * A screen waiting on the read says it is waiting for as long as it does, so a refusal asking for a minute is reported
 * as the refusal it is rather than held behind a spinner for that minute: the person can ask again, which is a
 * decision somebody watching a spinner cannot make.
 */
const longestHonouredRetryAfter = 10_000;

/**
 * How long to wait before putting a throttled request on the wire again, in milliseconds, or `null` where it is not
 * put there again.
 *
 * Only the transport's own refusal is throttling. A `429` carrying a problem document is the service declining the
 * operation itself — a deployment that has spent what its operator allows a provider — and each route that answers one
 * reads it as that, so it is handed back at once rather than asked again into the same refusal.
 *
 * A `Retry-After` in seconds is honoured and never undercut, and one asking for longer than this client will hold a
 * screen for ends the attempts instead. Without one — a concurrency refusal has no scheduled moment to name — the wait
 * doubles from a quarter of a second, spread so the requests a burst had refused together are not put back together.
 *
 * @param answer What the deployment answered the attempt just made with.
 * @param made How many times this request has already been put on the wire again.
 * @param drawn A value in `[0, 1)`, which the caller draws so that this stays a function of its arguments.
 * @returns The delay to wait before the next attempt, or `null` when the answer is the one to act on.
 */
export function throttledRetryDelay(
    answer: Pick<ClientResponse, 'status' | 'headers'>,
    made: number,
    drawn: number,
): number | null {
    if (answer.status !== 429 || made >= mostThrottledRetries || statesAProblem(answer.headers)) {
        return null;
    }

    const spread = 0.75 + drawn / 2;
    const retryAfter = retryAfterOf(answer.headers);

    if (retryAfter === null) {
        return Math.round(firstThrottledDelay * 2 ** made * spread);
    }

    return retryAfter > longestHonouredRetryAfter ? null : Math.round(retryAfter * (1 + drawn / 4));
}

function statesAProblem(headers: Readonly<Record<string, string>>): boolean {
    return (headers['content-type'] ?? '').toLowerCase().startsWith('application/problem+json');
}

/**
 * The wait a `Retry-After` names, in milliseconds, where it names one in seconds.
 *
 * The deployment writes seconds. The other form the field allows, a date, is read as naming nothing rather than parsed,
 * because this client would then be comparing the deployment's clock with its own.
 */
function retryAfterOf(headers: Readonly<Record<string, string>>): number | null {
    const stated = headers['retry-after']?.trim() ?? '';

    return /^\d{1,6}$/u.test(stated) ? Number(stated) * 1_000 : null;
}
