// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type {
    AnswerBlock,
    ClientResult,
    DiscoveryRunEvent,
    DiscoveryRunSpend,
    RunTail,
} from '@mailfathom/client-backend';
import { answerAfter, nothingRead } from './followedRun';

// What these tests read is the run's own bookkeeping — the order blocks arrived in, the sources they may rest on, the
// cursor — and never what a block holds, so the fixture is a block this build cannot draw at all: every type the
// contract carries now has a renderer, and a payload here would be one more thing to keep in step for nothing.
function arrived(named: string): AnswerBlock {
    return { type: null, named };
}

function answering(events: readonly DiscoveryRunEvent[], running = false): ClientResult<RunTail<DiscoveryRunEvent>> {
    return { outcome: 'read', value: { running, events } };
}

const started: DiscoveryRunEvent = {
    kind: 'started',
    sequence: 1,
    planSchemaVersion: 4,
    ceilings: { retrievedCharacters: 20_000, providerCalls: 8, tokens: 80_000 },
    endpointAlias: 'house',
    publishedModel: 'gpt-4o',
};

const spent: DiscoveryRunSpend = { providerCalls: 3, tokens: 1_200, retrievedCharacters: 900, messagesRetrieved: 4 };

describe('nothingRead', () => {
    it('is a run still working, so a screen says so while the first read is out', () => {
        expect(nothingRead.running).toBe(true);
        expect(nothingRead.ending).toBeNull();
        expect(nothingRead.blocks).toEqual([]);
        expect(nothingRead.sources).toEqual(new Map());
    });
});

describe('answerAfter', () => {
    it('keeps the blocks in the order the run published them', () => {
        const answer = answerAfter(
            nothingRead,
            answering([
                { kind: 'block', sequence: 2, block: arrived('RiskScore') },
                { kind: 'block', sequence: 3, block: arrived('Sentiment') },
            ]),
        );

        expect(answer.blocks.map((block) => block.sequence)).toEqual([2, 3]);
    });

    it('adds what a later read brought to what an earlier one did', () => {
        const first = answerAfter(
            nothingRead,
            answering([{ kind: 'block', sequence: 2, block: arrived('RiskScore') }], true),
        );

        const second = answerAfter(first, answering([{ kind: 'block', sequence: 3, block: arrived('Sentiment') }]));

        expect(second.blocks.map((block) => block.block.named)).toEqual(['RiskScore', 'Sentiment']);
    });

    it('keeps every source the run declared, so a block drawn later can still name what it rests on', () => {
        const declared = {
            id: 'c-1',
            target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
            label: 'Master agreement',
            medium: 'Written',
            unreadable: null,
        } as const;

        const first = answerAfter(nothingRead, answering([{ kind: 'citation', sequence: 1, source: declared }], true));
        const second = answerAfter(first, answering([{ kind: 'block', sequence: 2, block: arrived('RiskScore') }]));

        expect(second.sources.get('c-1')).toEqual(declared);
    });

    it('reads the revision the plan was written against off the run that started', () => {
        const answer = answerAfter(nothingRead, answering([started]));

        expect(answer.planSchemaVersion).toBe(4);
    });

    it('reads what the run may spend and what answers it off the run that started', () => {
        const answer = answerAfter(nothingRead, answering([started]));

        expect(answer.envelope).toEqual({
            ceilings: { retrievedCharacters: 20_000, providerCalls: 8, tokens: 80_000 },
            endpointAlias: 'house',
            publishedModel: 'gpt-4o',
        });
    });

    it('takes whether more is coming from the run rather than from its events', () => {
        expect(answerAfter(nothingRead, answering([], true)).running).toBe(true);
        expect(answerAfter(nothingRead, answering([])).running).toBe(false);
    });

    it('ignores an event no answer is drawn out of', () => {
        const answer = answerAfter(nothingRead, answering([{ kind: 'other', sequence: 5 }]));

        expect(answer.blocks).toEqual([]);
    });

    it('carries how far retrieval got and what it had spent reaching there', () => {
        const answer = answerAfter(
            nothingRead,
            answering(
                [
                    {
                        kind: 'retrieval',
                        sequence: 2,
                        progress: { lookupsRun: 2, lookupsRefused: 0, lookupsPlanned: 5, passagesFound: 41 },
                        spend: spent,
                    },
                ],
                true,
            ),
        );

        expect(answer.retrieval?.lookupsRun).toBe(2);
        expect(answer.spend).toEqual(spent);
    });

    it('says a finished run finished, and keeps what it finally consumed', () => {
        const answer = answerAfter(nothingRead, answering([{ kind: 'completed', sequence: 9, spend: spent }]));

        expect(answer.ending).toBe('completed');
        expect(answer.spend).toEqual(spent);
    });

    it('keeps the blocks that had arrived when the run ended badly, and says which ending it was', () => {
        const working = answerAfter(
            nothingRead,
            answering([{ kind: 'block', sequence: 2, block: arrived('RiskScore') }], true),
        );

        const ended = answerAfter(
            working,
            answering([{ kind: 'failed', sequence: 3, ending: 'timedOut', spend: spent, retryAt: null }]),
        );

        expect(ended.blocks).toHaveLength(1);
        expect(ended.ending).toBe('timedOut');
        expect(ended.retryAt).toBeNull();
    });

    it('carries when a refused period turns over, which is the one ending that names an instant', () => {
        const answer = answerAfter(
            nothingRead,
            answering([
                {
                    kind: 'failed',
                    sequence: 2,
                    ending: 'periodSpent',
                    spend: spent,
                    retryAt: '2026-09-21T13:00:00+00:00',
                },
            ]),
        );

        expect(answer.ending).toBe('periodSpent');
        expect(answer.retryAt).toBe('2026-09-21T13:00:00+00:00');
    });

    it('says the deployment is out of reach and keeps what had arrived', () => {
        const first = answerAfter(
            nothingRead,
            answering([{ kind: 'block', sequence: 1, block: arrived('RiskScore') }], true),
        );

        const second = answerAfter(first, { outcome: 'failed', failure: { reason: 'unavailable', status: null } });

        expect(second).toEqual({ ...first, failure: 'unavailable' });
    });

    it('carries which way the read failed rather than one flag for all four', () => {
        const reasons = ['unauthenticated', 'unauthorized', 'unavailable', 'unreadable'] as const;

        const carried = reasons.map(
            (reason) => answerAfter(nothingRead, { outcome: 'failed', failure: { reason, status: null } }).failure,
        );

        expect(carried).toEqual([...reasons]);
    });

    it('says a run the deployment no longer holds is gone rather than letting it disappear', () => {
        const answer = answerAfter(nothingRead, { outcome: 'failed', failure: { reason: 'missing', status: 404 } });

        expect(answer).toEqual({ ...nothingRead, running: false, ending: 'gone' });
    });

    it('takes back a failed read once a read answers again', () => {
        const lost = answerAfter(nothingRead, { outcome: 'failed', failure: { reason: 'unavailable', status: null } });

        expect(answerAfter(lost, answering([], true)).failure).toBeNull();
    });
});
