// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { BrowserContext, Page } from '@playwright/test';

import * as agent from './fixtures/agent';
import * as calendar from './fixtures/calendar';
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

/** One request the page issued, as the fake reads it. */
export interface FakeRequest {
    readonly method: string;
    readonly url: string;
    readonly body: string | null;
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
}

/** One change the fake wrote down, read back the way the records route publishes one. */
interface MutationRecord {
    readonly recordId: string;
    readonly storedEmailId: string;
    readonly mutation: 'set-seen' | 'set-flagged' | 'relocate' | 'delete';
    state: 'pending' | 'completed' | 'cancelled';
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

/** Where a conversation that is still composing stops, which is the question, its start, and one status line. */
const composingEntriesShown = 3;

/** When every record the fake writes down was written, which no check reads and the record route requires. */
const recordedAt = '2026-08-31T09:41:00+00:00';

/** What the fake reads of a row a folder page lists; every other field travels as the corpus states it. */
interface FolderRow {
    readonly id: string;
    readonly folder: string;
    readonly unread: boolean;
    readonly flagged: boolean;
    readonly hasAttachments: boolean;
}

/** Where in the generated folder the one row standing for a correspondence sits, read off the corpus's first page. */
const conversationRowPosition = mail.timelinePage(0).emails.findIndex(({ threadId }) => threadId !== null);

export class FakeDeployment {
    /** Every request that reached the fake, in the order the page issued them. */
    readonly issued: IssuedRequest[] = [];

    /** Every request the fake has no answer for, as `METHOD /route`, which the harness fails the check on. */
    readonly unimplemented: string[] = [];

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

    private readonly draftsHeld = new Map<string, typeof drafts.draft>();
    private readonly runs = new Map<string, DiscoveryRun>();
    private readonly conversations = new Map<string, Conversation>([
        [agent.answeredConversationId, this.corpusConversation(agent.answeredConversationId, 'answered')],
        [agent.composingConversationId, this.corpusConversation(agent.composingConversationId, 'composing')],
        ...agent.agentHistory.conversations
            .filter(({ archived }) => archived)
            .map(({ id }) => [id, this.corpusConversation(id, 'answered')] as const),
    ]);

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
            const answer = this.answer({ method: request.method(), url: request.url(), body: request.postData() });

            await route.fulfill({
                status: answer.status,
                body: answer.body,
                ...(answer.contentType === undefined ? {} : { contentType: answer.contentType }),
            });
        });
    }

    /** The requests the page issued with this method to this route, in the order it issued them. */
    requests(method: string, route: string): IssuedRequest[] {
        return this.issued.filter((issued) => issued.method === method && issued.route === route);
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

        const issued: IssuedRequest = {
            method: request.method,
            route: address.pathname.slice(clientPrefix.length),
            query: address.searchParams,
            body: parsedBody(request.body),
        };

        this.issued.push(issued);

        const answer = this.answerFor(issued);

        if (answer === null) {
            this.unimplemented.push(`${issued.method} ${issued.route}`);

            return { status: 501, body: '' };
        }

        return answer;
    }

    private answerFor(request: IssuedRequest): FakeAnswer | null {
        const { method, route } = request;
        const segments = route.split('/').slice(1).map(decodeURIComponent);

        switch (`${method} ${route}`) {
            case 'GET /sign-in-methods':
                return answering(deployment.signInMethods);
            case 'POST /session/token':
                return answering(deployment.mintedSession);
            case 'POST /session/token/revocation':
                return nothing;
            case 'GET /session':
                return answering({ ...deployment.sessionAnswer, version: this.version });
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
            // A deployment built before the signal channel existed refuses the ticket, which every screen already
            // reads as a channel that is down and carries on without.
            case 'POST /signals/ticket':
                return { status: 404, body: '' };
            case 'GET /accounts':
                return answering(deployment.mailAccounts);
            case 'GET /folders':
                return answering(this.folders());
            case 'GET /managed-folders':
                return answering(deployment.managedFolders);
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
            case 'GET /replies/drafting':
                return answering(drafts.draftsReplies);
            case 'POST /replies/drafting':
                return answering(drafts.draftedReply);
            case 'GET /notifications':
                return answering(notifications.notificationPage);
            case 'GET /notifications/unread-count':
                return answering(notifications.unreadNotificationCount);
            case 'GET /tasks':
                return answering(tasks.committedTasks(dayHere()));
            case 'GET /tasks/proposed':
                return answering(tasks.proposedTasks(dayHere()));
            case 'GET /tasks/today/layout':
                return answering(tasks.daysArranged);
            case 'GET /calendar':
                return answering(
                    calendar.calendarWindow(request.query.get('from') ?? '', request.query.get('until') ?? ''),
                );
            case 'GET /calendar/drafts':
                return answering(calendar.calendarDescriptionsRead);
            case 'POST /discovery/runs':
                this.runs.set(discovery.runId, { reads: 0, startsRunning: this.takeRunning(), stopped: false });

                return { ...answering(discovery.runStarted), status: 202 };
            case 'GET /agent/conversations':
                return answering({ conversations: this.history() });
            default:
                return (
                    this.telemetryAnswer(request) ??
                    this.messageAnswer(request, segments) ??
                    this.draftAnswer(request, segments) ??
                    this.discoveryAnswer(request, segments) ??
                    this.agentAnswer(request, segments)
                );
        }
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

    /** One message, its body, a file it carries, and the conversation it belongs to. */
    private messageAnswer({ method, query }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part, position] = segments;

        if (method !== 'GET') {
            return null;
        }

        if (space === 'threads' && segments.length <= 3) {
            return answering(part === 'state' ? mail.conversationState : mail.conversation);
        }

        if (space !== 'messages') {
            return null;
        }

        if (this.deleted.has(id)) {
            return { status: 404, body: '' };
        }

        const markupOnly = id === messages.markupOnlyId;

        if (part === undefined) {
            return answering({
                ...(markupOnly ? messages.markupOnlyMessage : messages.newsletterMessage),
                storedEmailId: id,
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

    /** Revising, discarding, and sending one draft the client wrote here. */
    private draftAnswer({ method, body }: IssuedRequest, segments: readonly string[]): FakeAnswer | null {
        const [space, id = '', part] = segments;

        if (space !== 'drafts' || segments.length > 3) {
            return null;
        }

        const held = this.draftsHeld.get(id);

        if (method === 'PUT' && part === undefined) {
            return held === undefined ? { status: 404, body: '' } : this.draftWritten(id, body, held.revision + 1);
        }

        if (method === 'DELETE' && part === undefined) {
            return this.draftsHeld.delete(id) ? nothing : { status: 404, body: '' };
        }

        if (method === 'POST' && part === 'send') {
            return this.draftsHeld.delete(id) ? answering(drafts.queuedSend) : { status: 404, body: '' };
        }

        return null;
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

    /** The folders the corpus states, with each count moved by what was written since. */
    private folders() {
        return {
            ...deployment.mailFolders,
            accounts: deployment.mailFolders.accounts.map(({ account, folders }) => ({
                account,
                folders: folders.map((folder) => {
                    const moved = this.counted.get(folder.alias) ?? { stored: 0, unread: 0 };

                    return {
                        ...folder,
                        storedEmailCount: folder.storedEmailCount + moved.stored,
                        unreadEmailCount: folder.unreadEmailCount + moved.unread,
                    };
                }),
            })),
        };
    }

    /**
     * One page of a folder, keyset-paged by position the way the corpus pages it, with every write applied.
     *
     * The inbox is the corpus's generated folder, so it is walked a page of the corpus at a time and whatever left it
     * is skipped; any other folder holds only what was moved into it, which is few enough to answer whole. Asked with
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
        const emails: unknown[] = [];
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

            const written: MutationRecord[] = [];
            const held = this.flags.get(storedEmailId) ?? {};

            if (typeof flags?.['seen'] === 'boolean') {
                this.count(facts.folder, 0, Number(!flags['seen']) - Number(facts.unread));
                held.unread = !flags['seen'];
                written.push(this.recorded(storedEmailId, 'set-seen', 'completed'));
            }

            if (typeof flags?.['flagged'] === 'boolean') {
                held.flagged = flags['flagged'];
                written.push(this.recorded(storedEmailId, 'set-flagged', 'completed'));
            }

            this.flags.set(storedEmailId, held);

            return { storedEmailId, outcome: 'recorded', detail: null, changes: written.map(submitted) };
        });
    }

    private moved(body: unknown) {
        const aliases = new Set(
            deployment.mailFolders.accounts.flatMap(({ folders }) => folders.map(({ alias }) => alias)),
        );

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
                attemptCount: record.state === 'completed' ? 1 : 0,
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
            attachments: [],
            revision,
            sizeOctets: typeof written['plainTextBody'] === 'string' ? written['plainTextBody'].length : 0,
        };

        this.draftsHeld.set(draftId, draft);

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

    const threaded = mail.conversation.messages.find(({ email }) => email.id === storedEmailId)?.email;

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

/** The day the machine running this is on, as a calendar day is spelled, which is the day a task list is grouped by. */
function dayHere(): string {
    const now = new Date();

    return `${String(now.getFullYear())}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
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
