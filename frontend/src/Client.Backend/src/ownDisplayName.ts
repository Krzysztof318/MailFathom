// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientFailure, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// What the signed-in person is called, which is the one thing a client drawing them cannot compose from the credential
// it holds. The read says whether this deployment would take a correction, so a screen draws the field it will
// actually be allowed to write rather than finding out by being refused.

/** The route the acting person's own name is read at and written back to, relative to the client prefix. */
export const ownDisplayNameRoute = '/display-name';

/** The name this deployment records the signed-in person under, and whether they may correct it here. */
export interface OwnDisplayName {
    readonly displayName: string;

    /**
     * Whether a correction from this caller would be accepted.
     *
     * The deployment answers it rather than leaving a client to discover it, and it does not say which of the two
     * things refuses one — a grant the credential lacks, or mail accounts a configuration source still declares. A
     * screen draws the same read-only field either way.
     */
    readonly changeable: boolean;
}

/**
 * The most of one name answer this package reads before refusing it.
 *
 * One name bounded at 128 characters and one boolean, with room for the widest UTF-8 encoding of each character and
 * the JSON around them. It is the same order the write route bounds its request body at, and for the same reason: what
 * the bound guards against is an answer that was never a name.
 */
export const longestDisplayNameAnswer = 1_024;

/** Reads the name this deployment records the signed-in person under, answering an expected failure as a value. */
export function readOwnDisplayName(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<OwnDisplayName>> {
    return spanned(`GET ${ownDisplayNameRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, ownDisplayNameRoute),
            headers: headersFor(session),
            longestAnswer: longestDisplayNameAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const name = parseDisplayName(response.body);

        return name === null ? failed('unreadable', response.status) : read(name);
    });
}

/**
 * What became of a correction somebody made to their own name.
 *
 * A refused name is separated from every other failure because it is the one a person acts on: what they typed is
 * blank, longer than the deployment stores, or a name somebody else there already carries, and the answer is to type
 * another one rather than to sign in again or to try later. The four reasons say nothing that reaches that, which is
 * why this is an outcome of its own rather than a fifth member added to them.
 */
export type OwnDisplayNameChange =
    | { readonly outcome: 'recorded'; readonly displayName: string }
    | { readonly outcome: 'notAcceptable' }
    | { readonly outcome: 'failed'; readonly failure: ClientFailure };

/**
 * Records the signed-in person under the name they corrected theirs to.
 *
 * The answer carries the name as it was stored rather than as it was sent, because a name is trimmed on its way in and
 * a screen that redrew what was typed would show something the deployment does not hold.
 */
export async function changeOwnDisplayName(
    session: ClientSession,
    transport: MailFathomTransport,
    displayName: string,
): Promise<OwnDisplayNameChange> {
    const response = await send(transport, {
        method: 'POST',
        path: routeFor(session, ownDisplayNameRoute),
        headers: { ...headersFor(session), 'Content-Type': 'application/json' },
        body: JSON.stringify({ displayName }),
        longestAnswer: longestDisplayNameAnswer,
    });

    return changeOf(response);
}

function changeOf(response: ClientResponse | null): OwnDisplayNameChange {
    if (response === null) {
        return { outcome: 'failed', failure: { reason: 'unavailable', status: null } };
    }

    // The deployment names what to correct in the body it refuses with, and none of that reaches the screen: what it
    // says is about this deployment's own entries, and the sentence a person is shown is the client's to word.
    if (response.status === 400) {
        return { outcome: 'notAcceptable' };
    }

    if (response.status !== 200) {
        return {
            outcome: 'failed',
            failure: { reason: failureReasonForStatus(response.status), status: response.status },
        };
    }

    const recorded = parseDisplayName(response.body);

    return recorded === null
        ? { outcome: 'failed', failure: { reason: 'unreadable', status: response.status } }
        : { outcome: 'recorded', displayName: recorded.displayName };
}

function parseDisplayName(body: string): OwnDisplayName | null {
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

    const displayName = record['displayName'];
    const changeable = record['changeable'];

    if (typeof displayName !== 'string' || typeof changeable !== 'boolean') {
        return null;
    }

    return { displayName, changeable };
}
