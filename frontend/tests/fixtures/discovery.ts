// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { markupOnlyId, newsletterId } from './messages';

// One question answered, as the run journalling it publishes: the run's start, the sources it declared, how far its
// retrieval got, the blocks of its plan, and its ending. It is what the Discover screen is looked at with, and
// `frontend/tests/AGENTS.md` § *The corpus* holds what may go in it.
//
// **The sources cite messages this corpus already holds**, so following a citation reaches the reading pane rather than
// a message nothing can read. Two of the three are passages and the third is a whole message, which are the two source
// kinds the inspector draws differently.
//
// **The states past the resting one are each stated rather than derived.** A run that is still working, one a person
// stopped, one that failed, one that composed nothing, one carrying a block type no client knows, and one whose plan is
// a revision ahead of this client are six screens somebody has to be able to draw and none of them can be reached from
// a finished run by pressing anything.
//
// It states no plan name. The design project draws the plan the service chose as a chip beside the run, and the tail
// carries no such field: which plan answered a question is not published on this surface, so a value here would be a
// field the deployment does not have.

/** The run every tail below belongs to, which is what starting one answers with. */
export const runId = '0198f4a1-0000-7000-8000-00000000d15c';

/** What the deployment answers a started run with. */
export const runStarted = { runId };

/** The first passage a block rests on, which is a fragment of a message this corpus holds. */
export const citedFragmentId = '0198f4a1-0000-7000-8000-0000000000f1';

const started = {
    event: 'started',
    sequence: 1,
    planSchemaVersion: 2,
    bounds: { maximumRetrievedCharacters: 20000, maximumProviderCalls: 8, maximumTokens: 80000 },
    endpointAlias: 'house',
    publishedModel: 'house-large',
};

const spent = { spend: { providerCalls: 3, tokens: 12400, retrievedCharacters: 8800, messagesRetrieved: 6 } };

const current = { staleness: 'Current', observedAt: '2026-08-31T09:42:00+00:00' };

// Every source the blocks below name, declared before anything names one — which is the order the run publishes them in
// and what lets a block be drawn the moment it arrives.
const citations = [
    {
        event: 'citation',
        sequence: 2,
        citation: {
            id: 'c-1',
            target: { kind: 'fragment', email: newsletterId, fragment: citedFragmentId },
            label: 'A newsletter from Example',
            medium: 'Written',
        },
    },
    {
        event: 'citation',
        sequence: 3,
        citation: {
            id: 'c-2',
            target: { kind: 'fragment', email: markupOnlyId, fragment: '0198f4a1-0000-7000-8000-0000000000f2' },
            label: 'Your order is on its way',
            medium: 'Written',
        },
    },
    {
        event: 'citation',
        sequence: 4,
        citation: {
            id: 'c-3',
            target: { kind: 'email', email: newsletterId },
            label: 'Bay reservation — confirmation',
            medium: 'Written',
        },
    },
];

const retrieval = {
    event: 'retrieval',
    sequence: 5,
    progress: { lookupsRun: 4, lookupsRefused: 0, lookupsPlanned: 4, passagesFound: 27 },
    ...spent,
};

const answerBlock = {
    event: 'block',
    sequence: 6,
    block: {
        type: 'answer',
        evidence: { support: 'Supported', citations: ['c-1', 'c-2'], freshness: current },
        text: 'The bays were confirmed at four, and the confirmation names the end of the week as the day they are held to.',
        confidence: 'High',
    },
};

const evidenceListBlock = {
    event: 'block',
    sequence: 7,
    block: {
        type: 'evidenceList',
        evidence: { support: 'Supported', citations: ['c-1', 'c-2'], freshness: current },
        entries: [
            {
                source: 'c-1',
                fragment: 'Please confirm the bays you want before the end of the week.',
                relevance: 0.94,
                freshness: current,
            },
            {
                source: 'c-2',
                fragment: 'Four bays are held under the reference above.',
                relevance: 0.71,
                freshness: current,
            },
        ],
    },
};

const timelineBlock = {
    event: 'block',
    sequence: 8,
    block: {
        type: 'timeline',
        evidence: { support: 'Supported', citations: ['c-1', 'c-3'], freshness: current },
        entries: [
            {
                occurredAt: '2026-08-24T09:00:00+00:00',
                summary: 'Four bays asked for, with the reference the reply names',
                subject: 'A newsletter from Example',
                sources: ['c-1'],
            },
            {
                occurredAt: '2026-08-31T09:41:00+00:00',
                summary: 'The bays were confirmed and held to the end of the week',
                subject: 'Bay reservation — confirmation',
                sources: ['c-3'],
            },
        ],
    },
};

const factTableBlock = {
    event: 'block',
    sequence: 9,
    block: {
        type: 'factTable',
        evidence: {
            support: 'Conflicting',
            citations: ['c-1', 'c-2'],
            freshness: current,
            conflictingClaims: [
                { statement: 'The bays are held to Friday', sources: ['c-1'] },
                { statement: 'The bays are held to the end of the month', sources: ['c-2'] },
            ],
        },
        columns: ['version', 'amount', 'date'],
        rows: [
            {
                cells: [
                    { value: 'Asked for', sources: ['c-1'] },
                    { value: '4 bays', sources: ['c-1'] },
                    { value: '24 August', sources: ['c-1'] },
                ],
            },
            {
                cells: [
                    { value: 'Confirmed', sources: ['c-2'] },
                    { value: '4 bays', sources: ['c-2'] },
                    /* A cell the correspondence says nothing about, which is drawn as holding no value rather than as
                       an empty string — and cites nothing, because citing an absence is refused. */
                    { value: null, sources: [] },
                ],
            },
        ],
    },
};

const suggestedActionBlock = {
    event: 'block',
    sequence: 10,
    block: {
        type: 'suggestedAction',
        evidence: { support: 'Supported', citations: ['c-3'], freshness: current },
        action: 'ReplyToThread',
        reason: 'The confirmation asks for an answer before the end of the week and none has been sent.',
        impact: 'SendsMail',
        requiresConfirmation: true,
    },
};

const completed = { event: 'completed', sequence: 11, ...spent };

// The whole of what one run published, in the order it published it.
const runPublished: readonly Readonly<Record<string, unknown>>[] = [
    started,
    ...citations,
    retrieval,
    answerBlock,
    evidenceListBlock,
    timelineBlock,
    factTableBlock,
    suggestedActionBlock,
    completed,
];

/**
 * The run as far as it has been published past a cursor, which is what a finished run answers a first read with.
 *
 * Stated as a function of the cursor because that is what the route takes: a client holding the first three events asks
 * for what came after them, and a corpus answering the whole run again would be answering a question it was not asked.
 *
 * @param since The last sequence the caller holds, and zero where it holds none.
 */
export function runTail(since: number) {
    return { running: false, events: runPublished.filter((event) => sequenceOf(event) > since) };
}

/**
 * A run still working, which is the screen the progress line and the control that stops it are drawn on.
 *
 * It stops after the answer, so the run is one block into a plan carrying five: what a reader meets is a block drawn,
 * the place held for what is still coming, and a run that can still be stopped.
 */
export function runWorkingTail(since: number) {
    return {
        running: true,
        events: runPublished.filter((event) => sequenceOf(event) > since).filter((event) => sequenceOf(event) <= 6),
    };
}

/** A run a person stopped, which ends every bit as legitimately as one that finished. */
export const runStopped = {
    running: false,
    events: [
        started,
        ...citations,
        retrieval,
        answerBlock,
        { event: 'failed', sequence: 7, failure: 'Cancelled', ...spent },
    ],
};

/** A run that ended for a reason it does not publish, which is the one ending that reads as a fault. */
export const runFailed = {
    running: false,
    events: [started, retrieval, { event: 'failed', sequence: 6, failure: 'Failed', ...spent }],
};

/** A run whose allowance for the period was spent, which names when questions may be asked again. */
export const runPeriodSpent = {
    running: false,
    events: [
        started,
        { event: 'failed', sequence: 2, failure: 'PeriodSpent', retryAt: '2026-09-01T00:00:00+00:00', ...spent },
    ],
};

/** A run that finished having composed nothing, which is still an answer somebody waited for. */
export const runComposedNothing = { running: false, events: [started, retrieval, completed] };

/**
 * A run carrying a block type no client knows, which is what a deployment updated first publishes.
 *
 * The answer stands beside it, because what the screen has to say is that the rest of the result rendered: a tail
 * holding the unknown block alone could not show that.
 *
 * It carries its type and nothing else. A member of its own would be a field no contract records, which is exactly what
 * `scripts/test-agent-workflow.sh` refuses — and it would prove nothing either, since what this client reads of a block
 * it does not know is the name to tell a reader.
 */
export const runWithAnUnknownBlock = {
    running: false,
    events: [
        started,
        ...citations,
        answerBlock,
        { event: 'block', sequence: 7, block: { type: 'riskScore' } },
        { event: 'completed', sequence: 8, ...spent },
    ],
};

/** A run whose plan is a revision ahead of this client, which is drawn as far as it goes rather than refused. */
export const runAheadOfTheClient = {
    running: false,
    events: [
        { ...started, planSchemaVersion: 9 },
        ...citations,
        answerBlock,
        { event: 'completed', sequence: 7, ...spent },
    ],
};

function sequenceOf(event: Readonly<Record<string, unknown>>): number {
    return typeof event['sequence'] === 'number' ? event['sequence'] : 0;
}
