// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientResponse } from './transport';

// What this client does about a request that failed for a reason that passes on its own. It is the one answer to that
// question in the client, applied where every request reaches the wire, so no operation and no screen carries a retry
// of its own: a deployment that blinks for a second — a restart, a rolling upgrade, a proxy that dropped a connection,
// the limiter refusing a burst — costs a person a short wait rather than a *Retry* button on every screen that happened
// to be reading.
//
// Three kinds of failure pass on their own, and they differ in what they prove about the request:
//
// - A bare `429` is the transport's limiter refusing before any route ran, so it proves nothing was done and any
//   request may be sent again after it. A `429` carrying a problem document is the service declining the operation
//   itself — a deployment that has spent what its operator allows a provider — and the route that answers it reads it
//   as that, so it is handed back at once.
// - A `502`, `503`, or `504` is a deployment or a proxy in front of it not answering for now. It proves nothing about
//   whether the request was acted on: a gateway that timed out may have handed the request on first.
// - A network failure — a connection refused, reset, or cut short — proves nothing either.
//
// So only a request that is safe to repeat is sent again after either of the last two, and a request is safe to repeat
// when it is a `GET`. Every write on this surface is treated as not safe, whatever its method: no client route takes an
// idempotency key, and a `PUT` or `DELETE` whose first attempt landed is answered differently the second time — a
// draft already deleted is `404` — so the nominal idempotency of an HTTP method is not a route's contract. A write
// route that comes to guarantee a repeat lands once is added to `repeatable` below, with the guarantee it rests on.
//
// The worst case one request can spend waiting is `mostRetries` waits of at most `longestHonouredRetryAfter` and a
// quarter, 37.5 seconds, reached only by a deployment naming a `Retry-After` of ten seconds three times running. With no
// `Retry-After` the three waits add up to at most 2.2 seconds.

/** The most times one request is put on the wire again after a failure that passes on its own. */
export const mostRetries = 3;

const firstRetryDelay = 250;

/**
 * The longest `Retry-After` this client waits out rather than giving up on the request.
 *
 * A screen waiting on the read says it is waiting for as long as it does, so a refusal asking for a minute is reported
 * as the refusal it is rather than held behind a spinner for that minute: the person can ask again, which is a
 * decision somebody watching a spinner cannot make.
 */
const longestHonouredRetryAfter = 10_000;

/**
 * How long to wait before putting a request on the wire again, in milliseconds, or `null` where it is not put there
 * again.
 *
 * A `Retry-After` in seconds is honoured and never undercut, and one asking for longer than this client will hold a
 * screen for ends the attempts instead. Without one the wait doubles from a quarter of a second, spread so the requests
 * a burst or an outage had refused together are not put back together — which is what keeps a fleet of clients coming
 * back from one outage from being the next.
 *
 * @param method The method the request was made with.
 * @param answer What the deployment answered the attempt just made with, or `null` where nothing answered at all.
 * @param made How many times this request has already been put on the wire again.
 * @param drawn A value in `[0, 1)`, which the caller draws so that this stays a function of its arguments.
 * @returns The delay to wait before the next attempt, or `null` when the answer, or the failure, is the one to act on.
 */
export function retryDelay(
    method: string,
    answer: Pick<ClientResponse, 'status' | 'headers'> | null,
    made: number,
    drawn: number,
): number | null {
    if (made >= mostRetries || !passesOnItsOwn(method, answer)) {
        return null;
    }

    const retryAfter = answer === null ? null : retryAfterOf(answer.headers);

    if (retryAfter === null) {
        return Math.round(firstRetryDelay * 2 ** made * (0.75 + drawn / 2));
    }

    return retryAfter > longestHonouredRetryAfter ? null : Math.round(retryAfter * (1 + drawn / 4));
}

function passesOnItsOwn(method: string, answer: Pick<ClientResponse, 'status' | 'headers'> | null): boolean {
    if (answer === null) {
        return repeatable(method);
    }

    switch (answer.status) {
        case 429:
            return !statesAProblem(answer.headers);
        case 502:
        case 503:
        case 504:
            return repeatable(method);
        default:
            return false;
    }
}

function repeatable(method: string): boolean {
    return method.toUpperCase() === 'GET';
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
