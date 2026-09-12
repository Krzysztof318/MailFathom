// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

/** The route the acting person's own client preferences are read at and written back to, relative to the client prefix. */
export const clientPreferencesRoute = '/preferences';

/** What a person may have the client painted in, as the deployment names it. */
export type ClientThemePreference = 'system' | 'light' | 'dark';

const themePreferences: readonly ClientThemePreference[] = ['system', 'light', 'dark'];

/**
 * Which of the three renderings a message opens on, as the deployment names it.
 *
 * `reduced` is the closed document tree the service reduces a body to and is what an unset preference reads as.
 * `cleaned` is that same tree with what the sender wrapped it in dropped, derived per open by a model that answers in
 * block indices. `embeddedHtml` is the sender's own markup, with everything that runs or reports removed.
 */
export type ClientMessageView = 'reduced' | 'cleaned' | 'embeddedHtml';

const messageViews: readonly ClientMessageView[] = ['reduced', 'cleaned', 'embeddedHtml'];

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

    /** Whether opening a message marks it read on the person's own mail server, across every account they read. */
    readonly markReadOnOpen: boolean;

    /** Whether opening a conversation draws every message in it rather than the one it was opened at. */
    readonly expandWholeThread: boolean;

    /** Which of the three renderings an open message is drawn on. */
    readonly messageView: ClientMessageView;

    /** Whether the folder tree carries the standing views of what a derivation read in the mail. */
    readonly aiFiltersShown: boolean;

    /**
     * How long one of the client's own notifications stands, in whole seconds.
     *
     * It is bounded by {@link shortestNotificationSeconds} and {@link longestNotificationSeconds}, and the deployment
     * refuses a value outside that rather than clamping it — so a client that sent one is told, instead of drawing a
     * setting the deployment quietly disagrees with.
     */
    readonly notificationSeconds: number;
}

/** The shortest a notification may be asked to stand for, which is the deployment's own bound. */
export const shortestNotificationSeconds = 1;

/** The longest a notification may be asked to stand for, which is the deployment's own bound. */
export const longestNotificationSeconds = 30;

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
    markReadOnOpen: true,
    expandWholeThread: false,
    messageView: 'reduced',
    aiFiltersShown: true,
    notificationSeconds: 5,
};

/**
 * The most of one preferences answer this package reads before refusing it.
 *
 * The document is eight scalars, so this is far above anything the deployment will legitimately send and far below
 * anything worth buffering. It is the same order the write route bounds its request body at, for the same reason:
 * what the bound guards against is an answer that was never a preferences document.
 */
export const longestPreferencesAnswer = 4_096;

/** Reads what the signed-in person set about their own client, answering an expected failure as a value. */
export function readClientPreferences(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<ClientPreferences>> {
    return spanned(`GET ${clientPreferencesRoute}`, async () => {
        return answerOf(
            await send(transport, {
                method: 'GET',
                path: routeFor(session, clientPreferencesRoute),
                headers: headersFor(session),
                longestAnswer: longestPreferencesAnswer,
            }),
        );
    });
}

/**
 * States the whole document, and answers with what is now stored.
 *
 * The whole of it rather than the part that changed, because that is what the route accepts: a preference the body
 * omits is committed as its own unset answer rather than left at whatever the row held. A caller therefore sends back
 * what it last read with one value replaced, which is also why nothing here merges anything.
 */
export function writeClientPreferences(
    session: ClientSession,
    transport: MailFathomTransport,
    stated: ClientPreferences,
): Promise<ClientResult<ClientPreferences>> {
    return spanned(`POST ${clientPreferencesRoute}`, async () => {
        return answerOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, clientPreferencesRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(stated),
                longestAnswer: longestPreferencesAnswer,
            }),
        );
    });
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
    const markReadOnOpen = record['markReadOnOpen'];
    const expandWholeThread = record['expandWholeThread'];
    const messageView = record['messageView'];
    const aiFiltersShown = record['aiFiltersShown'];
    const notificationSeconds = record['notificationSeconds'];

    if (!isNotificationTime(notificationSeconds)) {
        return null;
    }

    if (
        typeof telemetryEnabled !== 'boolean' ||
        typeof openMailInTabs !== 'boolean' ||
        typeof markReadOnOpen !== 'boolean' ||
        typeof expandWholeThread !== 'boolean' ||
        typeof aiFiltersShown !== 'boolean'
    ) {
        return null;
    }

    if (!isThemePreference(theme) || !isMessageView(messageView)) {
        return null;
    }

    return {
        telemetryEnabled,
        theme,
        openMailInTabs,
        markReadOnOpen,
        expandWholeThread,
        messageView,
        aiFiltersShown,
        notificationSeconds,
    };
}

function isThemePreference(value: unknown): value is ClientThemePreference {
    return typeof value === 'string' && themePreferences.includes(value as ClientThemePreference);
}

function isMessageView(value: unknown): value is ClientMessageView {
    return typeof value === 'string' && messageViews.includes(value as ClientMessageView);
}

/**
 * Whether that is a notification time this deployment will take, which is a whole number of seconds inside the bound.
 *
 * Checked here as well as at the deployment's own boundary, for the reason every field on this surface is checked: an
 * answer is untrusted input, and a value outside the bound would reach a screen as a notification that never goes or
 * one nobody can read. A whole number, because the preference is stated in seconds and a fraction is not one.
 *
 * Exported because the screen that offers the setting asks the same question before stating one, and a second reading
 * of the bound is how a client comes to send what the deployment refuses.
 */
export function isNotificationTime(value: unknown): value is number {
    return (
        typeof value === 'number' &&
        Number.isInteger(value) &&
        value >= shortestNotificationSeconds &&
        value <= longestNotificationSeconds
    );
}
