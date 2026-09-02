// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

/** The route the acting person's own client preferences are read at and written back to, relative to the client prefix. */
export const clientPreferencesRoute = '/preferences';

/** What a person may have the client painted in, as the deployment names it. */
export type ClientThemePreference = 'system' | 'light' | 'dark';

const themePreferences: readonly ClientThemePreference[] = ['system', 'light', 'dark'];

/**
 * What one person set about their own client, held on the deployment so it follows them between machines.
 *
 * Every preference is answered whether or not it was ever set, so a screen renders one shape rather than one per
 * combination of what happens to be stored. The document is closed and carries no version: a write states all of it,
 * and the last write wins.
 */
export interface ClientPreferences {
    readonly telemetryEnabled: boolean;
    readonly theme: ClientThemePreference;
    readonly openMailInTabs: boolean;
}

/**
 * What somebody who has set nothing is answered with, which is also what stands in until an answer arrives.
 *
 * It restates the deployment's own unset answer rather than deriving one, so a client with nothing read yet draws the
 * same screen the first answer will confirm instead of one that changes under the reader for no reason they caused.
 */
export const unsetClientPreferences: ClientPreferences = {
    telemetryEnabled: true,
    theme: 'system',
    openMailInTabs: false,
};

/**
 * The most of one preferences answer this package reads before refusing it.
 *
 * The document is three scalars, so this is far above anything the deployment will legitimately send and far below
 * anything worth buffering. It is the same order the write route bounds its request body at, for the same reason:
 * what the bound guards against is an answer that was never a preferences document.
 */
export const longestPreferencesAnswer = 4_096;

/** Reads what the signed-in person set about their own client, answering an expected failure as a value. */
export async function readClientPreferences(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<ClientPreferences>> {
    return answerOf(
        await send(transport, {
            method: 'GET',
            path: routeFor(session, clientPreferencesRoute),
            headers: headersFor(session),
            longestAnswer: longestPreferencesAnswer,
        }),
    );
}

/**
 * States the whole document, and answers with what is now stored.
 *
 * The whole of it rather than the part that changed, because that is what the route accepts: a preference the body
 * omits is committed as its own unset answer rather than left at whatever the row held. A caller therefore sends back
 * what it last read with one value replaced, which is also why nothing here merges anything.
 */
export async function writeClientPreferences(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: ClientPreferences,
): Promise<ClientResult<ClientPreferences>> {
    return answerOf(
        await send(transport, {
            method: 'POST',
            path: routeFor(session, clientPreferencesRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify(stated),
            longestAnswer: longestPreferencesAnswer,
        }),
    );
}

// Both routes answer the stored document, so both are read the same way. A deployment that holds no record for the
// caller answers the write with 404, which arrives here as `unavailable` like any other status this package did not
// expect to succeed: there is nothing a screen does differently about it that it does not already do about a
// deployment that would not take the change.
function answerOf(response: ClientResponse | null): ClientResult<ClientPreferences> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const preferences = parsePreferences(response.body);

    return preferences === null ? failed('unreadable', response.status) : read(preferences);
}

function parsePreferences(body: string): ClientPreferences | null {
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

    const telemetryEnabled = record['telemetryEnabled'];
    const theme = record['theme'];
    const openMailInTabs = record['openMailInTabs'];

    if (typeof telemetryEnabled !== 'boolean' || typeof openMailInTabs !== 'boolean') {
        return null;
    }

    if (!isThemePreference(theme)) {
        return null;
    }

    return { telemetryEnabled, theme, openMailInTabs };
}

/** Whether the value names one of the three themes this surface publishes. */
export function isThemePreference(value: unknown): value is ClientThemePreference {
    return typeof value === 'string' && themePreferences.includes(value as ClientThemePreference);
}
