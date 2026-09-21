// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientFailure, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { reported, spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// Which zone the signed-in person's own days are read in. It is the deployment's answer rather than the browser's, and
// that difference is the point of the route: the same value anchors every relative period a deployment resolves on
// their behalf — "this week", "since Tuesday" — so a client drawing its dates from the runtime and a deployment
// answering from the record would place the same message on two different days.
//
// The read says whether the value is still the one an unstated record falls to, which is what a client needs before
// proposing the zone the runtime reports: comparing identifiers instead would propose over somebody who chose the
// coordinated zone deliberately.

/** The route the acting person's own time zone is read at and written back to, relative to the client prefix. */
export const ownTimeZoneRoute = '/time-zone';

/** The zone this deployment reads the signed-in person's days in. */
export interface OwnTimeZone {
    /** The IANA identifier, such as `Europe/Warsaw`. */
    readonly timeZone: string;

    /** Whether that is the zone an unstated record falls to rather than one this person chose. */
    readonly isDefault: boolean;
}

/**
 * The most of one zone answer this package reads before refusing it.
 *
 * One identifier bounded as the deployment bounds it and one boolean, with room for the widest UTF-8 encoding of each
 * character and the JSON around them. What the bound guards against is an answer that was never a zone.
 */
export const longestTimeZoneAnswer = 1_024;

/** Reads the zone this deployment records the signed-in person's days in, answering an expected failure as a value. */
export function readOwnTimeZone(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<OwnTimeZone>> {
    return spanned(`GET ${ownTimeZoneRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, ownTimeZoneRoute),
            headers: headersFor(session),
            longestAnswer: longestTimeZoneAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const zone = parseTimeZone(response.body);

        return zone === null ? failed('unreadable', response.status) : read(zone);
    });
}

/**
 * What became of a zone somebody stated for themselves.
 *
 * An identifier the deployment does not know is separated from every other failure for the reason a refused name is:
 * it is the one a person acts on, by choosing another zone rather than by signing in again or trying later.
 */
export type OwnTimeZoneChange =
    | { readonly outcome: 'recorded'; readonly timeZone: string }
    | { readonly outcome: 'notAcceptable' }
    | { readonly outcome: 'failed'; readonly failure: ClientFailure };

/** Records the zone the signed-in person states their own days are read in. */
export function changeOwnTimeZone(
    session: ClientSession,
    transport: MailFathomTransport,
    timeZone: string,
): Promise<OwnTimeZoneChange> {
    return reported(
        `POST ${ownTimeZoneRoute}`,
        async () => {
            const response = await send(transport, {
                method: 'POST',
                path: routeFor(session, ownTimeZoneRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ timeZone }),
                longestAnswer: longestTimeZoneAnswer,
            });

            return changeOf(response);
        },

        // `reported` rather than `spanned`, for the reason the name correction gives: a zone this deployment does not
        // know is an answer rather than a failure, and recording it as one would put a mistyped identifier in the
        // dimension an operator reads for a deployment that is not answering.
        (change) => (change.outcome === 'failed' ? change.failure.reason : null),
    );
}

/**
 * The zone this runtime reports it is in, or `null` where it reports nothing at all.
 *
 * It is what a client proposes for somebody whose record still holds the default, and it is read through `Intl`
 * because that is the one place a browser and both desktop heads agree on the answer.
 *
 * Whatever comes back is passed on as it is, and nothing here judges whether it names a zone: the deployment's own
 * database is what decides that, it answers a name it does not carry with a refusal, and a second opinion composed
 * here from the shape of the string would only differ from it by being wrong. `UTC` is the case that proves it — it
 * is what a machine genuinely set to the coordinated zone reports, and what a runtime carrying no zone database falls
 * back to, and the two are indistinguishable from here. Recording it costs nothing either way, because a record
 * stating no zone already reads as `UTC` and the caller writes nothing where the two agree.
 */
export function reportedTimeZone(): string | null {
    const reportedZone: unknown = Intl.DateTimeFormat().resolvedOptions().timeZone;

    return typeof reportedZone === 'string' && reportedZone.length > 0 ? reportedZone : null;
}

function changeOf(response: ClientResponse | null): OwnTimeZoneChange {
    if (response === null) {
        return { outcome: 'failed', failure: { reason: 'unavailable', status: null } };
    }

    // The deployment names the form it takes in the body it refuses with, and none of that reaches the screen: the
    // sentence a person is shown is the client's to word.
    if (response.status === 400) {
        return { outcome: 'notAcceptable' };
    }

    if (response.status !== 200) {
        return {
            outcome: 'failed',
            failure: { reason: failureReasonForStatus(response.status), status: response.status },
        };
    }

    const recorded = parseTimeZone(response.body);

    return recorded === null
        ? { outcome: 'failed', failure: { reason: 'unreadable', status: response.status } }
        : { outcome: 'recorded', timeZone: recorded.timeZone };
}

function parseTimeZone(body: string): OwnTimeZone | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const timeZone = record['timeZone'];
    const isDefault = record['isDefault'];

    if (typeof timeZone !== 'string' || typeof isDefault !== 'boolean') {
        return null;
    }

    return { timeZone, isDefault };
}
