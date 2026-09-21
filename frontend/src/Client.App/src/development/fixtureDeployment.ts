// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    clientRoutePrefix,
    mailPermissions,
    protectedResourceMetadataRoute,
    sessionExchangeRoute,
    sessionRevocationRoute,
    signInMethodsRoute,
    type ClientRequest,
    type ClientResponse,
} from '@mailfathom/client-backend';
import * as calendar from '../../../../tests/fixtures/calendar';
import * as changes from '../../../../tests/fixtures/changes';
import * as contacts from '../../../../tests/fixtures/contacts';
import * as deployment from '../../../../tests/fixtures/deployment';
import * as drafts from '../../../../tests/fixtures/drafts';
import * as mail from '../../../../tests/fixtures/mail';
import * as messages from '../../../../tests/fixtures/messages';
import * as notifications from '../../../../tests/fixtures/notifications';
import * as tasks from '../../../../tests/fixtures/tasks';
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
 * It is a function of the request, the options and one drawn value rather than of a generator, so the whole of the
 * option handling is provable without one. Two routes are the exception and both state it: minting a session reads the
 * clock for the instant it ends and the corpus's own record of what it has already minted, neither of which a caller
 * decides, and the task routes read it for the day they answer about — a task list is grouped under *today*, *this
 * week* and *later*, and nothing in a request for one says which day that is. `null` is what
 * {@link fixtureDeployment} turns into a rejected promise, which is what `send` reads as a deployment that could not
 * be reached.
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

    // The one document published outside the client prefix, which RFC 9728 puts at the root with the prefix after it —
    // so it is matched on the whole path before anything slices the prefix off, which would leave nothing to match on.
    if (request.path.endsWith(protectedResourceMetadataRoute)) {
        // The identifier is composed from the address this was read at rather than stated by the corpus, because the
        // client checks that the document names the deployment it came from — and where the fixture is served from is
        // whatever port a development run took.
        return answering({
            ...deployment.protectedResource,
            resource: `${request.path.slice(0, -protectedResourceMetadataRoute.length)}${clientRoutePrefix}`,
        });
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
    // The exchange, which is the one request in a run that carries a password. The corpus mints a token per
    // credential rather than one token, so signing in as somebody else on the same run is a session of its own exactly
    // as it is against a deployment — and the client presents that token on everything afterwards.
    if (route === sessionExchangeRoute) {
        return options.expiredSession || request.headers['Authorization'] === undefined
            ? { status: 401, body: '', headers: { 'www-authenticate': 'Bearer, Basic realm="MailFathom"' } }
            : answering(fixtureSession(request.headers['Authorization']));
    }

    // Ending a session is answered and nothing is remembered: what a revoked token would be refused on is state this
    // deployment does not keep, and a run that signed out has already forgotten what it held.
    if (route === sessionRevocationRoute) {
        return { status: 204, body: '', headers: {} };
    }

    // The two documents a sign-in screen is composed from, both answered to a caller holding nothing, because a person
    // has to be shown what they may do before they can do any of it. The corpus publishes every way in at once so a
    // development run opens on the screen the design draws rather than on a password form with nothing beside it;
    // nothing behind these servers is answered here, so a control that is pressed reaches a provider that is not there.
    if (route === signInMethodsRoute) {
        return answering(deployment.signInMethodsOffered);
    }

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

/** Which token the corpus minted for each credential it has been presented, so one credential is answered one session. */
const mintedFor = new Map<string, string>();

/**
 * The session the corpus mints for a credential, which lives as long as a deployment's own does.
 *
 * A token per credential rather than one token, so signing in as somebody else on the same run is a session of its own
 * exactly as it is against a deployment. It is numbered rather than derived from what was presented, because a real
 * deployment answers a value carrying nothing of the credential — and a corpus that spelled a password into the token
 * would put one into every capture, every console log, and every stored session a run leaves behind.
 */
function fixtureSession(credential: string): { token: string; expiresAt: string } {
    const token = mintedFor.get(credential) ?? `mfs_fixture${String(mintedFor.size + 1)}.Zml4dHVyZS1zZXNzaW9u`;

    mintedFor.set(credential, token);

    return { token, expiresAt: new Date(Date.now() + sessionLifetime).toISOString() };
}

/** How long a session the corpus mints lasts, which is what the service's own is. */
const sessionLifetime = 43_200_000;

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

    // Before the search itself, because that route's path is a prefix of this one.
    if (route === '/emails/search/phrasing') {
        return request.method === 'GET' ? answering({ readsPhrases: true }) : answering(mail.phraseReading);
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

    if (route === '/citations/resolution') {
        return answering(mail.citationResolutions(fragmentsIn(request.body)));
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

    // The capability and the drafting itself, on one address the way the surface publishes them: the read is what the
    // composer asks before it draws the block, and the write is what the block presses for.
    if (route === '/replies/drafting') {
        return answering(request.method === 'POST' ? drafts.draftedReply : drafts.draftsReplies);
    }

    return (
        calendarAnswer(route, asked, request, options) ??
        notificationAnswer(route, options) ??
        changeAnswer(route, options) ??
        contactAnswer(route, request, options) ??
        draftAnswer(route, request) ??
        taskAnswer(route, request, options) ??
        messageAnswer(route, asked) ?? { status: 404, body: '', headers: {} }
    );
}

/**
 * What the task list, the two writes over one task, and the day's arrangement answer with.
 *
 * The order is the one the routes above are written in: `/tasks/today/layout` and `/tasks/proposed` are read before
 * `/tasks`, which is a prefix of both. Which day is answered about is the deployment's own reading of the clock rather
 * than anything the request states, for the reason {@link fixtureAnswer} gives.
 */
function taskAnswer(
    route: string,
    request: ClientRequest,
    options: Readonly<FixtureDeploymentOptions>,
): ClientResponse | null {
    if (!route.startsWith('/tasks')) {
        return null;
    }

    if (route === '/tasks/today/layout') {
        // The read is whether this deployment arranges a day at all, and the write is the suggestion itself. A
        // deployment with nothing left to place answers that rather than failing, which is its own sentence on the
        // panel and is what the empty corpus reaches.
        if (request.method !== 'POST') {
            return answering(tasks.daysArranged);
        }

        return answering(options.emptyCollections ? tasks.dayNotArranged : tasks.dayArrangement(dayHere()));
    }

    if (request.method === 'DELETE') {
        return answering(tasks.taskErased);
    }

    if (route.endsWith('/completion')) {
        return answering(tasks.taskCompleted);
    }

    if (route.endsWith('/acceptance')) {
        return answering(tasks.taskAccepted);
    }

    if (request.method === 'POST') {
        return answering(tasks.taskWritten);
    }

    if (request.method === 'PUT') {
        return answering(tasks.taskRevised);
    }

    if (route === '/tasks/proposed') {
        return answering(options.emptyCollections ? tasks.emptyTaskPage : tasks.proposedTasks(dayHere()));
    }

    return answering(options.emptyCollections ? tasks.emptyTaskPage : tasks.committedTasks(dayHere()));
}

/**
 * What the calendar answers: the window a view is drawing, the capability behind the description field, and the four
 * writes.
 *
 * The drafting address is read before the window for the reason the collected book is read before the asserted one —
 * the window's path is a prefix of it.
 */
function calendarAnswer(
    route: string,
    asked: URLSearchParams,
    request: ClientRequest,
    options: Readonly<FixtureDeploymentOptions>,
): ClientResponse | null {
    if (!route.startsWith('/calendar')) {
        return null;
    }

    if (route === '/calendar/drafts') {
        return request.method === 'GET'
            ? answering(calendar.calendarDescriptionsRead)
            : answering(calendar.calendarEventDrafted);
    }

    if (request.method === 'DELETE') {
        return { status: 204, body: '', headers: {} };
    }

    if (request.method === 'POST' || request.method === 'PUT') {
        return answering(calendar.calendarEventWritten);
    }

    if (route === '/calendar') {
        return answering(
            options.emptyCollections
                ? calendar.emptyCalendarWindow
                : calendar.calendarWindow(asked.get('from') ?? '', asked.get('until') ?? ''),
        );
    }

    return answering(calendar.calendarEventWritten);
}

/** The day the machine running this is on, as a calendar day is spelled, which is the day a task list is grouped by. */
function dayHere(): string {
    const now = new Date();

    return `${String(now.getFullYear())}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

/**
 * What the two address books and one person's own page answer with.
 *
 * The collected book is read before the asserted one because its path has the other's as a prefix, which is the same
 * ordering the routes above are written in. Which book a promotion moves somebody into is not modelled: the answer
 * states the record as the surface states it, and the screen reads the book again rather than correcting what it holds.
 */
function contactAnswer(
    route: string,
    request: ClientRequest,
    options: Readonly<FixtureDeploymentOptions>,
): ClientResponse | null {
    if (!route.startsWith('/contacts')) {
        return null;
    }

    if (request.method === 'DELETE') {
        return answering(contacts.contactErased);
    }

    if (route.endsWith('/promotion')) {
        return answering(contacts.contactPromoted);
    }

    if (route.endsWith('/correspondence')) {
        // The collected record is the person nothing was found about, which is how both columns' empty state is
        // reached without any option being turned on.
        return answering(
            route.includes(contacts.collectedContact.id) || options.emptyCollections
                ? contacts.noContactCorrespondence
                : contacts.contactCorrespondence,
        );
    }

    if (request.method === 'POST') {
        return answering(contacts.contactWritten);
    }

    if (route === '/contacts/collected') {
        return answering(options.emptyCollections ? contacts.emptyContactPage : contacts.collectedContactPage);
    }

    if (route === '/contacts') {
        return answering(options.emptyCollections ? contacts.emptyContactPage : contacts.assertedContactPage);
    }

    return answering(contacts.assertedContact);
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
        // The conversation and where it stands are two reads of one correspondence, and the corpus answers both: a
        // deployment that had derived nothing would draw the absence instead, which is a state of the block rather
        // than the one worth serving as the example.
        return answering(route.endsWith('/state') ? mail.conversationState : mail.conversation);
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

// The passages a citation request named, in the order it named them, which is what the answer is paired against. A
// body this cannot read answers no citations at all, which the client refuses as an answer it cannot pair — the same
// thing a deployment answering nonsense would produce.
function fragmentsIn(body: string | undefined): readonly string[] {
    const citations = recordIn(body)?.['citations'];

    if (!Array.isArray(citations)) {
        return [];
    }

    return citations
        .map((citation) => (citation as Record<string, unknown> | null)?.['fragment'])
        .filter((fragment): fragment is string => typeof fragment === 'string');
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
