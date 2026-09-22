// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import {
    parseAttachmentEntries,
    parseBlockEvidence,
    parseComposedDraft,
    parseConversationStanding,
    parseDeclaredSource,
    parseEvidenceEntries,
    parseFactTableColumns,
    parseFactTableRows,
    parsePersonEntries,
    parseSuggestedAction,
    parseSynthesizedAnswer,
    parseTimelineEntries,
    type AttachmentEntry,
    type BlockEvidence,
    type ComposedDraft,
    type ConversationStanding,
    type DeclaredSource,
    type EvidenceEntry,
    type FactTableColumn,
    type FactTableRow,
    type PersonEntry,
    type SuggestedAction,
    type SynthesizedAnswer,
    type TimelineEntry,
} from './presentationBlocks';
import type { RunTail } from './runFollowing';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What one Discover run has published so far, read from a cursor. The run is a row the deployment journals rather than
// a connection it holds open, so one route serves the first read and every later one and a client that was not
// listening is given what it missed — which is what lets the canvas draw a finished run with no hub at all.
//
// **The plan is versioned apart from the application.** A deployment and a client are updated separately, so this
// client will meet a plan a newer service wrote: `planSchemaVersion` on the run's start says which revision the plan
// was written against, and `understoodPlanSchemaVersion` below is the revision this client was written against. A
// screen renders every block it can draw and says what it could not, because the only other answer to one unfamiliar
// block would be to discard the whole answer.
//
// **The run also says what it is doing and what it is spending.** Its start names the ceilings one question may reach
// on this deployment and the endpoint that will answer it, each settled lookup says how far the retrieval has got and
// what has been consumed so far, and the ending says how the run stopped — a person stopping it and a spend ceiling
// refusing it among them, neither of which is a fault. All of it is counts, instants, and names an operator chose:
// nothing about the mail travels on any of them.
//
// **Nothing here decides what a block looks like.** A block arrives named by its type and carrying what that type
// holds; what a verdict, a confidence, or a relevance is called, and which of them is drawn as a warning, is the
// screen's. A type this build reads no payload for is named to the reader rather than dropped, which is the same
// forward compatibility one revision further down — and it is why an event kind this client does not know is carried
// as `other` instead of being skipped: a sequence dropped on the floor is a cursor that never advances past it.

/** The route a run is started at, relative to the client prefix. */
export const discoveryRunsRoute = '/discovery/runs';

/** The route one run is read at, relative to the client prefix. */
export function discoveryRunRoute(runId: string): string {
    return `${discoveryRunsRoute}/${encodeURIComponent(runId)}`;
}

/**
 * The revision of the presentation plan this client was written against.
 *
 * A run whose plan states a higher revision is drawn as far as it can be and the reader is told so; a run stating this
 * one or lower carries no plan shape this client was not built for. A block type with no renderer here is named to the
 * reader at either revision, which is how revision 3's event and task proposals reach this build until they are drawn.
 */
export const understoodPlanSchemaVersion = 3;

/** The block catalogue the plan is closed over, as the service spells each type on the wire. */
export const answerBlockTypes = [
    'answer',
    'evidenceList',
    'timeline',
    'factTable',
    'people',
    'threadState',
    'attachmentGallery',
    'draft',
    'suggestedAction',
] as const;

/** One of the block types this contract carries. */
export type AnswerBlockType = (typeof answerBlockTypes)[number];

/**
 * What every block carries whatever its type is.
 *
 * `named` is carried even for a type the catalogue does carry, because it is what a screen names to a reader when this
 * build has nothing to draw it with — and a screen that had to spell a type it did not recognise out of a value it had
 * refused would have nothing to spell.
 */
interface NamedBlock {
    /** What the run called the block, whichever of the two it is. */
    readonly named: string;
}

/**
 * One block of an answer, named by its type and carrying whatever that type holds.
 *
 * It is a union rather than one shape with optional members, so a renderer that has established which type it is
 * holding has the block's own members with nothing to check for absence: what a block of a given type carries is the
 * contract's answer rather than each screen's guess.
 *
 * Every type the catalogue carries is read in full here, so the last member is a type this contract does not carry at
 * all: a run written by a newer service. It arrives named and nothing more, which is the whole of what a reader is
 * owed — and it is the member that keeps one unfamiliar block from discarding the answer around it. A tenth type added
 * to the catalogue and not read here would fail to compile in `parseBlock` rather than arriving as one of these.
 */
export type AnswerBlock =
    | (NamedBlock & {
          readonly type: 'answer';
          readonly evidence: BlockEvidence;
          readonly answer: SynthesizedAnswer;
      })
    | (NamedBlock & {
          readonly type: 'evidenceList';
          readonly evidence: BlockEvidence;
          readonly entries: readonly EvidenceEntry[];
      })
    | (NamedBlock & {
          readonly type: 'timeline';
          readonly evidence: BlockEvidence;
          readonly entries: readonly TimelineEntry[];
      })
    | (NamedBlock & {
          readonly type: 'factTable';
          readonly evidence: BlockEvidence;

          /** The columns compared across, in the order they are drawn, which every row holds one cell per. */
          readonly columns: readonly FactTableColumn[];

          readonly rows: readonly FactTableRow[];
      })
    | (NamedBlock & {
          readonly type: 'people';
          readonly evidence: BlockEvidence;
          readonly entries: readonly PersonEntry[];
      })
    | (NamedBlock & {
          readonly type: 'threadState';
          readonly evidence: BlockEvidence;
          readonly standing: ConversationStanding;
      })
    | (NamedBlock & {
          readonly type: 'attachmentGallery';
          readonly evidence: BlockEvidence;
          readonly entries: readonly AttachmentEntry[];
      })
    | (NamedBlock & {
          readonly type: 'draft';
          readonly evidence: BlockEvidence;
          readonly draft: ComposedDraft;
      })
    | (NamedBlock & {
          readonly type: 'suggestedAction';
          readonly evidence: BlockEvidence;
          readonly suggestion: SuggestedAction;
      })
    | (NamedBlock & {
          /** `null`, the run having named a type this contract does not carry. */
          readonly type: null;
      });

/**
 * What one question may spend on this deployment, which is what every count a run reports is read against.
 *
 * Known before the run has spent anything, and stated rather than predicted: a run is a conversation whose length a
 * model decides, so an estimate would be a guess that teaches people to ignore the true figure beside it.
 */
export interface DiscoveryRunCeilings {
    /** The characters of retrieved mail one run may send.  */
    readonly retrievedCharacters: number;

    /** The provider calls one run may make. */
    readonly providerCalls: number;

    /** The tokens, sent and received together, one run may consume. */
    readonly tokens: number;
}

/**
 * What a run has consumed, in the units the ceilings that will stop it are stated in.
 *
 * Never money: this client holds no price list, a price is a contract between an operator and a provider, and a figure
 * in currency would be a claim about somebody's bill. The token count is a floor rather than a bill — a call abandoned
 * in flight advances it by nothing while the provider may still charge for it.
 */
export interface DiscoveryRunSpend {
    readonly providerCalls: number;
    readonly tokens: number;
    readonly retrievedCharacters: number;

    /** How many distinct messages those characters came from, which is the figure a person has an intuition about. */
    readonly messagesRetrieved: number;
}

/** How far the run's retrieval plan has got, as one of its lookups settles. */
export interface DiscoveryRetrievalProgress {
    readonly lookupsRun: number;
    readonly lookupsRefused: number;
    readonly lookupsPlanned: number;
    readonly passagesFound: number;
}

/**
 * How a run ended without finishing.
 *
 * Three of the nine are not faults at all and are carried apart for that reason: the person stopped it, or one of the
 * two spend ceilings refused it. A ceiling reached is the deployment behaving exactly as its operator configured it,
 * and a screen that rendered one as an error would produce somebody retrying three times something that will not
 * become cheaper.
 */
export type DiscoveryRunEnding =
    | 'unavailable'
    | 'temporarilyUnavailable'
    | 'retrievalRefused'
    | 'timedOut'
    | 'failed'
    | 'stopped'
    | 'cancelled'
    | 'periodSpent'
    | 'runSpent';

// What the service spells each ending as, which is its own member name: this surface configures no naming policy, so a
// closed value goes out Pascal-cased while every property name around it is camel-cased.
//
// An ending this build does not know is read as `failed`, which is what that member already means — the run ended for a
// reason it does not publish and an operator reads it in the deployment's own logs. Refusing the tail instead would
// discard an answer over the one event that says it is finished.
const endings: Readonly<Record<string, DiscoveryRunEnding | undefined>> = {
    Unavailable: 'unavailable',
    TemporarilyUnavailable: 'temporarilyUnavailable',
    RetrievalRefused: 'retrievalRefused',
    TimedOut: 'timedOut',
    Failed: 'failed',
    Stopped: 'stopped',
    Cancelled: 'cancelled',
    PeriodSpent: 'periodSpent',
    RunSpent: 'runSpent',
};

/**
 * One thing a run published, as far as drawing an answer and the chrome around it is concerned.
 *
 * `other` is every kind this client does not act on, which is both the ones the contract already carries and the ones
 * it will gain: what a follower needs of an event it ignores is that it happened and where, so the cursor moves past it.
 */
export type DiscoveryRunEvent =
    | {
          readonly kind: 'started';
          readonly sequence: number;
          readonly planSchemaVersion: number;
          readonly ceilings: DiscoveryRunCeilings;

          /** This deployment's own name for the endpoint answering the run, and empty where it answers no questions. */
          readonly endpointAlias: string;

          /** The model name the operator declared for publication, and empty where they declared none. */
          readonly publishedModel: string;
      }
    | { readonly kind: 'citation'; readonly sequence: number; readonly source: DeclaredSource }
    | { readonly kind: 'block'; readonly sequence: number; readonly block: AnswerBlock }
    | {
          readonly kind: 'retrieval';
          readonly sequence: number;
          readonly progress: DiscoveryRetrievalProgress;
          readonly spend: DiscoveryRunSpend;
      }
    | { readonly kind: 'completed'; readonly sequence: number; readonly spend: DiscoveryRunSpend }
    | {
          readonly kind: 'failed';
          readonly sequence: number;
          readonly ending: DiscoveryRunEnding;
          readonly spend: DiscoveryRunSpend;

          /** When the refused allowance returns, which only a period refusal names. */
          readonly retryAt: string | null;
      }
    | { readonly kind: 'other'; readonly sequence: number };

// The whole of one answer: twenty blocks of prose with their citations. Generous against that and small enough that
// anything which is not a run's tail is refused before it is read.
const longestRunAnswer = 512 * 1024;

// Everything one run may publish, which is what a first read of a finished run answers with: its start, one report per
// lookup its retrieval plan may hold, one citation per source it may declare, one event per block, and its ending.
const mostEventsPerRead = 1 + 6 + 200 + 20 + 1;

// A type name the service assigned. Bounded because it is spelled onto a screen for a type this build cannot draw.
const longestBlockType = 128;

// The endpoint alias and the published model name, both of them names an operator chose and both of them spelled onto
// a screen. Generous against anything anybody would write in a configuration file.
const longestEndpointName = 128;

// A stop answers `204` with no body at all, so anything arriving on it is already more than the contract carries.
const longestStopAnswer = 4 * 1024;

// A start answers one identifier, so the same bound holds: anything larger is not the answer this route publishes.
const longestStartAnswer = 4 * 1024;

/**
 * The question one run is asked, and the mail it may be answered from.
 *
 * Every field the deployment requires is stated rather than optional, because a question asked against *whatever the
 * service defaults to* is a run reading mail the person never chose. What a caller narrows with is the empty list and
 * `null`, which is how the service spells *every account* and *no conversation* — so a scope reaching nothing is
 * expressible and a scope the caller forgot to state is not.
 *
 * It names no user. Whose mail is read comes off the credential, exactly as it does on every other route here.
 */
export interface DiscoveryRunAsk {
    /** What the person wants to know. */
    readonly question: string;

    /** The accounts to read, by identifier or display name, and empty for every account this user holds. */
    readonly accounts: readonly string[];

    /** The folders to read, by alias or as `role:Inbox`, and empty for every folder. */
    readonly folders: readonly string[];

    /** The conversation the question is about, or `null` where it is about none. */
    readonly thread: string | null;

    /** The individual messages the question is about, and empty where it is about none. */
    readonly emails: readonly string[];
}

/**
 * Asks one question of this user's mail, and answers with the run that will answer it.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param asked The question and the mail it may be answered from.
 * @returns The run to follow, or why it was not started. The deployment accepts a question as soon as it knows the
 * question and its scope are answerable, so what comes back is an identifier rather than an answer: everything the run
 * publishes is read at {@link readDiscoveryRunTail}.
 * @remarks
 * Every status but `202` is read by {@link failureReasonForStatus}, so a refusal of the question itself — which the
 * service answers `400` — reaches a screen as `unavailable`, beside a deployment that could not be reached at all. That
 * is the same reading every other route here takes of a `400`, and it is deliberate rather than a gap: what the service
 * says a caller has to change is stated in a body this client does not surface, a question being the most revealing
 * value this surface carries, so there is nothing a distinct reason could tell somebody that *asking again* does not.
 * `unreadable` is the accepted run this client could not name — a `202` whose body carries no identifier a later read
 * could use — and it is reached from nowhere else.
 */
export function startDiscoveryRun(
    session: ClientSession,
    transport: MailFathomTransport,
    asked: DiscoveryRunAsk,
): Promise<ClientResult<string>> {
    return spanned(`POST ${discoveryRunsRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, discoveryRunsRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                question: asked.question,
                accounts: asked.accounts,
                folders: asked.folders,
                thread: asked.thread,
                emails: asked.emails,
            }),
            longestAnswer: longestStartAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 202) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const runId = parseStartedRun(response.body);

        return runId === null ? failed('unreadable', response.status) : read(runId);
    });
}

/** The run the deployment accepted, or nothing where what came back is not an identifier a later read could name. */
function parseStartedRun(body: string): string | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    const runId = record?.['runId'];

    return typeof runId === 'string' && runId.length > 0 && runId.length <= longestRunIdentifier ? runId : null;
}

// An identifier the deployment wrote, which is a UUID. Bounded rather than matched, because what this client does with
// it is put it back in a path — and a shape the route does not match is the deployment's answer to that, not a value to
// be refused on a guess about what a later version of it may look like.
const longestRunIdentifier = 128;

/**
 * Reads what one run has published after a cursor, and whether it is still working.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param runId The run to read, as starting one answered.
 * @param since The last sequence the caller holds, and zero to read the run from its beginning.
 * @returns The tail, or why it did not arrive. A run this user does not hold is `missing` rather than a failure to
 * retry: the deployment answers a run belonging to somebody else exactly as it answers one that never existed, so
 * asking again reaches the same answer for ever.
 */
export function readDiscoveryRunTail(
    session: ClientSession,
    transport: MailFathomTransport,
    runId: string,
    since: number,
): Promise<ClientResult<RunTail<DiscoveryRunEvent>>> {
    return spanned('GET /discovery/runs/{runId}', async () => {
        const path = routeFor(session, discoveryRunRoute(runId));

        const response = await send(transport, {
            method: 'GET',
            path: since > 0 ? `${path}?since=${String(since)}` : path,
            headers: headersFor(session),
            longestAnswer: longestRunAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const tail = parseTail(response.body);

        return tail === null ? failed('unreadable', response.status) : read(tail);
    });
}

/**
 * Stops one run, so it makes no further provider call and abandons the retrieval it is waiting on.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param runId The run to stop.
 * @returns Nothing where the stop was recorded, or why it did not reach the deployment. A run that finished a moment
 * earlier is stopped successfully, because whoever asked could not have known; a run this user does not hold is
 * `missing` rather than a failure to retry.
 * @remarks
 * Stopping is not ending the reading. A client that only stopped reading would leave the run calling the provider and
 * drawing mail for nobody, which costs exactly what not stopping costs — so the control is worth having only because
 * this request reaches the work. What the run had already written stays written, and what it had already spent stays
 * spent: stopping buys the remainder rather than a refund.
 */
export function stopDiscoveryRun(
    session: ClientSession,
    transport: MailFathomTransport,
    runId: string,
): Promise<ClientResult<void>> {
    return spanned('DELETE /discovery/runs/{runId}', async () => {
        const response = await send(transport, {
            method: 'DELETE',
            path: routeFor(session, discoveryRunRoute(runId)),
            headers: headersFor(session),
            longestAnswer: longestStopAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        return response.status === 204
            ? read(undefined)
            : failed(failureReasonForStatus(response.status), response.status);
    });
}

function parseTail(body: string): RunTail<DiscoveryRunEvent> | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null || typeof record['running'] !== 'boolean') {
        return null;
    }

    const events = parseEvents(record['events']);

    return events === null ? null : { running: record['running'], events };
}

function parseEvents(value: unknown): readonly DiscoveryRunEvent[] | null {
    if (!Array.isArray(value) || value.length > mostEventsPerRead) {
        return null;
    }

    const events: DiscoveryRunEvent[] = [];
    for (const written of value) {
        const event = parseEvent(written);
        if (event === null) {
            return null;
        }

        events.push(event);
    }

    return events;
}

function parseEvent(value: unknown): DiscoveryRunEvent | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const sequence = record['sequence'];
    if (typeof sequence !== 'number' || !Number.isSafeInteger(sequence) || sequence < 1) {
        return null;
    }

    switch (record['event']) {
        case 'started': {
            const planSchemaVersion = counted(record['planSchemaVersion']);
            const ceilings = parseCeilings(record['bounds']);
            const endpointAlias = named(record['endpointAlias']);
            const publishedModel = named(record['publishedModel']);

            return planSchemaVersion === null || ceilings === null || endpointAlias === null || publishedModel === null
                ? null
                : { kind: 'started', sequence, planSchemaVersion, ceilings, endpointAlias, publishedModel };
        }

        case 'citation': {
            const source = parseDeclaredSource(record['citation']);

            return source === null ? null : { kind: 'citation', sequence, source };
        }

        case 'block': {
            const block = parseBlock(record['block']);

            return block === null ? null : { kind: 'block', sequence, block };
        }

        case 'retrieval': {
            const progress = parseProgress(record['progress']);
            const spend = parseSpend(record['spend']);

            return progress === null || spend === null ? null : { kind: 'retrieval', sequence, progress, spend };
        }

        case 'completed': {
            const spend = parseSpend(record['spend']);

            return spend === null ? null : { kind: 'completed', sequence, spend };
        }

        case 'failed': {
            const spend = parseSpend(record['spend']);
            const retryAt = record['retryAt'];

            if (spend === null || !(retryAt === null || retryAt === undefined || typeof retryAt === 'string')) {
                return null;
            }

            // An ending this build has no name for is read as the one that already means *ended for a reason it does
            // not publish*, so a deployment that gained a tenth way to stop still ends the run on the screen.
            const failure = record['failure'];
            if (typeof failure !== 'string') {
                return null;
            }

            return { kind: 'failed', sequence, ending: endings[failure] ?? 'failed', spend, retryAt: retryAt ?? null };
        }

        default:
            // A kind this client does not act on, which is every one it does not draw an answer out of — the run's
            // retrieval reports and its ending among them, whether a run has stopped being what the tail itself says —
            // and every kind a later revision of the contract adds. The sequence is what it is read for, so the cursor
            // moves past it rather than asking for it again for ever.
            return typeof record['event'] === 'string' ? { kind: 'other', sequence } : null;
    }
}

function parseCeilings(value: unknown): DiscoveryRunCeilings | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const retrievedCharacters = counted(record['maximumRetrievedCharacters']);
    const providerCalls = counted(record['maximumProviderCalls']);
    const tokens = counted(record['maximumTokens']);

    return retrievedCharacters === null || providerCalls === null || tokens === null
        ? null
        : { retrievedCharacters, providerCalls, tokens };
}

function parseSpend(value: unknown): DiscoveryRunSpend | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const providerCalls = counted(record['providerCalls']);
    const tokens = counted(record['tokens']);
    const retrievedCharacters = counted(record['retrievedCharacters']);
    const messagesRetrieved = counted(record['messagesRetrieved']);

    return providerCalls === null || tokens === null || retrievedCharacters === null || messagesRetrieved === null
        ? null
        : { providerCalls, tokens, retrievedCharacters, messagesRetrieved };
}

function parseProgress(value: unknown): DiscoveryRetrievalProgress | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const lookupsRun = counted(record['lookupsRun']);
    const lookupsRefused = counted(record['lookupsRefused']);
    const lookupsPlanned = counted(record['lookupsPlanned']);
    const passagesFound = counted(record['passagesFound']);

    return lookupsRun === null || lookupsRefused === null || lookupsPlanned === null || passagesFound === null
        ? null
        : { lookupsRun, lookupsRefused, lookupsPlanned, passagesFound };
}

/** A count the run reported, or nothing where what arrived is not one a screen could put against a ceiling. */
function counted(value: unknown): number | null {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

/** A name an operator chose, bounded because it is spelled onto a screen, and empty where they chose none. */
function named(value: unknown): string | null {
    return typeof value === 'string' && value.length <= longestEndpointName ? value : null;
}

/** One block as the presentation catalogue writes it, which the Agent reads out of its conversation too. */
export function parseBlock(value: unknown): AnswerBlock | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const named = record['type'];
    if (typeof named !== 'string' || named.length === 0 || named.length > longestBlockType) {
        return null;
    }

    if (!isBlockType(named)) {
        return { type: null, named };
    }

    // A block whose own payload this client cannot read is refused rather than drawn named: the type is one this build
    // has a renderer for, so drawing it as unknown would tell the reader their client is behind when what happened is
    // that the deployment sent something neither of them can stand behind. A refused tail is read again as itself.
    switch (named) {
        case 'answer': {
            const evidence = parseBlockEvidence(record['evidence']);
            const answer = parseSynthesizedAnswer(record);

            return evidence === null || answer === null ? null : { type: named, named, evidence, answer };
        }

        case 'evidenceList': {
            const evidence = parseBlockEvidence(record['evidence']);
            const entries = parseEvidenceEntries(record['entries']);

            return evidence === null || entries === null ? null : { type: named, named, evidence, entries };
        }

        case 'timeline': {
            const evidence = parseBlockEvidence(record['evidence']);
            const entries = parseTimelineEntries(record['entries']);

            return evidence === null || entries === null ? null : { type: named, named, evidence, entries };
        }

        case 'factTable': {
            const evidence = parseBlockEvidence(record['evidence']);
            const columns = parseFactTableColumns(record['columns']);

            if (evidence === null || columns === null) {
                return null;
            }

            const rows = parseFactTableRows(record['rows'], columns.length);

            return rows === null ? null : { type: named, named, evidence, columns, rows };
        }

        case 'people': {
            const evidence = parseBlockEvidence(record['evidence']);
            const entries = parsePersonEntries(record['entries']);

            return evidence === null || entries === null ? null : { type: named, named, evidence, entries };
        }

        case 'threadState': {
            const evidence = parseBlockEvidence(record['evidence']);
            const standing = parseConversationStanding(record);

            return evidence === null || standing === null ? null : { type: named, named, evidence, standing };
        }

        case 'attachmentGallery': {
            const evidence = parseBlockEvidence(record['evidence']);
            const entries = parseAttachmentEntries(record['entries']);

            return evidence === null || entries === null ? null : { type: named, named, evidence, entries };
        }

        case 'draft': {
            const evidence = parseBlockEvidence(record['evidence']);
            const draft = parseComposedDraft(record);

            return evidence === null || draft === null ? null : { type: named, named, evidence, draft };
        }

        case 'suggestedAction': {
            const evidence = parseBlockEvidence(record['evidence']);
            const suggestion = parseSuggestedAction(record);

            return evidence === null || suggestion === null ? null : { type: named, named, evidence, suggestion };
        }

        default:
            return { type: named, named };
    }
}

function isBlockType(value: string): value is AnswerBlockType {
    return (answerBlockTypes as readonly string[]).includes(value);
}
