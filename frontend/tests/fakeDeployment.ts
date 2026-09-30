// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { BrowserContext, Page } from '@playwright/test';

import * as agent from './fixtures/agent';
import * as calendar from './fixtures/calendar';
import * as changes from './fixtures/changes';
import * as contacts from './fixtures/contacts';
import * as deployment from './fixtures/deployment';
import * as discovery from './fixtures/discovery';
import * as drafts from './fixtures/drafts';
import * as mail from './fixtures/mail';
import * as messages from './fixtures/messages';
import * as notifications from './fixtures/notifications';
import * as tasks from './fixtures/tasks';

// The deployment the browser suite signs in to: one per check, holding what the corpus says a mailbox holds and
// applying what the client writes to it, so a read after an act answers what the act left behind rather than what a
// fixture happened to say. It is a consumer of the corpus rather than a part of it — `frontend/tests/AGENTS.md`
// § *The corpus* keeps routing out of `fixtures/`, exactly as `pnpm dev:fixtures` keeps its own transport out — and
// every answer it gives is a corpus value or one built from them, parsed by the bundle through `Client.Backend` as a
// deployment's would be.
//
// Nothing here waits. An answer an agent composes is canned and returned at once; where a check has to look at one
// still running, `startNextAnswerRunning` makes the next question read as running once and finished on the read after.

/** The prefix every route of the client surface sits under, and the one the fake answers beneath. */
const clientPrefix = '/api/client';

/** The one document published outside the prefix, which RFC 9728 puts at the root with the prefix after it. */
const protectedResourcePath = `/.well-known/oauth-protected-resource${clientPrefix}`;

/** The addresses the fake answers, on whatever origin the preview server took. */
const servedRoutes = /^[^?#]*\/(?:api\/client\/|\.well-known\/oauth-protected-resource\/api\/client(?:[/?#]|$))/u;

/** The address the client opens its signal connection at, with the ticket on it. */
const hubAddress = /^[^?#]*\/api\/client\/signals(?:\?|$)/u;

/** What ends every message of SignalR's JSON protocol, which is how several arrive in one frame. */
const recordSeparator = '\u001e';

/** The SignalR message types the hub speaks: a method invoked on the client, and the ping that keeps a connection. */
const invocation = 1;
const ping = 6;

/**
 * The half of a routed WebSocket the hub speaks through.
 *
 * Playwright's own route is one, and it is named apart so the hub can be asked with no page at all, as the rest of the
 * fake is: `fakeDeployment.spec.ts` stands a socket of its own in front of it.
 */
export interface HubSocket {
    url(): string;
    onMessage(handler: (message: string | Buffer) => void): void;
    onClose(handler: () => void): void;
    send(message: string): void;
    close(options?: { code?: number; reason?: string }): Promise<void>;
}

/** The flags a change from elsewhere writes, each left alone where it is not stated. */
interface WrittenFlags {
    readonly seen?: boolean;
    readonly flagged?: boolean;
}

/** One request the page issued, as the fake reads it. */
export interface FakeRequest {
    readonly method: string;
    readonly url: string;
    readonly body: string | null;

    /** What the request declared its body to be, which is where a staged file's media type travels. */
    readonly contentType?: string;

    /** The credential the request presented, which is what tells a renewal from a sign-in at the one route both reach. */
    readonly authorization?: string;
}

/** What the fake answers one request with, as the route fulfils it. */
export interface FakeAnswer {
    readonly status: number;
    readonly body: string;
    readonly contentType?: string;
}

/** One request the page issued to the fake, kept so a check can assert what the client asked for and with what. */
export interface IssuedRequest {
    readonly method: string;

    /** The path beneath `/api/client`, without the query, so a message or a draft is named in it where the route does. */
    readonly route: string;

    readonly query: URLSearchParams;

    /** The body as the JSON it was sent as, or the text where it was not JSON, or `null` where there was none. */
    readonly body: unknown;

    /** The body exactly as it travelled, which is what a staged file is: its octets, whatever they happen to parse as. */
    readonly text: string | null;

    /** What the request declared its body to be, or `null` where it declared nothing. */
    readonly contentType: string | null;
}

/** One change the fake wrote down, read back the way the records route publishes one. */
interface MutationRecord {
    readonly recordId: string;
    readonly storedEmailId: string;
    readonly mutation: 'set-seen' | 'set-flagged' | 'relocate' | 'delete';
    state: 'pending' | 'completed' | 'cancelled' | 'dead-lettered';
}

/** Where a message stands in the corpus before anything is written to it. */
interface MessageFacts {
    readonly folder: string;
    readonly unread: boolean;
    readonly flagged: boolean;
}

/** A question put to Discover, and how far the check has read it. */
interface DiscoveryRun {
    reads: number;
    readonly startsRunning: boolean;
    stopped: boolean;
}

/** A conversation with the Agent as the fake holds it: which corpus conversation it reads as, and what was added. */
interface Conversation {
    title: string;
    readonly startedAt: string;
    archived: boolean;
    readonly reads: 'answered' | 'composing';

    /** What a conversation started here was asked, and `null` for one the corpus states. */
    readonly asked: { readonly text: unknown; readonly scope: unknown } | null;

    readonly added: { sequence: number; entry: Readonly<Record<string, unknown>> }[];
    readsTaken: number;
    readonly startsRunning: boolean;
    stopped: boolean;
}

/** One row of the notification centre as the fake holds it: what the corpus states, and whether it now stands read. */
type HeldNotification = Readonly<Record<string, unknown>> & { readonly id: string; read: boolean };

/** Where a conversation that is still composing stops, which is the question, its start, and one status line. */
const composingEntriesShown = 3;

/** When every record the fake writes down was written, which no check reads and the record route requires. */
const recordedAt = '2026-08-31T09:41:00+00:00';

/** How many passes stand behind a record in each state: a refused one is one the account stopped retrying. */
const attemptsBehind: Readonly<Record<MutationRecord['state'], number>> = {
    pending: 0,
    completed: 1,
    cancelled: 0,
    'dead-lettered': 5,
};

/** What the fake reads of a row a folder page lists; every other field travels as the corpus states it. */
interface FolderRow {
    readonly id: string;
    readonly folder: string;
    readonly unread: boolean;
    readonly flagged: boolean;
    readonly hasAttachments: boolean;
}

/** A record the corpus or a write states, held as the JSON it travels as. */
type Held = Readonly<Record<string, unknown>>;

/**
 * One folder of the mailbox's hierarchy as the management route publishes it.
 *
 * Stated rather than read off the corpus, whose literal pins each folder to the parent and role it happens to have — and
 * a folder act moves a folder under any parent.
 */
interface HeldFolder {
    readonly id: string;
    readonly parentId: string | null;
    readonly name: string;
    readonly role: string | null;
    readonly allowedActs: readonly string[];
}

/** The two halves of the task list, as the corpus states them for the day the list was first read on. */
interface TaskLists {
    readonly committed: Held[];
    readonly proposed: Held[];
}

/** One file staged against a draft, with the octets it went up with, which the stored message answers again. */
interface StagedFile {
    readonly attachmentId: string;
    readonly fileName: string;
    readonly mediaType: string;
    readonly octets: string;
}

/**
 * A draft the client wrote here: what its last save stated, the files staged against it, and the stored message filed
 * for it in the drafts folder — which is how a deployment holds one, a row and a stored message in the same act.
 */
interface HeldDraft {
    readonly storedEmailId: string;
    readonly record: Held;
    readonly written: Held;
    readonly files: StagedFile[];
}

/** The drafts folder of the corpus mailbox, which is where every draft a check writes is filed. */
const draftsFolder = deployment.mailFolders.accounts
    .flatMap(({ folders }) => folders)
    .find(({ role }) => role === 'Drafts')?.alias;

/** Who the corpus mailbox belongs to, as the conversation it answered in names them, which is who a draft is from. */
const author = mail.conversation().messages.find(({ email }) => email.folder === 'SENT')?.email;

/** Where in the generated folder the one row standing for a correspondence sits, read off the corpus's first page. */
const conversationRowPosition = mail.timelinePage(0).emails.findIndex(({ threadId }) => threadId !== null);

export class FakeDeployment {
    /** Every request that reached the fake, in the order the page issued them. */
    readonly issued: IssuedRequest[] = [];

    /** Every request the fake has no answer for, as `METHOD /route`, which the harness fails the check on. */
    readonly unimplemented: string[] = [];

    private permissions: readonly string[] = deployment.sessionAnswer.permissions;
    private preferences: Readonly<Record<string, unknown>> = { ...deployment.clientPreferences };
    private displayName = { ...deployment.ownDisplayName };
    private timeZone = { ...deployment.ownTimeZone };

    /** The folder each message was moved to, for a message that no longer sits where the corpus put it. */
    private readonly placed = new Map<string, string>();

    private readonly flags = new Map<string, { unread?: boolean; flagged?: boolean }>();

    /** Messages a delete was written down for and not taken back, which no read answers with any more. */
    private readonly deleted = new Set<string>();

    private readonly records = new Map<string, MutationRecord>();

    /** How far each folder's two counts have moved from what the corpus states, by alias. */
    private readonly counted = new Map<string, { stored: number; unread: number }>();

    /**
     * The mailbox's folders as the management route reads them, with every folder act applied since.
     *
     * Both folder routes answer from it, because they are two readings of one mailbox: a folder deleted here is one
     * the tree no longer lists and mail can no longer be moved into, and where a folder sits is read off it — which is
     * the service's reading of a declaration rather than of the last synchronization.
     */
    private hierarchy: readonly HeldFolder[] = deployment.managedFolders.folders;

    /** The folders a folder act made, which the folders route answers beside the corpus's own. */
    private readonly made = new Set<string>();

    /** The notification centre, newest first, with every read mark and erasure written since. */
    private notificationsHeld: HeldNotification[] = notifications.notificationPage.notifications.map((row) => ({
        ...row,
    }));

    private readonly draftsHeld = new Map<string, HeldDraft>();

    /** The drafts a send queued, by the outgoing identity it answered, until a withdrawal puts one back. */
    private readonly sendsQueued = new Map<string, { readonly draftId: string; readonly held: HeldDraft }>();
    private readonly runs = new Map<string, DiscoveryRun>();
    private readonly conversations = new Map<string, Conversation>([
        [agent.answeredConversationId, this.corpusConversation(agent.answeredConversationId, 'answered')],
        [agent.composingConversationId, this.corpusConversation(agent.composingConversationId, 'composing')],
        ...agent.agentHistory.conversations
            .filter(({ archived }) => archived)
            .map(({ id }) => [id, this.corpusConversation(id, 'answered')] as const),
    ]);

    /**
     * Events written or amended here, by identity, which every window they fall in answers with.
     *
     * An amended corpus event is held here under its own identity too, because the corpus places its entries against
     * the span that was asked for and an amendment states an instant — so from then on it is where the amendment put it.
     */
    private readonly eventsWritten = new Map<string, Held>();

    /** Events a delete took off the calendar, which no window answers with any more. */
    private readonly eventsGone = new Set<string>();

    /** Read on the first request for either half rather than built here, because the day it is stated for is the zone's. */
    private taskLists: TaskLists | null = null;

    private readonly contactsWritten: Held[] = [];
    private readonly contactsErased = new Set<string>();

    /** Mail that arrived while the check ran, newest first, which the inbox lists ahead of what the corpus holds. */
    private readonly arrived: FolderRow[] = [];

    private accounts: Held = deployment.mailAccounts;

    /** Whether the ticket is minted at all, which is whether this deployment serves a signal channel. */
    private signalsServed = false;

    /** Tickets minted and not yet presented, because a ticket opens exactly one connection. */
    private readonly ticketsUnspent = new Set<string>();

    /** The connections whose handshake has been answered, which are the ones a signal is said on. */
    private readonly connections = new Set<HubSocket>();

    /** Whether the deployment has stopped answering, which refuses every request and every connection. */
    private unreachable = false;

    /** Whether the next change the client asks for is written down and never taken. */
    private refusingNextChange = false;

    private minted = 0;
    private nextAnswerRunning = false;

    private readonly version: string;

    /** @param version The version this build answers with, which only the build knows and the corpus leaves open. */
    constructor(version: string) {
        this.version = version;
    }

    /** Answers every route of the client surface on the page or the context from here on, until it is closed. */
    async serve(target: Page | BrowserContext): Promise<void> {
        // A pattern rather than a predicate, because Playwright matches a pattern in the browser's driver and a predicate
        // in this process: a predicate would route every request the page makes — each file of the bundle among them —
        // through the runner to be asked about, and under a loaded machine that is most of a check's time.
        await target.route(servedRoutes, async (route) => {
            const request = route.request();
            const contentType = request.headers()['content-type'];
            const authorization = request.headers()['authorization'];
            const asked: FakeRequest = {
                method: request.method(),
                url: request.url(),
                body: request.postData(),
                ...(contentType === undefined ? {} : { contentType }),
                ...(authorization === undefined ? {} : { authorization }),
            };

            // Refused as a network refuses it rather than answered with a status, because a deployment that stopped
            // answering answers nothing. What was asked is still written down, which is how a check reads how often
            // the client reached for it.
            if (this.unreachable) {
                this.issued.push(issuedFrom(asked));
                await route.abort('connectionrefused');

                return;
            }

            const answer = this.answer(asked);

            await route.fulfill({
                status: answer.status,
                body: answer.body,
                ...(answer.contentType === undefined ? {} : { contentType: answer.contentType }),
            });
        });

        await target.routeWebSocket(hubAddress, (socket) => {
            this.connect(socket);
        });
    }

    /**
     * Takes one connection the client opened at the hub, speaking SignalR's JSON protocol as the service's hub does.
     *
     * The ticket on its address opens it once and is spent by opening it, so a client reopening the channel has to mint
     * another — which is the service's rule, and what makes a reopening visible in the requests a check reads.
     */
    connect(socket: HubSocket): void {
        const ticket = new URL(socket.url()).searchParams.get('access_token');

        if (this.unreachable || ticket === null || !this.ticketsUnspent.delete(ticket)) {
            void socket.close({ code: 1008, reason: 'The ticket opens no connection.' });

            return;
        }

        socket.onMessage((message) => {
            for (const frame of String(message).split(recordSeparator)) {
                this.heard(socket, recordIn(parsedBody(frame)));
            }
        });
        socket.onClose(() => {
            this.connections.delete(socket);
        });
    }

    /** How many connections stand, which is what a check waits for before anything is said over one. */
    channelsOpen(): number {
        return this.connections.size;
    }

    /** Serves the signal channel from the next ticket the client asks for; until then the ticket is refused. */
    serveSignals(): void {
        this.signalsServed = true;
    }

    /** Stops answering anything at all, closing whatever connection stands, until {@link answerAgain}. */
    stopAnswering(): void {
        this.unreachable = true;

        for (const socket of this.connections) {
            void socket.close({ code: 1011, reason: 'The deployment stopped.' });
        }

        this.connections.clear();
    }

    /** Answers again, holding everything it held before it stopped. */
    answerAgain(): void {
        this.unreachable = false;
    }

    /**
     * Writes the next change the client asks for down and never takes it: the record stands as the account's
     * reconciliation leaves one it stopped retrying, and the mailbox is as it was before the change was asked for.
     */
    refuseNextChange(): void {
        this.refusingNextChange = true;
    }

    /** Delivers the corpus's arriving message to the head of the inbox, and says so over the channel. */
    deliver(): void {
        const { account, folder } = mail.arrivingRow;

        this.arrived.unshift(mail.arrivingRow);
        this.count(folder, 1, 1);
        this.signal({ kind: 'mail.arrived', account, folder, count: 1 });
    }

    /** Marks a message from another client of the same mailbox, and states the flags it now carries over the channel. */
    flagElsewhere(storedEmailId: string, flags: WrittenFlags): void {
        const facts = this.flagsWritten(storedEmailId, flags);

        this.signal({
            kind: 'mail.flags.changed',
            account: 'work',
            folder: facts.folder,
            flags: [{ email: storedEmailId, isSeen: flags.seen ?? null, isFlagged: flags.flagged ?? null }],
        });
    }

    /** Marks a message from elsewhere and says no more over the channel than that it changed, which it is read again for. */
    changeElsewhere(storedEmailId: string, flags: WrittenFlags): void {
        const facts = this.flagsWritten(storedEmailId, flags);

        this.signal({ kind: 'mail.changed', account: 'work', folder: facts.folder, emails: [storedEmailId] });
    }

    /** Makes a folder on the mail server from elsewhere, which the account declares and the channel says moved. */
    makeFolderElsewhere(name: string): void {
        this.folderMade({ name, parentId: null });
        this.signal({ kind: 'folders.changed', account: 'work' });
    }

    /** Moves the accounts to the corpus's troubled reading, and says an account's state moved. */
    troubleTheAccounts(): void {
        this.accounts = deployment.troubledAccounts;
        this.signal({ kind: 'account.state', account: deployment.failingAccount.id });
    }

    /** The requests the page issued with this method to this route, in the order it issued them. */
    requests(method: string, route: string): IssuedRequest[] {
        return this.issued.filter((issued) => issued.method === method && issued.route === route);
    }

    /**
     * Raises the corpus's arriving notification at the head of the centre, which is what a deployment does while nobody
     * is looking at the screen: nothing is pushed, and the client finds it the next time it asks for the count.
     */
    raiseNotification(): void {
        this.notificationsHeld = [{ ...notifications.arrivingNotification }, ...this.notificationsHeld];
    }

    /**
     * Grants the credential more than the corpus's reading and asking, from the next time the session is read.
     *
     * The corpus states the two permissions a frame needs and no more, so a check whose journey writes something one
     * of the others guards grants it before it signs in, exactly as a deployment's operator would.
     */
    grant(...permissions: readonly string[]): void {
        this.permissions = [...this.permissions, ...permissions];
    }

    /** Makes the next question put to Discover or to the Agent read as running once, and finished on the read after. */
    startNextAnswerRunning(): void {
        this.nextAnswerRunning = true;
    }

    /** What the fake answers one request with, which is the whole of it and needs no browser to be asked. */
    answer(request: FakeRequest): FakeAnswer {
        const address = new URL(request.url);

        if (address.pathname.startsWith(protectedResourcePath)) {
            // The identifier is composed onto the address this was read at, because the client checks that the document
            // names the deployment it came from — and the port is whatever the preview server took.
            return answering({ ...deployment.protectedResource, resource: `${address.origin}${clientPrefix}` });
        }

        const issued = issuedFrom(request);

        this.issued.push(issued);

        const answer = this.answerFor(issued, request.authorization ?? null);

        if (answer === null) {
            this.unimplemented.push(`${issued.method} ${issued.route}`);

            return { status: 501, body: '' };
        }

        return answer;
    }

    /**
     * One message a connection sent. The handshake names the protocol and is answered with an empty record, which is
     * when the connection stands; a ping is answered with one, which is what keeps the client's own timeout from
     * closing a connection nothing else is said on. Everything else a client may send is nothing the hub acts on.
     */
    private heard(socket: HubSocket, message: Held | null): void {
        if (message === null) {
            return;
        }

        if (typeof message['protocol'] === 'string') {
            socket.send(`{}${recordSeparator}`);
            this.connections.add(socket);

            return;
        }

        if (message['type'] === ping) {
            socket.send(`${JSON.stringify({ type: ping })}${recordSeparator}`);
        }
    }

    /** Says one statement on every connection that stands, as the hub invokes the one method the client listens on. */
    private signal(payload: Held): void {
        const said = `${JSON.stringify({ type: invocation, target: 'signal', arguments: [payload] })}${recordSeparator}`;

        for (const socket of this.connections) {
            socket.send(said);
        }
    }

    private ticketMinted() {
        const ticket = this.mintedIdentity();

        this.ticketsUnspent.add(ticket);

        return { ...changes.signalTicket, ticket };
    }

    private answerFor(request: IssuedRequest, authorization: string | null): FakeAnswer | null {
        const { method, route } = request;
        const segments = route.split('/').slice(1).map(decodeURIComponent);

        switch (`${method} ${route}`) {
            case 'GET /sign-in-methods':
                return answering(deployment.signInMethods);
            // One route mints a session and renews one, told apart by what was presented to it: the password a person
            // typed, or the session a client is holding, which a deployment destroys as it answers with the next.
            case 'POST /session/token':
                return answering(
                    authorization?.startsWith('Bearer ') === true
                        ? deployment.renewedSession
                        : deployment.mintedSession,
                );
            case 'POST /session/token/revocation':
                return nothing;
            case 'GET /session':
                return answering({ ...deployment.sessionAnswer, version: this.version, permissions: this.permissions });
            case 'GET /preferences':
                return answering(this.preferences);
            case 'POST /preferences':
                this.preferences = recordIn(request.body) ?? this.preferences;

                return answering(this.preferences);
            case 'GET /display-name':
                return answering(this.displayName);
            case 'POST /display-name': {
                const written = recordIn(request.body)?.['displayName'];

                if (typeof written === 'string') {
                    this.displayName = { displayName: written, changeable: true };
                }

                return answering(this.displayName);
            }
            // A portrait is octets, and a stored one would cost a binary fixture for checks that are all about the name
            // beside it, so the person has none.
            case 'GET /portrait':
                return nothing;
            case 'GET /time-zone':
                return answering(this.timeZone);
            case 'POST /time-zone': {
                const written = recordIn(request.body)?.['timeZone'];

                if (typeof written === 'string') {
                    this.timeZone = { timeZone: written, isDefault: false };
                }

                return answering(this.timeZone);
            }
            // Until a check serves the channel, the ticket is refused the way a deployment built before the channel
            // existed refuses it, which every screen already reads as a channel that is down and carries on without.
            case 'POST /signals/ticket':
                return this.signalsServed ? answering(this.ticketMinted()) : { status: 404, body: '' };
            case 'GET /accounts':
                return answering(this.accounts);
            case 'GET /folders':
                return answering(this.folders());
            case 'GET /managed-folders':
                return answering({ ...deployment.managedFolders, folders: this.hierarchy });
            case 'POST /managed-folders':
                return this.folderMade(request.body);
            case 'POST /managed-folders/renames':
                return this.folderRevised(request.body, 'Renamed');
            case 'POST /managed-folders/moves':
                return this.folderRevised(request.body, 'Moved');
            case 'POST /managed-folders/deletions':
                return this.folderDeleted(request.body);
            case 'GET /emails':
                return answering(this.folderPage(request.query));
            case 'GET /emails/search':
                return answering({
                    ...mail.searchResults,
                    results: mail.searchResults.results.filter(({ id }) => !this.deleted.has(id)),
                });
            case 'GET /emails/search/phrasing':
                return answering(mail.readsPhrases);
            case 'POST /emails/search/phrasing':
                return answering(mail.phraseReading);
            case 'POST /citations/resolution':
                return answering(mail.citationResolutions(fragmentsIn(request.body)));
            case 'POST /mutations/flags':
                return answering({ results: this.flagged(request.body) });
            case 'POST /mutations/moves':
                return answering({ results: this.moved(request.body) });
            case 'POST /mutations/deletes':
                return answering({ results: this.deletedNow(request.body) });
            case 'POST /mutations/deletes/withdrawals':
                return answering({ changes: this.overRecords(request.body, 'cancelled') });
            case 'POST /mutations/deletes/releases':
                return answering({ changes: this.overRecords(request.body, 'completed') });
            case 'GET /mutations':
                return answering({ changes: this.recordsNamed(request.query.getAll('record')) });
            case 'POST /drafts':
                return this.draftWritten(this.mintedIdentity(), request.body, 1);
            case 'POST /outbox/cancellation':
                return answering(this.sendWithdrawn(request.body));
            case 'GET /replies/drafting':
                return answering(drafts.draftsReplies);
            case 'POST /replies/drafting':
                return answering(drafts.draftedReply);
            case 'GET /notifications':
                return answering({ notifications: this.notificationsHeld, nextCursor: null });
            case 'GET /notifications/unread-count':
                return answering({ unreadCount: this.unreadNotifications() });
            case 'POST /notifications/read': {
                const markedRead = this.unreadNotifications();

                for (const row of this.notificationsHeld) {
                    row.read = true;
                }

                return answering({ markedRead, unreadCount: 0 });
            }
            case 'POST /notifications/deletions': {
                const named = recordIn(request.body)?.['notificationIds'];
                const ids = new Set(Array.isArray(named) ? named.map(String) : []);
                const kept = this.notificationsHeld.filter(({ id }) => !ids.has(id));
                const deleted = this.notificationsHeld.length - kept.length;

                this.notificationsHeld = kept;

                return answering({ deleted, unreadCount: this.unreadNotifications() });
            }
            case 'GET /tasks':
                return answering({ tasks: this.tasks().committed, nextCursor: null });
            case 'GET /tasks/proposed':
                return answering({ tasks: this.tasks().proposed, nextCursor: null });
            case 'POST /tasks':
                return answering(this.taskWritten(request.body));
            case 'GET /tasks/today/layout':
                return answering(tasks.daysArranged);
            case 'GET /calendar':
                return answering(this.calendarWindow(request.query));
            case 'POST /calendar':
                return answering(this.eventWritten(this.mintedIdentity(), request.body, null));
            case 'GET /calendar/drafts':
                return answering(calendar.calendarDescriptionsRead);
            case 'GET /contacts':
                return answering(contactPage(this.ownBook(), request.query));
            case 'GET /contacts/collected':
                return answering(
                    contactPage(
                        contacts.collectedContactPage.contacts.filter(({ id }) => !this.contactsErased.has(id)),
                        request.query,
                    ),
                );
            case 'POST /contacts':
                return answering(this.contactWritten(request.body));
            case 'POST /discovery/runs':
                this.runs.set(discovery.runId, { reads: 0, startsRunning: this.takeRunning(), stopped: false });

                return { ...answering(discovery.runStarted), status: 202 };
            case 'GET /agent/conversations':
                return answering({ conversations: this.history() });
            default:
                return (
                    this.telemetryAnswer(request) ??
                    this.notificationAnswer(request, segments) ??
                    this.messageAnswer(request, segments) ??
                    this.draftAnswer(request, segments) ??
                    this.discoveryAnswer(request, segments) ??
                    this.agentAnswer(request, segments) ??
                    this.eventAnswer(request, segments) ??
                    this.taskAnswer(request, segments) ??
                    this.contactAnswer(request, segments)
                );
        }
    }

    /** Amending and deleting one event, whether the corpus stated it or a write here did. */
    private eventAnswer({ method, body }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part] = segments;

        if (space !== 'calendar' || segments.length !== 2 || part !== undefined) {
            return null;
        }

        const standing = this.eventsGone.has(id) ? undefined : (this.eventsWritten.get(id) ?? corpusEvent(id));

        if (method === 'PUT') {
            return standing === undefined
                ? { status: 404, body: '' }
                : answering(this.eventWritten(id, body, standing));
        }

        if (method === 'DELETE') {
            this.eventsWritten.delete(id);
            this.eventsGone.add(id);

            return standing === undefined ? { status: 404, body: '' } : nothing;
        }

        return null;
    }

    /** Marking one task done or not done, and erasing it, from whichever half holds it. */
    private taskAnswer({ method, body }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part] = segments;

        if (space !== 'tasks' || segments.length > 3) {
            return null;
        }

        const lists = this.tasks();
        const half = [lists.committed, lists.proposed].find((held) => held.some((task) => task['id'] === id));
        const at = half?.findIndex((task) => task['id'] === id) ?? -1;
        const standing = half?.[at];

        if (method === 'POST' && part === 'completion') {
            if (half === undefined || standing === undefined) {
                return { status: 404, body: '' };
            }

            const completed = { ...standing, completed: recordIn(body)?.['completed'] === true };

            half.splice(at, 1, completed);

            return answering(completed);
        }

        if (method === 'DELETE' && part === undefined) {
            half?.splice(at, 1);

            return answering({ id, erased: standing !== undefined });
        }

        return null;
    }

    /** Erasing one person from whichever book holds them, and what the mail index says about them. */
    private contactAnswer({ method }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part] = segments;

        if (space !== 'contacts' || segments.length > 3) {
            return null;
        }

        const books: Held[] = [...this.ownBook(), ...contacts.collectedContactPage.contacts];
        const standing = books
            .filter((contact) => !this.contactsErased.has(String(contact['id'])))
            .find((contact) => contact['id'] === id);

        if (method === 'DELETE' && part === undefined) {
            this.contactsErased.add(id);

            const addresses = standing?.['addresses'];

            return answering({
                contact: id,
                wasHeld: standing !== undefined,
                addressesErased: Array.isArray(addresses) ? addresses.length : 0,
            });
        }

        if (method === 'GET' && part === 'correspondence') {
            if (standing === undefined) {
                return { status: 404, body: '' };
            }

            return answering(
                id === contacts.contactCorrespondence.contactId
                    ? contacts.contactCorrespondence
                    : { ...contacts.noContactCorrespondence, contactId: id },
            );
        }

        return null;
    }

    /**
     * One window of the calendar: the corpus placed against the span, less what was deleted or amended out of it, and
     * every event a write placed inside the span, earliest first.
     */
    private calendarWindow(asked: URLSearchParams) {
        const from = asked.get('from') ?? '';
        const until = asked.get('until') ?? '';
        const opens = Date.parse(from);
        const closes = Date.parse(until);

        const placed = calendar
            .calendarWindow(from, until)
            .events.map(recordIn)
            .filter((event): event is Held => event !== null)
            .filter(({ id }) => !this.eventsGone.has(String(id)) && !this.eventsWritten.has(String(id)));
        const written = [...this.eventsWritten.values()].filter((event) => {
            const begins = Date.parse(String(event['start']));
            const ends = typeof event['end'] === 'string' ? Date.parse(event['end']) : begins;

            return begins < closes && ends >= opens;
        });

        return {
            events: [...placed, ...written].sort(
                (one, other) => Date.parse(String(one['start'])) - Date.parse(String(other['start'])),
            ),
        };
    }

    /**
     * Holds the event a write states, under the identity it is written at, and answers the record the calendar now has.
     *
     * An amendment restates everything but where the event came from, so that is carried over from what it amends.
     */
    private eventWritten(id: string, body: unknown, amends: Held | null) {
        const stated = recordIn(body) ?? {};
        const start = String(stated['start']);
        const reminders = Array.isArray(stated['reminders']) ? stated['reminders'].map(Number) : [];
        const event = {
            id,
            title: stated['title'],
            start,
            end: stated['end'] ?? null,
            isAllDay: stated['isAllDay'] === true,
            reminders,
            remindsAt: reminders.map((lead) => new Date(Date.parse(start) - lead * 60_000).toISOString()),
            origin: 'Asserted',
            sourceMessage: amends === null ? (stated['sourceMessage'] ?? null) : (amends['sourceMessage'] ?? null),
            recordedAt: amends?.['recordedAt'] ?? recordedAt,
            amendedAt: recordedAt,
        };

        this.eventsWritten.set(id, event);

        return { ...calendar.calendarEventWritten, ...event };
    }

    /** The two halves of the task list, stated by the corpus for the day the deployment's own zone is on. */
    private tasks(): TaskLists {
        if (this.taskLists === null) {
            const day = dayIn(this.timeZone.timeZone);

            this.taskLists = {
                committed: [...tasks.committedTasks(day).tasks],
                proposed: [...tasks.proposedTasks(day).tasks],
            };
        }

        return this.taskLists;
    }

    /**
     * Writes down a task the person committed to, as the last of the committed half.
     *
     * No lead is held, because the client states none on a task it writes down: what announces one is written afterwards,
     * from the row.
     */
    private taskWritten(body: unknown): Held {
        const stated = recordIn(body) ?? {};
        const task = {
            ...tasks.taskWritten,
            id: this.mintedIdentity(),
            title: stated['title'],
            dueOn: stated['dueOn'] ?? null,
            sourceMessageId: stated['sourceMessageId'] ?? null,
        };

        this.tasks().committed.push(task);

        return task;
    }

    /** The people this user wrote down, the corpus's and those written here, in the order the book is walked in. */
    private ownBook(): Held[] {
        const book: Held[] = [...contacts.assertedContactPage.contacts, ...this.contactsWritten];

        return book
            .filter((contact) => !this.contactsErased.has(String(contact['id'])))
            .sort((one, other) => String(one['displayName']).localeCompare(String(other['displayName']), 'en'));
    }

    /** Writes somebody down as a person this user asserted, and answers the write as the book answers one. */
    private contactWritten(body: unknown) {
        const stated = recordIn(body) ?? {};
        const contact = {
            ...contacts.assertedContact,
            id: this.mintedIdentity(),
            displayName: stated['displayName'],
            addresses: stated['addresses'],
            preferredAddress: stated['preferredAddress'],
            note: stated['note'] ?? null,
        };

        this.contactsWritten.push(contact);

        return { ...contacts.contactWritten, contact };
    }

    /**
     * The three signals the client exports while telemetry is on, answered as the service's relay answers them.
     *
     * The relay hands back the collector's own answer, which for an export that was accepted whole is an empty
     * protobuf message — zero octets under the protobuf media type.
     */
    private telemetryAnswer({ method, route }: IssuedRequest): FakeAnswer | null {
        return method === 'POST' && /^\/telemetry\/v1\/(traces|metrics|logs)$/u.test(route)
            ? { status: 200, body: '', contentType: 'application/x-protobuf' }
            : null;
    }

    /** Putting one notification into the read state stated, answered with that state and what it leaves on the bell. */
    private notificationAnswer({ method, body }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part] = segments;

        if (method !== 'POST' || space !== 'notifications' || part !== 'read-state' || segments.length !== 3) {
            return null;
        }

        const row = this.notificationsHeld.find((held) => held.id === id);
        const read = recordIn(body)?.['read'];

        if (row === undefined || typeof read !== 'boolean') {
            return { status: 404, body: '' };
        }

        row.read = read;

        return answering({ id, read, unreadCount: this.unreadNotifications() });
    }

    private unreadNotifications(): number {
        return this.notificationsHeld.filter(({ read }) => !read).length;
    }

    /** One message, its body, a file it carries, and the conversation it belongs to. */
    private messageAnswer({ method, query }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part, position] = segments;

        if (method !== 'GET') {
            return null;
        }

        if (space === 'threads' && segments.length <= 3) {
            return answering(
                part === 'state'
                    ? mail.conversationState
                    : mail.conversation({ content: query.get('content') === 'true' }),
            );
        }

        if (space !== 'messages') {
            return null;
        }

        if (this.deleted.has(id)) {
            return { status: 404, body: '' };
        }

        const filed = this.filedDraft(id);

        if (filed !== undefined) {
            return this.draftMessageAnswer(filed, part, position);
        }

        const markupOnly = id === messages.markupOnlyId;

        if (part === undefined) {
            const read = markupOnly ? messages.markupOnlyMessage : messages.newsletterMessage;
            const flags = this.flags.get(id);

            return answering({
                ...read,
                storedEmailId: id,
                unread: flags?.unread ?? read.unread,
                flagged: flags?.flagged ?? read.flagged,
            });
        }

        if (part === 'body' && position === undefined) {
            const body = markupOnly
                ? messages.markupOnlyBody
                : messages.newsletterBody({
                      remoteImages: query.get('remoteImages') === 'true',
                      fullHtml: query.get('fullHtml') === 'true',
                  });

            return answering({ ...body, storedEmailId: id });
        }

        // The one route answering with octets rather than JSON, which is what lets a check watch a real download.
        if (part === 'attachments' && position !== undefined && segments.length === 4) {
            return { status: 200, body: messages.attachedOctets, contentType: 'text/csv' };
        }

        return null;
    }

    /** Revising, discarding, and sending one draft the client wrote here, and staging files against it. */
    private draftAnswer(
        { method, body, text, query, contentType }: IssuedRequest,
        segments: readonly string[],
    ): FakeAnswer | null {
        const [space, id = '', part, attachmentId] = segments;

        if (space !== 'drafts' || segments.length > 4) {
            return null;
        }

        const held = this.draftsHeld.get(id);

        if (method === 'PUT' && part === undefined) {
            return held === undefined
                ? { status: 404, body: '' }
                : this.draftWritten(id, body, Number(held.record['revision']) + 1);
        }

        if (method === 'DELETE' && part === undefined) {
            return this.draftGone(id) ? nothing : { status: 404, body: '' };
        }

        if (method === 'POST' && part === 'send') {
            if (held === undefined || !this.draftGone(id)) {
                return { status: 404, body: '' };
            }

            const outgoingEmail = this.mintedIdentity();

            this.sendsQueued.set(outgoingEmail, { draftId: id, held });

            return answering({ ...drafts.queuedSend, outgoingEmail });
        }

        if (part !== 'attachments' || held === undefined) {
            return part === 'attachments' ? { status: 404, body: '' } : null;
        }

        if (method === 'POST' && attachmentId === undefined) {
            const file = {
                attachmentId: this.mintedIdentity(),
                fileName: query.get('fileName') ?? '',
                mediaType: contentType ?? 'application/octet-stream',
                octets: text ?? '',
            };

            held.files.push(file);

            return answering(stagedRecord(file));
        }

        if (method === 'DELETE' && attachmentId !== undefined) {
            const at = held.files.findIndex((file) => file.attachmentId === attachmentId);

            if (at === -1) {
                return { status: 404, body: '' };
            }

            held.files.splice(at, 1);

            return nothing;
        }

        return null;
    }

    /**
     * Takes a queued send back, which puts the message where the service leaves one it never transmitted: in the drafts
     * folder, whole, as it stood when it was sent.
     *
     * The service keeps a queued draft's copy in the folder until the message is delivered and the fake delivers at
     * once, so between the two the fake reads the folder as delivery leaves it and a withdrawal puts the copy back —
     * the two readings meet where a check can look, before the send and after the withdrawal.
     */
    private sendWithdrawn(body: unknown): Held {
        const named = recordIn(body)?.['outgoingEmail'];
        const outgoingEmail = typeof named === 'string' ? named : '';
        const queued = this.sendsQueued.get(outgoingEmail);

        if (queued === undefined) {
            return { ...drafts.withdrawnSend, outgoingEmail, outcome: 'RecordUnknown' };
        }

        this.sendsQueued.delete(outgoingEmail);
        this.draftsHeld.set(queued.draftId, queued.held);
        this.count(draftsFolder ?? '', 1, 0);

        return { ...drafts.withdrawnSend, outgoingEmail };
    }

    /** Takes a draft out of the book and its stored message out of the drafts folder, as a send or a give-up does. */
    private draftGone(draftId: string): boolean {
        if (!this.draftsHeld.delete(draftId)) {
            return false;
        }

        this.count(draftsFolder ?? '', -1, 0);

        return true;
    }

    /** The draft whose stored message this is, where one filed here stands in the drafts folder under that identity. */
    private filedDraft(storedEmailId: string): HeldDraft | undefined {
        return [...this.draftsHeld.values()].find((held) => held.storedEmailId === storedEmailId);
    }

    /** The drafts folder as a page lists it: every draft filed here, newest first, as the stored message standing for it. */
    private draftRows(): FolderRow[] {
        return [...this.draftsHeld.values()].reverse().map(({ storedEmailId, record, written, files }) => ({
            ...generatedRow(0),
            id: storedEmailId,
            account: String(record['account']),
            folder: draftsFolder ?? '',
            subject: String(record['subject']),
            senderAddress: author?.senderAddress ?? '',
            senderDisplayName: author?.senderDisplayName ?? '',
            toAddresses: listed(written['to']),
            unread: false,
            flagged: false,
            hasAttachments: files.length > 0,
            attachmentCount: files.length,
            sizeOctets: Number(record['sizeOctets']),
            preview: wordsOf(written),
        }));
    }

    /** The stored message filed for a draft, read the way any message is: what it is, its words, and its files. */
    private draftMessageAnswer(
        { storedEmailId, record, written, files }: HeldDraft,
        part: string | undefined,
        position: string | undefined,
    ): FakeAnswer | null {
        const words = wordsOf(written);

        if (part === undefined) {
            return answering({
                ...messages.markupOnlyMessage,
                storedEmailId,
                account: record['account'],
                folder: draftsFolder,
                sizeOctets: record['sizeOctets'],
                headers: {
                    ...messages.markupOnlyMessage.headers,
                    subject: record['subject'],
                    sentAt: recordedAt,
                    receivedAt: recordedAt,
                    participants: [
                        { role: 'From', address: author?.senderAddress, displayName: author?.senderDisplayName },
                        ...(['to', 'cc', 'bcc'] as const).flatMap((role) =>
                            listed(written[role]).map((address) => ({
                                role: role === 'to' ? 'To' : role === 'cc' ? 'Cc' : 'Bcc',
                                address,
                                displayName: null,
                            })),
                        ),
                    ],
                    messageId: `${storedEmailId}@example.invalid`,
                },
                body: { availability: 'Readable', plainText: true, html: typeof written['htmlBody'] === 'string' },
                attachments: files.map((file, at) => ({
                    position: at,
                    fileName: file.fileName,
                    wasFileNameNormalized: false,
                    mediaType: file.mediaType,
                    sizeOctets: file.octets.length,
                })),
            });
        }

        if (part === 'body' && position === undefined) {
            // No document is stated, so a reader falls back on the words themselves, one paragraph to a line — which
            // is what the composer wrote them as.
            return answering({
                ...messages.markupOnlyBody,
                storedEmailId,
                plainText: { text: words, originalCharacterCount: words.length, truncation: 'None' },
                document: { ...messages.markupOnlyBody.document, blocks: [] },
            });
        }

        const file = part === 'attachments' ? files[Number(position)] : undefined;

        return file === undefined
            ? { status: 404, body: '' }
            : { status: 200, body: file.octets, contentType: file.mediaType };
    }

    /** Following one question put to Discover, and stopping it. */
    private discoveryAnswer({ method, query }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, runs, id = ''] = segments;

        if (space !== 'discovery' || runs !== 'runs' || segments.length !== 3) {
            return null;
        }

        const run = this.runs.get(id);

        if (method === 'DELETE') {
            if (run !== undefined) {
                run.stopped = true;
            }

            return run === undefined ? { status: 404, body: '' } : nothing;
        }

        if (method !== 'GET') {
            return null;
        }

        if (run === undefined) {
            return { status: 404, body: '' };
        }

        const since = Number(query.get('since') ?? '0');
        const running = run.startsRunning && run.reads === 0 && !run.stopped;

        run.reads += 1;

        if (run.stopped) {
            return answering({
                ...discovery.runStopped,
                events: discovery.runStopped.events.filter((event) => sequenceOf(event) > since),
            });
        }

        return answering(running ? discovery.runWorkingTail(since) : discovery.runTail(since));
    }

    /** One conversation with the Agent: reading it, asking in it, steering and stopping it, and the proposals in it. */
    private agentAnswer({ method, query, body }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, conversations, id = '', part, parameter, rest] = segments;

        if (space !== 'agent' || conversations !== 'conversations' || segments.length < 3) {
            return null;
        }

        if (method === 'POST' && part === 'messages' && parameter === undefined) {
            if (!this.conversations.has(id)) {
                const text = recordIn(body)?.['text'];

                this.conversations.set(id, {
                    title: typeof text === 'string' ? text : agent.answeredConversation.title,
                    startedAt: new Date().toISOString(),
                    archived: false,
                    reads: 'answered',
                    asked: { text, scope: recordIn(body)?.['scope'] ?? null },
                    added: [],
                    readsTaken: 0,
                    startsRunning: this.takeRunning(),
                    stopped: false,
                });
            }

            return { ...answering(agent.messagePosted(agent.answeredQuestionId)), status: 202 };
        }

        const conversation = this.conversations.get(id);

        if (conversation === undefined) {
            return { status: 404, body: '' };
        }

        if (method === 'GET' && part === undefined) {
            return answering(this.conversationRead(conversation, Number(query.get('since') ?? '0')));
        }

        if (method === 'POST' && part === 'runs' && rest === 'messages') {
            return { ...answering(agent.messagePosted(agent.composingQuestionId)), status: 202 };
        }

        if (method === 'DELETE' && part === 'runs' && rest === undefined) {
            conversation.stopped = true;
            this.append(conversation, {
                entry: 'answerEnded',
                messageId: conversation.reads === 'composing' ? agent.composingQuestionId : agent.answeredQuestionId,
                outcome: 'Stopped',
                followUps: [],
            });

            return nothing;
        }

        if (part === 'archive' && (method === 'PUT' || method === 'DELETE')) {
            conversation.archived = method === 'PUT';

            return nothing;
        }

        if (method === 'DELETE' && part === undefined) {
            this.conversations.delete(id);

            return nothing;
        }

        if (method === 'PUT' && part === 'proposals' && parameter !== undefined) {
            return this.proposalAnswered(conversation, Number(parameter), recordIn(body)?.['decision']);
        }

        return null;
    }

    /** The history, conversations started here first, then the corpus's own, each with what was done to it since. */
    private history(): readonly Readonly<Record<string, unknown>>[] {
        const corpus = new Map(agent.agentHistory.conversations.map((listed) => [listed.id, listed]));

        return [...this.conversations]
            .map(([id, held]) => ({
                id,
                title: held.title,
                startedAt: held.startedAt,
                lastActivityAt: corpus.get(id)?.lastActivityAt ?? held.startedAt,
                archived: held.archived,
            }))
            .sort((one, other) => Number(corpus.has(one.id)) - Number(corpus.has(other.id)));
    }

    private conversationRead(conversation: Conversation, since: number) {
        const base = conversation.reads === 'composing' ? agent.composingConversation : agent.answeredConversation;
        const running = conversation.startsRunning && conversation.readsTaken === 0;
        const written = this.written(conversation);
        const entries = running ? written.slice(0, composingEntriesShown) : [...written, ...conversation.added];

        conversation.readsTaken += 1;

        return {
            ...base,
            composing: (running || base.composing) && !conversation.stopped,
            entries: entries.filter((entry) => entry.sequence > since),
        };
    }

    /**
     * What the corpus conversation a conversation reads as was written as, before anything was added to it here.
     *
     * One started here opens with the question it was actually asked, under the scope it was asked in, and nothing in
     * its answer has been decided yet — so the corpus's own resolutions stay with the corpus's own conversation, and
     * every proposal in a new one waits on the person who asked.
     */
    private written(
        conversation: Conversation,
    ): readonly { sequence: number; entry: Readonly<Record<string, unknown>> }[] {
        const base = conversation.reads === 'composing' ? agent.composingConversation : agent.answeredConversation;
        const { asked } = conversation;

        if (asked === null) {
            return base.entries;
        }

        return base.entries
            .filter(({ entry }) => entry.entry !== 'resolution')
            .map((held) =>
                held.entry.entry === 'message'
                    ? { ...held, entry: { ...held.entry, text: asked.text, scope: asked.scope } }
                    : held,
            );
    }

    /**
     * What answering one proposal writes: a resolution naming the proposal, and the place it was written at.
     *
     * A proposal already answered is refused as the service refuses it, `409`, and a place that holds no proposal as
     * `404` — the two answers the client reads as the card having moved on under it.
     */
    private proposalAnswered(conversation: Conversation, proposedAt: number, decision: unknown): FakeAnswer {
        const entries = [...this.written(conversation), ...conversation.added];
        const proposal = entries.find((held) => held.sequence === proposedAt && held.entry['entry'] === 'proposal');

        if (proposal === undefined || (decision !== 'accepted' && decision !== 'declined')) {
            return { status: 404, body: '' };
        }

        if (entries.some((held) => held.entry['entry'] === 'resolution' && held.entry['proposedAt'] === proposedAt)) {
            return { status: 409, body: '' };
        }

        const sequence = this.append(conversation, {
            entry: 'resolution',
            proposedAt,
            state: decision === 'accepted' ? 'Accepted' : 'Declined',
        });

        return answering({ sequence });
    }

    /** Adds an entry after everything the conversation holds, and says where it went. */
    private append(conversation: Conversation, entry: Readonly<Record<string, unknown>>): number {
        const sequence =
            Math.max(
                0,
                ...this.written(conversation).map((held) => held.sequence),
                ...conversation.added.map((held) => held.sequence),
            ) + 1;

        conversation.added.push({ sequence, entry });

        return sequence;
    }

    private corpusConversation(id: string, reads: Conversation['reads']): Conversation {
        const listed = agent.agentHistory.conversations.find((held) => held.id === id);

        return {
            title: listed?.title ?? '',
            startedAt: listed?.startedAt ?? '',
            archived: listed?.archived ?? false,
            reads,
            asked: null,
            added: [],
            readsTaken: 0,
            startsRunning: false,
            stopped: false,
        };
    }

    private takeRunning(): boolean {
        const running = this.nextAnswerRunning;

        this.nextAnswerRunning = false;

        return running;
    }

    /**
     * The folders the corpus states that no folder act has deleted, and the ones a folder act made, each at the place
     * the hierarchy puts it and with its counts moved by what was written.
     *
     * A folder made here has never been synchronized and holds nothing yet, which is how the service answers a folder
     * declared a moment ago that no pass has reached.
     */
    private folders() {
        return {
            ...deployment.mailFolders,
            accounts: deployment.mailFolders.accounts.map(({ account, folders }) => ({
                account,
                folders: [
                    ...folders,
                    ...[...this.made].map((alias) => ({
                        alias,
                        role: null,
                        path: [],
                        storedEmailCount: 0,
                        unreadEmailCount: 0,
                        synchronizationState: 'NeverSynchronized',
                        lastSynchronizedAt: null,
                        behind: false,
                    })),
                ]
                    .filter(({ alias }) => this.hierarchy.some(({ id }) => id === alias))
                    .map((folder) => {
                        const moved = this.counted.get(folder.alias) ?? { stored: 0, unread: 0 };

                        return {
                            ...folder,
                            path: this.lineOf(folder.alias).map(({ name }) => name),
                            storedEmailCount: folder.storedEmailCount + moved.stored,

                            // Floored because the inbox's stated count is a dozen and its generated rows hold tens of
                            // thousands unread, so marking a page of them read would otherwise answer a count no
                            // deployment answers.
                            unreadEmailCount: Math.max(folder.unreadEmailCount + moved.unread, 0),
                        };
                    }),
            })),
        };
    }

    /** The folder named and every folder above it, from the top of the hierarchy down, or none for one it does not hold. */
    private lineOf(id: string | null): readonly HeldFolder[] {
        const folder = this.hierarchy.find((held) => held.id === id);

        return folder === undefined ? [] : [...this.lineOf(folder.parentId), folder];
    }

    /**
     * Makes a folder on the mail server beneath the parent named, and declares it under an alias of its own name —
     * with a number after it where another folder already holds that one — which is the service's reading: an alias
     * says nothing about where a folder sits, and nothing afterwards changes it.
     */
    private folderMade(body: unknown): FakeAnswer {
        const asked = recordIn(body);
        const name = asked?.['name'];
        const parentId = typeof asked?.['parentId'] === 'string' ? asked['parentId'] : null;

        if (typeof name !== 'string' || (parentId !== null && this.lineOf(parentId).length === 0)) {
            return folderRefused(typeof name === 'string' ? 'ParentMissing' : 'NameInvalid');
        }

        const taken = (candidate: string) => this.hierarchy.some(({ id }) => id === candidate);
        let alias = name.toUpperCase();

        for (let suffix = 2; taken(alias); suffix += 1) {
            alias = `${name.toUpperCase()}-${String(suffix)}`;
        }

        const folder: HeldFolder = { id: alias, parentId, name, role: null, allowedActs: ['Rename', 'Move', 'Delete'] };

        this.hierarchy = [...this.hierarchy, folder];
        this.made.add(alias);

        return answering({ change: 'Created', folder, mailErasureDeferred: false });
    }

    /** Renames or moves a folder the account declares, which changes where it sits and never the alias it is named by. */
    private folderRevised(body: unknown, change: 'Renamed' | 'Moved'): FakeAnswer {
        const asked = recordIn(body);
        const standing = this.hierarchy.find(({ id }) => id === asked?.['folderId']);

        if (standing === undefined) {
            return folderRefused('FolderMissing');
        }

        const revised: HeldFolder =
            change === 'Renamed'
                ? { ...standing, name: typeof asked?.['name'] === 'string' ? asked['name'] : standing.name }
                : { ...standing, parentId: typeof asked?.['parentId'] === 'string' ? asked['parentId'] : null };

        this.hierarchy = this.hierarchy.map((folder) => (folder === standing ? revised : folder));

        return answering({ change, folder: revised, mailErasureDeferred: false });
    }

    /**
     * Deletes a folder and everything beneath it on the mail server, which takes the mail stored from them out of every
     * read — the change the service reports for an account whose folders a mail server holds, as the corpus's does.
     */
    private folderDeleted(body: unknown): FakeAnswer {
        const folder = this.hierarchy.find(({ id }) => id === recordIn(body)?.['folderId']);

        if (folder === undefined) {
            return folderRefused('FolderMissing');
        }

        const gone = new Set(this.hierarchy.filter(({ id }) => this.lineOf(id).includes(folder)).map(({ id }) => id));

        for (const [storedEmailId, alias] of this.placed) {
            if (gone.has(alias)) {
                this.deleted.add(storedEmailId);
            }
        }

        this.hierarchy = this.hierarchy.filter(({ id }) => !gone.has(id));

        return answering({ change: 'Deleted', folder, mailErasureDeferred: false });
    }

    /**
     * One page of a folder, keyset-paged by position the way the corpus pages it, with every write applied.
     *
     * The inbox is the corpus's generated folder, so it is walked a page of the corpus at a time and whatever left it
     * is skipped; the drafts folder holds the drafts written here, newest first; any other folder holds only what was
     * moved into it, which is few enough to answer whole. Asked with
     * no folder, it is the whole mailbox. The order a check asks for changes nothing, because every corpus row arrived
     * at the same instant and the two orders are therefore the same list.
     */
    private folderPage(asked: URLSearchParams) {
        const folder = asked.get('folder');
        // ponytail: the date and mark filters are not applied; every corpus row shares one instant and most carry no
        // mark, so honour them here when a journey filters on either.
        const matches = (row: FolderRow | null): row is FolderRow =>
            row !== null &&
            (folder === null || row.folder === folder) &&
            wanted(asked.get('unread'), row.unread) &&
            wanted(asked.get('flagged'), row.flagged) &&
            wanted(asked.get('hasAttachments'), row.hasAttachments);

        if (folder !== null && folder === draftsFolder) {
            return {
                emails: this.draftRows().filter(matches),
                nextCursor: null,
                previousCursor: null,
                pageSize: mail.rowsPerPage,
            };
        }

        if (folder !== null && folder !== 'INBOX') {
            const emails = [...this.placed]
                .filter(([, alias]) => alias === folder)
                .map(([id]) => corpusRow(id))
                .filter((row) => row !== null)
                .map((row) => this.shaped(row))
                .filter(matches);

            return { emails, nextCursor: null, previousCursor: null, pageSize: mail.rowsPerPage };
        }

        const cursor = Math.min(Math.max(Number(asked.get('cursor') ?? '0'), 0), mail.mailboxSize);

        return asked.get('direction') === 'backward'
            ? this.pageBefore(cursor, matches)
            : this.pageFrom(cursor, matches);
    }

    private pageFrom(start: number, matches: (row: FolderRow | null) => boolean) {
        // What arrived heads the folder, and only a read from its head lists it: a cursor names a position among the
        // corpus's own rows, so the page after the first goes on from wherever the first stopped reading them.
        const emails: unknown[] = start === 0 ? this.arrived.map((row) => this.shaped(row)).filter(matches) : [];
        let position = start;

        while (emails.length < mail.rowsPerPage && position < mail.mailboxSize) {
            for (const generated of mail.timelinePage(position).emails) {
                position += 1;

                const row = this.shaped(generated);

                if (matches(row)) {
                    emails.push(row);
                }

                if (emails.length === mail.rowsPerPage) {
                    break;
                }
            }
        }

        return {
            emails,
            nextCursor: position >= mail.mailboxSize ? null : String(position),
            previousCursor: start === 0 ? null : String(start),
            pageSize: mail.rowsPerPage,
        };
    }

    private pageBefore(end: number, matches: (row: FolderRow | null) => boolean) {
        const emails: unknown[] = [];
        let position = end;

        while (emails.length < mail.rowsPerPage && position > 0) {
            const from = Math.max(position - mail.rowsPerPage, 0);

            for (const generated of mail
                .timelinePage(from)
                .emails.slice(0, position - from)
                .reverse()) {
                position -= 1;

                const row = this.shaped(generated);

                if (matches(row)) {
                    emails.unshift(row);
                }

                if (emails.length === mail.rowsPerPage) {
                    break;
                }
            }
        }

        return {
            emails,
            nextCursor: end >= mail.mailboxSize ? null : String(end),
            previousCursor: position === 0 ? null : String(position),
            pageSize: mail.rowsPerPage,
        };
    }

    /** A row as it reads now: gone where it was deleted, and in the folder and with the flags it was last given. */
    private shaped(row: FolderRow): FolderRow | null {
        if (this.deleted.has(row.id)) {
            return null;
        }

        const flags = this.flags.get(row.id);

        return {
            ...row,
            folder: this.placed.get(row.id) ?? row.folder,
            unread: flags?.unread ?? row.unread,
            flagged: flags?.flagged ?? row.flagged,
        };
    }

    /** Where a message stands now, or `null` for one the corpus does not hold or a delete has taken. */
    private factsOf(storedEmailId: string): MessageFacts | null {
        const corpus = corpusFacts(storedEmailId);

        if (corpus === null || this.deleted.has(storedEmailId)) {
            return null;
        }

        const flags = this.flags.get(storedEmailId);

        return {
            folder: this.placed.get(storedEmailId) ?? corpus.folder,
            unread: flags?.unread ?? corpus.unread,
            flagged: flags?.flagged ?? corpus.flagged,
        };
    }

    private flagged(body: unknown) {
        return listIn(body, 'changes').map((change) => {
            const storedEmailId = String(change['storedEmailId']);
            const facts = this.factsOf(storedEmailId);
            const flags = change['flags'] as Readonly<Record<string, unknown>> | undefined;

            if (facts === null) {
                return { storedEmailId, outcome: 'message-not-found', detail: null, changes: [] };
            }

            const asked: WrittenFlags = {
                ...(typeof flags?.['seen'] === 'boolean' ? { seen: flags['seen'] } : {}),
                ...(typeof flags?.['flagged'] === 'boolean' ? { flagged: flags['flagged'] } : {}),
            };
            const applied = !this.refusesThisChange();

            if (applied) {
                this.flagsWritten(storedEmailId, asked);
            }

            const state = applied ? 'completed' : 'dead-lettered';
            const written = [
                ...(asked.seen === undefined ? [] : [this.recorded(storedEmailId, 'set-seen', state)]),
                ...(asked.flagged === undefined ? [] : [this.recorded(storedEmailId, 'set-flagged', state)]),
            ];

            return { storedEmailId, outcome: 'recorded', detail: null, changes: written.map(submitted) };
        });
    }

    /** Writes a message's flags as its mailbox now holds them, moving the unread count with the read mark. */
    private flagsWritten(storedEmailId: string, flags: WrittenFlags): MessageFacts {
        const facts = this.factsOf(storedEmailId);

        if (facts === null) {
            throw new Error(`The corpus holds no message ${storedEmailId} to mark.`);
        }

        const held = this.flags.get(storedEmailId) ?? {};

        if (flags.seen !== undefined) {
            this.count(facts.folder, 0, Number(!flags.seen) - Number(facts.unread));
            held.unread = !flags.seen;
        }

        if (flags.flagged !== undefined) {
            held.flagged = flags.flagged;
        }

        this.flags.set(storedEmailId, held);

        return facts;
    }

    /** Whether the change being written is the one a check asked to be refused, which spends that ask. */
    private refusesThisChange(): boolean {
        const refused = this.refusingNextChange;

        this.refusingNextChange = false;

        return refused;
    }

    private moved(body: unknown) {
        const aliases = new Set(this.folders().accounts.flatMap(({ folders }) => folders.map(({ alias }) => alias)));

        return listIn(body, 'moves').map((move) => {
            const storedEmailId = String(move['storedEmailId']);
            const destinationFolder = String(move['destinationFolder']);
            const facts = this.factsOf(storedEmailId);

            if (facts === null) {
                return { storedEmailId, outcome: 'message-not-found', destinationFolder: null, change: null };
            }

            if (!aliases.has(destinationFolder)) {
                return { storedEmailId, outcome: 'destination-not-found', destinationFolder: null, change: null };
            }

            if (facts.folder === destinationFolder) {
                return { storedEmailId, outcome: 'already-in-destination', destinationFolder, change: null };
            }

            if (this.refusesThisChange()) {
                const change = submitted(this.recorded(storedEmailId, 'relocate', 'dead-lettered'));

                return { storedEmailId, outcome: 'recorded', destinationFolder, change };
            }

            this.count(facts.folder, -1, -Number(facts.unread));
            this.count(destinationFolder, 1, Number(facts.unread));
            this.placed.set(storedEmailId, destinationFolder);

            const change = submitted(this.recorded(storedEmailId, 'relocate', 'completed'));

            return { storedEmailId, outcome: 'recorded', destinationFolder, change };
        });
    }

    /**
     * Writes a delete down for each message, which takes it out of every read at once.
     *
     * The record stays pending, because that is the window the way back is offered in: a withdrawal cancels it and
     * puts the message back, and a release — or nothing at all, which is what a real window elapsing would be —
     * completes it.
     */
    private deletedNow(body: unknown) {
        return listIn(body, 'deletes').map((named) => {
            const storedEmailId = String(named['storedEmailId']);
            const facts = this.factsOf(storedEmailId);

            if (facts === null) {
                return { storedEmailId, outcome: 'message-not-found', change: null };
            }

            this.count(facts.folder, -1, -Number(facts.unread));
            this.deleted.add(storedEmailId);

            return {
                storedEmailId,
                outcome: 'recorded',
                change: submitted(this.recorded(storedEmailId, 'delete', 'pending')),
            };
        });
    }

    /** Takes pending deletes back or lets them go, answering each named record as it now stands. */
    private overRecords(body: unknown, becomes: 'cancelled' | 'completed') {
        const named = recordIn(body)?.['recordIds'];
        const recordIds = Array.isArray(named) ? named.map(String) : [];

        for (const record of recordIds.map((recordId) => this.records.get(recordId))) {
            if (record?.mutation !== 'delete' || record.state !== 'pending') {
                continue;
            }

            record.state = becomes;

            if (becomes === 'cancelled') {
                this.deleted.delete(record.storedEmailId);

                const facts = this.factsOf(record.storedEmailId);

                this.count(facts?.folder ?? 'INBOX', 1, Number(facts?.unread ?? false));
            }
        }

        return this.recordsNamed(recordIds);
    }

    private recordsNamed(recordIds: readonly string[]) {
        return recordIds
            .map((recordId) => this.records.get(recordId))
            .filter((record) => record !== undefined)
            .map((record) => ({
                recordId: record.recordId,
                storedEmailId: record.storedEmailId,
                mutation: record.mutation,
                state: record.state,
                outcomeUnknown: false,
                attemptCount: attemptsBehind[record.state],
                lastFailure: null,
                recordedAt,
                stateChangedAt: recordedAt,
            }));
    }

    private recorded(
        storedEmailId: string,
        mutation: MutationRecord['mutation'],
        state: MutationRecord['state'],
    ): MutationRecord {
        const record = { recordId: this.mintedIdentity(), storedEmailId, mutation, state };

        this.records.set(record.recordId, record);

        return record;
    }

    private count(alias: string, stored: number, unread: number): void {
        const moved = this.counted.get(alias) ?? { stored: 0, unread: 0 };

        this.counted.set(alias, { stored: moved.stored + stored, unread: moved.unread + unread });
    }

    /**
     * Keeps a draft as the composition states it, and answers the record the way a save is answered.
     *
     * Each save is a revision of its own, which is what tells a check that a second save reached the deployment
     * rather than being folded into the first.
     */
    private draftWritten(draftId: string, body: unknown, revision: number): FakeAnswer {
        const held = this.draftsHeld.get(draftId);
        const written = recordIn(body) ?? {};
        const answersMail = typeof written['answeredEmailId'] === 'string';
        const recipients = (['to', 'cc', 'bcc'] as const).flatMap((role) =>
            (Array.isArray(written[role]) ? written[role] : []).map((address) => ({
                role: role === 'to' ? 'To' : role === 'cc' ? 'Cc' : 'Bcc',
                address: String(address),
                displayName: null,
            })),
        );
        const draft = {
            ...drafts.draft,
            draftId,
            account: typeof written['account'] === 'string' ? written['account'] : drafts.draft.account,
            subject: answersMail
                ? drafts.answeringDraft.subject
                : typeof written['subject'] === 'string'
                  ? written['subject']
                  : '',
            recipients,
            attachments: held?.files.map(stagedRecord) ?? [],
            revision,
            sizeOctets: typeof written['plainTextBody'] === 'string' ? written['plainTextBody'].length : 0,
        };

        // The first save files the stored message in the drafts folder, and every later one replaces it where it stands.
        if (held === undefined) {
            this.count(draftsFolder ?? '', 1, 0);
        }

        this.draftsHeld.set(draftId, {
            storedEmailId: held?.storedEmailId ?? this.mintedIdentity(),
            record: draft,
            written,
            files: held?.files ?? [],
        });

        return answering({ ...drafts.savedDraft, draft });
    }

    /** A fresh identity in the shape the service mints, numbered so a run is the same run every time. */
    private mintedIdentity(): string {
        this.minted += 1;

        return `0198f4a1-0000-7000-8000-${this.minted.toString(16).padStart(12, '0')}`;
    }
}

/** An answer that says only that it happened. */
const nothing: FakeAnswer = { status: 204, body: '' };

/** A staged file the way a draft record lists it and a stage answers it. */
function stagedRecord({ attachmentId, fileName, mediaType, octets }: StagedFile) {
    return { attachmentId, fileName, mediaType, sizeOctets: octets.length, stagedAt: recordedAt };
}

/** The words a save states, which is nothing where it states none. */
function wordsOf(written: Held): string {
    const words = written['plainTextBody'];

    return typeof words === 'string' ? words : '';
}

/** The addresses one header of a save names, skipping anything that is not one. */
function listed(header: unknown): string[] {
    return Array.isArray(header) ? header.filter((address) => typeof address === 'string') : [];
}

/** A folder act refused the way the service refuses one, as a problem naming the refusal. */
function folderRefused(refusal: 'FolderMissing' | 'ParentMissing' | 'NameInvalid'): FakeAnswer {
    return {
        status: refusal === 'NameInvalid' ? 400 : 404,
        body: JSON.stringify({ refusal }),
        contentType: 'application/problem+json',
    };
}

/** One request as a check reads it back: the route beneath the prefix, its query, and its body. */
function issuedFrom(request: FakeRequest): IssuedRequest {
    const address = new URL(request.url);

    return {
        method: request.method,
        route: address.pathname.slice(clientPrefix.length),
        query: address.searchParams,
        body: parsedBody(request.body),
        text: request.body,
        contentType: request.contentType ?? null,
    };
}

/** One value put on the wire as the deployment behind the preview server would answer with it. */
function answering(answered: unknown): FakeAnswer {
    return { status: 200, body: JSON.stringify(answered), contentType: 'application/json' };
}

/** A record change as a submission answers it, which is before any pass has taken it in hand. */
function submitted(record: MutationRecord) {
    return { mutation: record.mutation, recordId: record.recordId, state: 'pending' };
}

/** Whether a row passes one of the three flag filters, which a request leaves unstated to mean either. */
function wanted(asked: string | null, value: boolean): boolean {
    return asked === null || (asked === 'true') === value;
}

/** Where in the generated folder a message sits, or `null` for one it does not hold. */
function positionOf(storedEmailId: string): number | null {
    if (storedEmailId === generatedRow(conversationRowPosition).id) {
        return conversationRowPosition;
    }

    const position = Number(/^message-(\d+)$/u.exec(storedEmailId)?.[1] ?? Number.NaN);

    return position < mail.mailboxSize && position !== conversationRowPosition ? position : null;
}

function generatedRow(position: number): FolderRow {
    const [row] = mail.timelinePage(position).emails;

    if (row === undefined) {
        throw new Error(`The corpus holds no row at position ${String(position)}.`);
    }

    return row;
}

/**
 * A message the corpus holds anywhere — in the folder, in a thread, or only in a search answer — as a folder page lists
 * it, which is how it reads once a move has filed it somewhere.
 */
function corpusRow(storedEmailId: string): FolderRow | null {
    const position = positionOf(storedEmailId);

    if (position !== null) {
        return generatedRow(position);
    }

    const threaded = mail.conversation().messages.find(({ email }) => email.id === storedEmailId)?.email;

    if (threaded !== undefined) {
        return threaded;
    }

    const found = mail.searchResults.results.find(({ id }) => id === storedEmailId);

    if (found === undefined) {
        return null;
    }

    // A search answer is a folder row with what the search matched added and the derivation left off, so the row is
    // the fields the two share and a derivation of none.
    const listed = {
        id: found.id,
        account: found.account,
        folder: found.folder,
        threadId: found.threadId,
        subject: found.subject,
        receivedAt: found.receivedAt,
        sentAt: found.sentAt,
        senderAddress: found.senderAddress,
        senderDisplayName: found.senderDisplayName,
        toAddresses: found.toAddresses,
        unread: found.unread,
        flagged: found.flagged,
        answered: found.answered,
        hasAttachments: found.hasAttachments,
        attachmentCount: found.attachmentCount,
        sizeOctets: found.sizeOctets,
        preview: found.preview,
        threadMessageCount: found.threadMessageCount,
        enrichment: null,
    };

    return listed;
}

/** Where the corpus puts a message before anything is written to it. */
function corpusFacts(storedEmailId: string): MessageFacts | null {
    const row = corpusRow(storedEmailId);

    return row === null ? null : { folder: row.folder, unread: row.unread, flagged: row.flagged };
}

/**
 * The day it is in a zone, as a calendar day is spelled, which is the day a task list is grouped by.
 *
 * The deployment's zone rather than the machine's, because that is the zone the client reads the list in: a machine
 * standing on a different day from the deployment would otherwise file today's work under another heading.
 */
function dayIn(timeZone: string): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(
        new Date(),
    );
}

/** One event the corpus states, whichever span it is placed in, or `undefined` for an identity it does not hold. */
function corpusEvent(id: string): Held | undefined {
    return (
        calendar
            .calendarWindow('2026-01-05T00:00:00Z', '2026-01-12T00:00:00Z')
            .events.map(recordIn)
            .find((event) => event?.['id'] === id) ?? undefined
    );
}

/**
 * One page of a book as the service's keyset serves it: the people from the cursor on who carry the search in their
 * name or in one of their addresses, as many as were asked for. The cursor is a position in the book's own order rather
 * than in what the search found, so a walk continued under another search goes on from the same place.
 */
function contactPage(book: readonly Held[], asked: URLSearchParams) {
    const size = Number(asked.get('pageSize') ?? String(contacts.contactsPerPage));
    const start = Number(asked.get('cursor') ?? '0');
    const term = (asked.get('search') ?? '').trim().toUpperCase();
    const found = book
        .map((contact, position) => ({ contact, position }))
        .filter(
            ({ contact, position }) =>
                position >= start &&
                [
                    contact['displayName'],
                    ...(Array.isArray(contact['addresses']) ? (contact['addresses'] as unknown[]) : []),
                ].some((text) => typeof text === 'string' && text.toUpperCase().includes(term)),
        );
    const served = found.slice(0, size);
    const last = served.at(-1);

    return {
        contacts: served.map(({ contact }) => contact),
        nextCursor: found.length > size && last !== undefined ? String(last.position + 1) : null,
    };
}

function sequenceOf(event: Readonly<Record<string, unknown>>): number {
    return typeof event['sequence'] === 'number' ? event['sequence'] : 0;
}

function parsedBody(body: string | null): unknown {
    if (body === null || body === '') {
        return null;
    }

    try {
        return JSON.parse(body) as unknown;
    } catch {
        return body;
    }
}

/** What a request body states, where it states an object at all. */
function recordIn(body: unknown): Readonly<Record<string, unknown>> | null {
    return typeof body === 'object' && body !== null && !Array.isArray(body)
        ? (body as Readonly<Record<string, unknown>>)
        : null;
}

/** The objects one named list of a request body holds, skipping anything in it that is not one. */
function listIn(body: unknown, named: string): readonly Readonly<Record<string, unknown>>[] {
    const listed = recordIn(body)?.[named];

    return Array.isArray(listed)
        ? listed.map(recordIn).filter((entry): entry is Readonly<Record<string, unknown>> => entry !== null)
        : [];
}

// The passages a citation request named, in the order it named them, which is what the answer is paired against.
function fragmentsIn(body: unknown): readonly string[] {
    return listIn(body, 'citations')
        .map((citation) => citation['fragment'])
        .filter((fragment): fragment is string => typeof fragment === 'string');
}
