// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { discoveryRunRoute, readDiscoveryRunTail, startDiscoveryRun, stopDiscoveryRun } from './discoveryRun';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const runId = '6f1b0a8c-2d3e-4f50-9a1b-7c8d9e0f1a2b';

function bodyOf(events: readonly unknown[], running = false): string {
    return JSON.stringify({ running, events });
}

// The three payloads the service writes, in its own spelling: camel-cased property names, and a closed value spelled
// as the member itself because this surface configures no naming policy for one.
const startedEvent = {
    event: 'started',
    sequence: 1,
    planSchemaVersion: 3,
    bounds: { maximumRetrievedCharacters: 20000, maximumProviderCalls: 8, maximumTokens: 80000 },
    endpointAlias: 'house',
    publishedModel: 'gpt-4o',
};

const spentEvent = { spend: { providerCalls: 3, tokens: 1200, retrievedCharacters: 900, messagesRetrieved: 4 } };

const progressed = { lookupsRun: 2, lookupsRefused: 1, lookupsPlanned: 5, passagesFound: 41 };

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

describe('discoveryRunRoute', () => {
    it('names the run in the path', () => {
        expect(discoveryRunRoute(runId)).toBe(`/discovery/runs/${runId}`);
    });

    it('escapes an identifier that is not the shape the route matches', () => {
        expect(discoveryRunRoute('../runs')).toBe('/discovery/runs/..%2Fruns');
    });
});

describe('readDiscoveryRunTail', () => {
    it('reads the run from its beginning without naming a cursor', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readDiscoveryRunTail(session, transport, runId, 0);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/discovery/runs/${runId}`);
    });

    it('carries the cursor it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyOf([]) });

        await readDiscoveryRunTail(session, transport, runId, 7);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/discovery/runs/${runId}?since=7`);
    });

    it('reads whether the run is still working', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([], true) }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'read', value: { running: true, events: [] } });
    });

    it('reads the plan revision, the ceilings, and what answers the run off the run that started', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([startedEvent]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [
                    {
                        kind: 'started',
                        sequence: 1,
                        planSchemaVersion: 3,
                        ceilings: { retrievedCharacters: 20000, providerCalls: 8, tokens: 80000 },
                        endpointAlias: 'house',
                        publishedModel: 'gpt-4o',
                    },
                ],
            },
        });
    });

    it('reads a deployment that declared no model for publication as naming the alias alone', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ ...startedEvent, publishedModel: '' }]) }),
            runId,
            0,
        );

        expect(answered.outcome === 'read' && answered.value.events[0]).toMatchObject({ publishedModel: '' });
    });

    it('reads how far retrieval got and what the run had spent reaching there', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'retrieval',
                        sequence: 2,
                        progress: { lookupsRun: 2, lookupsRefused: 1, lookupsPlanned: 5, passagesFound: 41 },
                        spend: spentEvent.spend,
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [
                    {
                        kind: 'retrieval',
                        sequence: 2,
                        progress: { lookupsRun: 2, lookupsRefused: 1, lookupsPlanned: 5, passagesFound: 41 },
                        spend: spentEvent.spend,
                    },
                ],
            },
        });
    });

    it('reads what a finished run finally consumed', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'completed', sequence: 9, ...spentEvent }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: { running: false, events: [{ kind: 'completed', sequence: 9, spend: spentEvent.spend }] },
        });
    });

    it.each([
        ['Cancelled', 'cancelled'],
        ['PeriodSpent', 'periodSpent'],
        ['RunSpent', 'runSpent'],
        ['TimedOut', 'timedOut'],
        ['Stopped', 'stopped'],
        ['Unavailable', 'unavailable'],
        ['TemporarilyUnavailable', 'temporarilyUnavailable'],
        ['RetrievalRefused', 'retrievalRefused'],
        ['Failed', 'failed'],
    ])('reads the ending the service spells %s', async (spelled, ending) => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([{ event: 'failed', sequence: 3, failure: spelled, ...spentEvent }]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [{ kind: 'failed', sequence: 3, ending, spend: spentEvent.spend, retryAt: null }],
            },
        });
    });

    it('carries when a refused period turns over', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'failed',
                        sequence: 3,
                        failure: 'PeriodSpent',
                        retryAt: '2026-09-21T13:00:00+00:00',
                        ...spentEvent,
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered.outcome === 'read' && answered.value.events[0]).toMatchObject({
            retryAt: '2026-09-21T13:00:00+00:00',
        });
    });

    it('reads an ending this build has no name for as one the run does not publish', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([{ event: 'failed', sequence: 3, failure: 'ProviderRefused', ...spentEvent }]),
            }),
            runId,
            0,
        );

        expect(answered.outcome === 'read' && answered.value.events[0]).toMatchObject({ ending: 'failed' });
    });

    it('reads a block the catalogue carries under the type it names', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 4,
                        block: {
                            type: 'people',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            entries: [
                                {
                                    displayName: 'Anna Kowalska',
                                    address: 'anna@contoso.example',
                                    relationship: 'Contoso · waiting for a reply',
                                    lastContactAt: '2026-09-20T08:47:00+00:00',
                                    sources: ['c-1'],
                                },
                            ],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;
        const block = event?.kind === 'block' && event.block.type === 'people' ? event.block : null;

        // The address is the bare text the plan publishes rather than a record around it, which is what
        // `EmailAddressJsonConverter` writes — so this reads one block of the shape the service actually serves.
        expect(block?.entries[0]?.person).toEqual({
            displayName: 'Anna Kowalska',
            address: 'anna@contoso.example',
        });
    });

    it('keeps the name of a block type the catalogue does not carry rather than refusing the run', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'block', sequence: 2, block: { type: 'RiskScore' } }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [{ kind: 'block', sequence: 2, block: { type: null, named: 'RiskScore' } }],
            },
        });
    });

    it('reads a source the run declared, so a block naming it can be drawn', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'citation',
                        sequence: 2,
                        citation: {
                            id: 'c-1',
                            target: {
                                kind: 'fragment',
                                email: '0198f4a1-0000-7000-8000-000000000001',
                                fragment: 'p-3',
                            },
                            label: 'Master agreement.pdf',
                            medium: 'Written',
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [
                    {
                        kind: 'citation',
                        sequence: 2,
                        source: {
                            id: 'c-1',
                            target: {
                                kind: 'fragment',
                                email: '0198f4a1-0000-7000-8000-000000000001',
                                fragment: 'p-3',
                            },
                            label: 'Master agreement.pdf',
                            medium: 'Written',
                            unreadable: null,
                        },
                    },
                ],
            },
        });
    });

    it('reads an answer with what it rests on and how far it is worth trusting', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 5,
                        block: {
                            type: 'answer',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            text: 'The rate was agreed in April.',
                            confidence: 'High',
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: {
                running: false,
                events: [
                    {
                        kind: 'block',
                        sequence: 5,
                        block: {
                            type: 'answer',
                            named: 'answer',
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                                conflictingClaims: [],
                            },
                            answer: { text: 'The rate was agreed in April.', confidence: 'High' },
                        },
                    },
                ],
            },
        });
    });

    it('reads each message an evidence list presents with the part of it worth reading', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 6,
                        block: {
                            type: 'evidenceList',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            entries: [
                                {
                                    source: 'c-1',
                                    fragment: 'Monthly remuneration is EUR 1,200 net',
                                    relevance: 0.94,
                                    freshness: { staleness: 'Stale', observedAt: '2026-09-01T08:00:00+00:00' },
                                },
                            ],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;

        expect(event?.kind === 'block' && event.block.type === 'evidenceList' ? event.block.entries : null).toEqual([
            {
                source: 'c-1',
                fragment: 'Monthly remuneration is EUR 1,200 net',
                relevance: 0.94,
                freshness: { staleness: 'Stale', observedAt: '2026-09-01T08:00:00+00:00' },
            },
        ]);
    });

    it('reads each event a timeline presents, in the order the run composed them', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 7,
                        block: {
                            type: 'timeline',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            entries: [
                                {
                                    occurredAt: '2021-04-12T00:00:00+00:00',
                                    summary: 'Monthly remuneration set at EUR 1,200',
                                    subject: 'Master agreement',
                                    sources: ['c-1'],
                                },
                            ],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;

        expect(event?.kind === 'block' && event.block.type === 'timeline' ? event.block.entries : null).toEqual([
            {
                occurredAt: '2021-04-12T00:00:00+00:00',
                summary: 'Monthly remuneration set at EUR 1,200',
                subject: 'Master agreement',
                sources: ['c-1'],
            },
        ]);
    });

    it('reads the columns a fact table compares across beside the rows that fill them', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 8,
                        block: {
                            type: 'factTable',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            columns: ['version', 'amount'],
                            rows: [
                                {
                                    cells: [
                                        { value: 'Agreement 2021', sources: ['c-1'] },
                                        { value: 'EUR 1,200', sources: ['c-1'] },
                                    ],
                                },
                            ],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;
        const table = event?.kind === 'block' && event.block.type === 'factTable' ? event.block : null;

        expect(table?.columns).toEqual(['version', 'amount']);
        expect(table?.rows[0]?.cells[1]).toEqual({ value: 'EUR 1,200', sources: ['c-1'] });
    });

    it('refuses a fact table whose row disagrees with its header, which is a comparison nobody can trust', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 9,
                        block: {
                            type: 'factTable',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            columns: ['version', 'amount'],
                            rows: [{ cells: [{ value: 'Agreement 2021', sources: ['c-1'] }] }],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('reads each file a gallery presents with its size and whether it can be opened', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 10,
                        block: {
                            type: 'attachmentGallery',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            entries: [
                                {
                                    source: 'c-1',
                                    name: 'Master agreement.pdf',
                                    mediaType: 'application/pdf',
                                    sizeOctets: 312000,
                                    availability: 'NotStored',
                                },
                            ],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;

        expect(
            event?.kind === 'block' && event.block.type === 'attachmentGallery' ? event.block.entries : null,
        ).toEqual([
            {
                source: 'c-1',
                name: 'Master agreement.pdf',
                mediaType: 'application/pdf',
                sizeOctets: 312000,
                availability: 'NotStored',
            },
        ]);
    });

    it('reads where a conversation stands off the block that carries it rather than off a member of its own', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 8,
                        block: {
                            type: 'threadState',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            participants: [{ displayName: 'Anna Kowalska', address: null }],
                            agreements: [{ text: 'SLA cut from 4 h to 2 h', sources: ['c-1'] }],
                            openQuestions: [],
                            commitments: [],
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        const event = answered.outcome === 'read' ? answered.value.events[0] : null;

        expect(
            event?.kind === 'block' && event.block.type === 'threadState' ? event.block.standing.agreements : null,
        ).toEqual([{ text: 'SLA cut from 4 h to 2 h', sources: ['c-1'] }]);
    });

    it('refuses a run offering a step that would send mail without being confirmed', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 9,
                        block: {
                            type: 'suggestedAction',
                            version: 1,
                            evidence: {
                                support: 'Supported',
                                citations: ['c-1'],
                                freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
                            },
                            action: 'ReplyToThread',
                            reason: 'They have been waiting two days.',
                            impact: 'SendsMail',
                            requiresConfirmation: false,
                        },
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads an event and a task proposal off the block that carries each', async () => {
        const evidence = {
            support: 'Supported',
            citations: ['c-1'],
            freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
        };

        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 10,
                        block: {
                            type: 'eventProposal',
                            version: 1,
                            evidence,
                            title: 'Renewal call',
                            start: '2026-09-23T09:00:00+00:00',
                            end: null,
                            isAllDay: false,
                        },
                    },
                    {
                        event: 'block',
                        sequence: 11,
                        block: { type: 'taskProposal', version: 1, evidence, title: 'Send the schedule', dueOn: null },
                    },
                ]),
            }),
            runId,
            0,
        );

        const proposals = answered.outcome === 'read' ? answered.value.events : [];

        expect(
            proposals.map((event) =>
                event.kind === 'block' && (event.block.type === 'eventProposal' || event.block.type === 'taskProposal')
                    ? event.block.proposal
                    : null,
            ),
        ).toEqual([
            { title: 'Renewal call', start: '2026-09-23T09:00:00+00:00', end: null, isAllDay: false },
            { title: 'Send the schedule', dueOn: null },
        ]);
    });

    it('refuses a block whose own payload it cannot read rather than drawing the type as unknown', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([
                    {
                        event: 'block',
                        sequence: 7,
                        block: { type: 'answer', version: 1, text: 'No evidence beside it.', confidence: 'High' },
                    },
                ]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a declared source it cannot read rather than drawing a block that names it', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({
                status: 200,
                body: bodyOf([{ event: 'citation', sequence: 2, citation: { id: 'c-1', label: 'Agreement' } }]),
            }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('carries an event kind this contract does not name so the cursor moves past it', async () => {
        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf([{ event: 'riskScored', sequence: 5, score: 3 }]) }),
            runId,
            0,
        );

        expect(answered).toEqual({
            outcome: 'read',
            value: { running: false, events: [{ kind: 'other', sequence: 5 }] },
        });
    });

    it('reads a run this user does not hold as gone rather than as something to retry', async () => {
        const answered = await readDiscoveryRunTail(session, answering({ status: 404, body: '' }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answered = await readDiscoveryRunTail(session, answering({ status, body: '' }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment it could not reach', async () => {
        const answered = await readDiscoveryRunTail(session, () => Promise.reject(new Error('down')), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        ['a body that is not JSON', 'not json'],
        ['a body that is not an object', '[]'],
        ['an answer that does not say whether the run is working', JSON.stringify({ events: [] })],
        ['events that are not a list', JSON.stringify({ running: false, events: {} })],
        ['an event that is not an object', bodyOf(['started'])],
        ['an event with no sequence', bodyOf([{ event: 'block', block: { type: 'answer' } }])],
        ['an event whose sequence is not whole', bodyOf([{ event: 'other', sequence: 1.5 }])],
        ['an event whose sequence is below the first', bodyOf([{ event: 'other', sequence: 0 }])],
        ['an event this contract cannot name', bodyOf([{ event: 7, sequence: 1 }])],
        ['a start that names no revision', bodyOf([{ ...startedEvent, planSchemaVersion: undefined }])],
        ['a start that names no ceilings', bodyOf([{ ...startedEvent, bounds: undefined }])],
        ['a start whose ceilings are incomplete', bodyOf([{ ...startedEvent, bounds: { maximumTokens: 10 } }])],
        ['a start naming no endpoint at all', bodyOf([{ ...startedEvent, endpointAlias: undefined }])],
        [
            'a start whose endpoint name is longer than a screen could carry',
            bodyOf([{ ...startedEvent, endpointAlias: 'a'.repeat(129) }]),
        ],
        ['a retrieval report with no counts', bodyOf([{ event: 'retrieval', sequence: 2, spend: spentEvent.spend }])],
        ['a retrieval report with no spend', bodyOf([{ event: 'retrieval', sequence: 2, progress: progressed }])],
        ['a completion with no spend', bodyOf([{ event: 'completed', sequence: 9 }])],
        ['an ending that names no failure', bodyOf([{ event: 'failed', sequence: 3, ...spentEvent }])],
        ['an ending with no spend', bodyOf([{ event: 'failed', sequence: 3, failure: 'Cancelled' }])],
        [
            'a spend count that is not whole',
            bodyOf([{ event: 'completed', sequence: 9, spend: { ...spentEvent.spend, tokens: 1.5 } }]),
        ],
        ['a block that is not an object', bodyOf([{ event: 'block', sequence: 1, block: 'answer' }])],
        ['a block naming no type', bodyOf([{ event: 'block', sequence: 1, block: {} }])],
        ['a block whose type is empty', bodyOf([{ event: 'block', sequence: 1, block: { type: '' } }])],
    ])('refuses %s', async (_, body) => {
        const answered = await readDiscoveryRunTail(session, answering({ status: 200, body }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses more events than one run may publish', async () => {
        const events = Array.from({ length: 229 }, (_, at) => ({ event: 'other', sequence: at + 1 }));

        const answered = await readDiscoveryRunTail(
            session,
            answering({ status: 200, body: bodyOf(events) }),
            runId,
            0,
        );

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a block type longer than one a screen could name', async () => {
        const body = bodyOf([{ event: 'block', sequence: 1, block: { type: 'a'.repeat(129) } }]);

        const answered = await readDiscoveryRunTail(session, answering({ status: 200, body }), runId, 0);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('startDiscoveryRun', () => {
    const asked = {
        question: 'What did we agree the rate would be?',
        accounts: [],
        folders: [],
        thread: null,
        emails: [],
    };

    it('asks the question and its scope in one request, so a run never reads mail nobody chose', async () => {
        const { transport, requests } = recording({ status: 202, body: JSON.stringify({ runId }) });

        await startDiscoveryRun(session, transport, { ...asked, folders: ['role:Inbox'], emails: ['an-email'] });

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/discovery/runs');
        expect(JSON.parse(requests[0]?.body ?? '')).toEqual({
            question: 'What did we agree the rate would be?',
            accounts: [],
            folders: ['role:Inbox'],
            thread: null,
            emails: ['an-email'],
        });
    });

    it('answers with the run to follow rather than with an answer', async () => {
        const answered = await startDiscoveryRun(
            session,
            answering({ status: 202, body: JSON.stringify({ runId }) }),
            asked,
        );

        expect(answered).toEqual({ outcome: 'read', value: runId });
    });

    it('carries the conversation a question was asked about', async () => {
        const { transport, requests } = recording({ status: 202, body: JSON.stringify({ runId }) });

        await startDiscoveryRun(session, transport, { ...asked, thread: 'a-thread' });

        expect(JSON.parse(requests[0]?.body ?? '')).toMatchObject({ thread: 'a-thread' });
    });

    it.each([
        ['an answer that is not a record', '[]'],
        ['an answer naming no run', '{}'],
        ['a run named as something other than a string', '{"runId":7}'],
        ['a run named as nothing at all', '{"runId":""}'],
        ['an answer that is not JSON', 'accepted'],
    ])('refuses %s rather than following a run it cannot name', async (_unused, body) => {
        const answered = await startDiscoveryRun(session, answering({ status: 202, body }), asked);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 202 } });
    });

    it('refuses an identifier longer than one a later read could put in a path', async () => {
        const body = JSON.stringify({ runId: 'a'.repeat(129) });

        const answered = await startDiscoveryRun(session, answering({ status: 202, body }), asked);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 202 } });
    });

    it.each([
        // A question this client composed and the deployment refused is the shared reading of a refused request rather
        // than one of this route's own: nothing about the answer was unreadable, and what a reader does is ask again.
        [400, 'unavailable'],
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [200, 'unavailable'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answered = await startDiscoveryRun(session, answering({ status, body: '' }), asked);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment it could not reach', async () => {
        const answered = await startDiscoveryRun(session, () => Promise.reject(new Error('down')), asked);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('stopDiscoveryRun', () => {
    it('asks the deployment to stop the run itself rather than stopping the reading', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        const answered = await stopDiscoveryRun(session, transport, runId);

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/discovery/runs/${runId}`);
        expect(answered.outcome).toBe('read');
    });

    it('reads a run this user does not hold as gone rather than as something to retry', async () => {
        const answered = await stopDiscoveryRun(session, answering({ status: 404, body: '' }), runId);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [200, 'unavailable'],
        [500, 'unavailable'],
    ])('reports %i as %s', async (status, reason) => {
        const answered = await stopDiscoveryRun(session, answering({ status, body: '' }), runId);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment it could not reach', async () => {
        const answered = await stopDiscoveryRun(session, () => Promise.reject(new Error('down')), runId);

        expect(answered).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
