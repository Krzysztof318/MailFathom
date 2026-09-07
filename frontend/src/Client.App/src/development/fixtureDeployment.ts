// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    clientRoutePrefix,
    mailPermissions,
    type ClientRequest,
    type ClientResponse,
} from '@mailfathom/client-backend';
import * as changes from '../../../../tests/fixtures/changes';
import * as deployment from '../../../../tests/fixtures/deployment';
import * as drafts from '../../../../tests/fixtures/drafts';
import * as mail from '../../../../tests/fixtures/mail';
import * as messages from '../../../../tests/fixtures/messages';
import * as notifications from '../../../../tests/fixtures/notifications';
import type { DeploymentTransport } from '../deployment/sendToDeployment';

// A deployment answered out of the corpus, so `pnpm dev:fixtures` serves a populated client with no database, no mail
// server and no service behind it. It is a development convenience and it never ships: `main.tsx` reaches this module
// through a dynamic import behind `import.meta.env.DEV`, a production build folds that condition away, and the browser
// suite asserts that neither this module nor the corpus reaches the bundle a deployment publishes.
//
// It is a transport rather than a server because `MailFathomTransport` is already the seam: every read in the client
// goes through the function the composition root supplies, so a second implementation of that one function reaches
// every screen and needs no mocking package, no service worker and no port. What travels over it is
// `frontend/tests/fixtures/`, the same corpus both suites read, which is why nothing here states a message of its own.
//
// Three requests do not go through this seam and are therefore not answered here: the picture a person is drawn by,
// the octets of a file a message carries, and the signal channel — each calls `fetch` in `deployment/` or opens a
// connection of its own rather than taking a transport. A run against the corpus meets all three as a deployment that
// did not answer, which is a state every screen drawing one already has to have.

/**
 * How the fixture deployment behaves, which is how the states that are not the resting one are reached.
 *
 * Every field is writable and read afresh on each request, because the point of them is to be changed while the client
 * is running: they are published as `window.mailfathomFixtures`, so turning `latency` up in a console puts the next
 * screen into its loading state with no fixture edited and nothing rebuilt.
 */
export interface FixtureDeploymentOptions {
    /** How long the deployment takes to answer, in milliseconds, which is what a loading state is looked at under. */
    latency: number;

    /**
     * The share of requests the deployment fails, from `0` to `1`.
     *
     * A failed request answers `503`, which every operation reads as `unavailable` — the failure a screen offers a
     * retry for rather than one it asks for a password over.
     */
    failureRate: number;

    /**
     * Whether every collection answers empty.
     *
     * The mailboxes and their folders are deliberately not among them: an empty folder, a search that found nothing
     * and a notification centre with nothing in it are each screens somebody has to be able to draw, and a client with
     * no mailbox at all draws none of them.
     */
    emptyCollections: boolean;

    /**
     * Whether the session has expired, so every request is refused as `401` and the client asks for the password again.
     *
     * Signing in is refused while it stands, which is what makes it the re-authentication state rather than one
     * refused read: the way back in is to turn it off, not to type the password again.
     */
    expiredSession: boolean;

    /** Whether the deployment is unreachable, so nothing answers at all and every screen draws its offline state. */
    unreachable: boolean;
}

/** What every option is before anybody changes one: a deployment that answers at once and is not in trouble. */
export const fixtureDeploymentDefaults: Readonly<FixtureDeploymentOptions> = {
    latency: 0,
    failureRate: 0,
    emptyCollections: false,
    expiredSession: false,
    unreachable: false,
};

declare global {
    interface Window {
        /** {@link FixtureDeploymentOptions}, present only in a run serving the corpus and absent from every build. */
        mailfathomFixtures?: FixtureDeploymentOptions;
    }
}

/** What the deployment holds between requests, so a choice made on a screen is still in force on the next read. */
export interface FixtureDeploymentState {
    preferences: Record<string, unknown>;
    displayName: { displayName: string; changeable: boolean };
}

/** The state a fixture deployment opens with, which is what the corpus says one holds before anything is chosen. */
export function fixtureDeploymentState(): FixtureDeploymentState {
    return { preferences: { ...deployment.clientPreferences }, displayName: { ...deployment.ownDisplayName } };
}

/**
 * What the fixture deployment answers one request with, or `null` where it answered nothing at all.
 *
 * It is a function of the request, the options and one drawn value rather than of a clock or a generator, so the whole
 * of the option handling is provable without either. `null` is what {@link fixtureDeployment} turns into a rejected
 * promise, which is what `send` reads as a deployment that could not be reached.
 *
 * @param draw A value in `[0, 1)` the caller decided, which is what `failureRate` is compared against.
 */
export function fixtureAnswer(
    request: ClientRequest,
    options: Readonly<FixtureDeploymentOptions>,
    draw: number,
    state: FixtureDeploymentState,
): ClientResponse | null {
    if (options.unreachable) {
        return null;
    }

    const surface = request.path.slice(request.path.indexOf(clientRoutePrefix) + clientRoutePrefix.length);
    const [route = '', query = ''] = surface.split('?');

    // The session route is the one place a refusal has to carry the challenge, because that is what tells the client it
    // reached MailFathom rather than something else refusing it: a refusal without one is read as a wrong address, and
    // the person is told there is no deployment there rather than that their credential was turned away.
    //
    // Two of its fields are the consumer's rather than the corpus's, and for one reason: only whoever routes this knows
    // them. The version is the build's, which the corpus itself states a placeholder for. The grant is this run's —
    // the corpus states a credential narrowed to reading and asking, which is a state worth looking at and is the
    // wrong one to be stuck in on a machine where the point is to look at every screen, so a development run is given
    // everything the client acts on and reaches the composer, the filing controls and the flags with it.
    if (route === '/session') {
        return options.expiredSession || request.headers['Authorization'] === undefined
            ? { status: 401, body: '', headers: { 'www-authenticate': 'Bearer, Basic realm="MailFathom"' } }
            : answering({ ...deployment.sessionAnswer, version: __MAILFATHOM_VERSION__, permissions: mailPermissions });
    }

    if (options.expiredSession) {
        return { status: 401, body: '', headers: {} };
    }

    if (draw < options.failureRate) {
        return { status: 503, body: '', headers: {} };
    }

    return answerFor(route, new URLSearchParams(query), request, options, state);
}

/**
 * The transport a run against the corpus reaches its deployment through, in the shape the composition root supplies.
 *
 * The options are read on every request rather than captured here, which is what makes changing one take effect on the
 * next thing a screen asks for instead of on the next reload.
 *
 * @param draw How the value `failureRate` is compared against is decided, which a test decides for itself.
 */
export function fixtureDeployment(draw: () => number = Math.random): DeploymentTransport {
    const state = fixtureDeploymentState();

    window.mailfathomFixtures = { ...fixtureDeploymentDefaults };

    return (abandoned) => async (request) => {
        const options = window.mailfathomFixtures ?? fixtureDeploymentDefaults;

        await answerAfter(options.latency, abandoned);

        const answer = fixtureAnswer(request, options, draw(), state);

        if (answer === null) {
            throw new Error('The fixture deployment is set unreachable, so nothing answered this request.');
        }

        return answer;
    };
}

/** One value of the corpus put on the wire as the deployment behind it would answer with it. */
function answering(answered: unknown): ClientResponse {
    return { status: 200, body: JSON.stringify(answered), headers: { 'content-type': 'application/json' } };
}

/**
 * Waits the latency out, and gives up where the screen that started the request stopped listening.
 *
 * Abandoning arrives as a rejected promise for the reason it does against a service: `send` turns anything thrown into
 * the absence of an answer, so a screen that walked away from a slow read meets exactly what it meets against one.
 */
function answerAfter(latency: number, abandoned: AbortSignal): Promise<void> {
    if (abandoned.aborted) {
        return Promise.reject(new Error('The request was abandoned before the fixture deployment answered.'));
    }

    if (latency <= 0) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        const giveUp = () => {
            clearTimeout(waiting);
            reject(new Error('The request was abandoned before the fixture deployment answered.'));
        };

        const waiting = setTimeout(() => {
            abandoned.removeEventListener('abort', giveUp);
            resolve();
        }, latency);

        abandoned.addEventListener('abort', giveUp, { once: true });
    });
}

/**
 * The corpus value one route answers with.
 *
 * The order is what a path has to be read in rather than a taste: a route that is a prefix of another is matched after
 * it, so `/notifications/unread-count` is not answered with the page `/notifications` answers.
 *
 * The mailboxes are the corpus's troubled set rather than its resting one, and that is the whole of what is chosen
 * here. It holds the resting mailbox as its first entry, so nothing is lost by it — and it is the only reading that
 * puts a failing mailbox, a mailbox that is behind, and an empty folder on the screen, which are three of the states
 * this exists to make reachable.
 */
function answerFor(
    route: string,
    asked: URLSearchParams,
    request: ClientRequest,
    options: Readonly<FixtureDeploymentOptions>,
    state: FixtureDeploymentState,
): ClientResponse {
    if (route === '/accounts') {
        return answering(deployment.troubledAccounts);
    }

    if (route === '/folders') {
        return answering(deployment.troubledFolders);
    }

    if (route === '/emails/search') {
        return answering(options.emptyCollections ? mail.noSearchResults : mail.searchResults);
    }

    if (route === '/emails') {
        if (options.emptyCollections) {
            return answering(mail.emptyFolderPage);
        }

        const cursor = Number(asked.get('cursor') ?? '0');

        return answering(mail.timelinePage(asked.get('direction') === 'backward' ? cursor - mail.rowsPerPage : cursor));
    }

    if (route === '/preferences') {
        if (request.method === 'POST') {
            state.preferences = recordIn(request.body) ?? state.preferences;
        }

        return answering(state.preferences);
    }

    if (route === '/display-name') {
        if (request.method === 'POST') {
            const written = recordIn(request.body)?.['displayName'];

            if (typeof written === 'string') {
                state.displayName = { displayName: written, changeable: true };
            }
        }

        return answering(state.displayName);
    }

    return (
        notificationAnswer(route, options) ??
        changeAnswer(route, options) ??
        draftAnswer(route, request) ??
        messageAnswer(route, asked) ?? { status: 404, body: '', headers: {} }
    );
}

/** What the notification centre and the bell above it answer with. */
function notificationAnswer(route: string, options: Readonly<FixtureDeploymentOptions>): ClientResponse | null {
    if (route === '/notifications/unread-count') {
        return answering(options.emptyCollections ? { unreadCount: 0 } : notifications.unreadNotificationCount);
    }

    if (route === '/notifications/read') {
        return answering(notifications.everyNotificationMarkedRead);
    }

    if (route.startsWith('/notifications/') && route.endsWith('/read-state')) {
        return answering(notifications.notificationMarkedRead);
    }

    if (route === '/notifications') {
        return answering(
            options.emptyCollections ? notifications.emptyNotificationPage : notifications.notificationPage,
        );
    }

    return null;
}

/** What a submitted batch of changes answers with, where the caller's own changes stand, and the signal ticket. */
function changeAnswer(route: string, options: Readonly<FixtureDeploymentOptions>): ClientResponse | null {
    if (route === '/mutations/flags') {
        return answering(changes.flagsRecorded);
    }

    if (route === '/mutations/moves') {
        return answering(changes.movesPartlyRecorded);
    }

    if (route === '/mutations') {
        return answering(options.emptyCollections ? { changes: [] } : changes.mutationRecords);
    }

    if (route === '/signals/ticket') {
        return answering(changes.signalTicket);
    }

    if (route === '/outbox/cancellation') {
        return answering({ outcome: 'Accepted' });
    }

    return null;
}

/** What the drafts routes answer: the record itself for a write, the queued send, and nothing for a discard. */
function draftAnswer(route: string, request: ClientRequest): ClientResponse | null {
    if (!route.startsWith('/drafts')) {
        return null;
    }

    if (request.method === 'DELETE') {
        return { status: 204, body: '', headers: {} };
    }

    return route.endsWith('/send') ? answering(drafts.queuedSend) : answering(drafts.savedDraft);
}

/**
 * What one message, its body, and the conversation it belongs to answer with.
 *
 * Which of the two the corpus states is drawn follows the identity in the route, because they are two different
 * screens: one whose sender wrote words and attached a file, and one who wrote no text part at all. The identity the
 * answer carries is the one that was asked for rather than the corpus's own, so a message opened out of the
 * conversation is the message the reader pressed — reading the request is the consumer's half, exactly as it is for
 * the page a cursor names.
 */
function messageAnswer(route: string, asked: URLSearchParams): ClientResponse | null {
    if (route.startsWith('/threads/')) {
        return answering(mail.conversation);
    }

    if (!route.startsWith('/messages/')) {
        return null;
    }

    const storedEmailId = decodeURIComponent(route.split('/')[2] ?? '');
    const markupOnly = storedEmailId === messages.markupOnlyId;

    if (route.endsWith('/body')) {
        const body = markupOnly
            ? messages.markupOnlyBody
            : messages.newsletterBody({
                  remoteImages: asked.get('remoteImages') === 'true',
                  fullHtml: asked.get('fullHtml') === 'true',
              });

        return answering({ ...body, storedEmailId });
    }

    const message = markupOnly ? messages.markupOnlyMessage : messages.newsletterMessage;

    return answering({ ...message, storedEmailId });
}

/** What a request body states, where it states an object at all. */
function recordIn(body: string | undefined): Record<string, unknown> | null {
    if (body === undefined) {
        return null;
    }

    let stated: unknown;

    try {
        stated = JSON.parse(body);
    } catch {
        return null;
    }

    return typeof stated === 'object' && stated !== null && !Array.isArray(stated)
        ? (stated as Record<string, unknown>)
        : null;
}
