// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientResult, DiscoveryRunEvent, RunTail } from '@mailfathom/client-backend';
import { answerAfter, nothingRead } from './followedRun';

function answering(events: readonly DiscoveryRunEvent[], running = false): ClientResult<RunTail<DiscoveryRunEvent>> {
    return { outcome: 'read', value: { running, events } };
}

describe('nothingRead', () => {
    it('is a run still working, so a screen says so while the first read is out', () => {
        expect(nothingRead).toEqual({ blocks: [], running: true, planSchemaVersion: null, unreachable: false });
    });
});

describe('answerAfter', () => {
    it('keeps the blocks in the order the run published them', () => {
        const answer = answerAfter(
            nothingRead,
            answering([
                { kind: 'block', sequence: 2, block: { type: 'answer', named: 'answer' } },
                { kind: 'block', sequence: 3, block: { type: 'timeline', named: 'timeline' } },
            ]),
        );

        expect(answer.blocks.map((arrived) => arrived.sequence)).toEqual([2, 3]);
    });

    it('adds what a later read brought to what an earlier one did', () => {
        const first = answerAfter(
            nothingRead,
            answering([{ kind: 'block', sequence: 2, block: { type: 'answer', named: 'answer' } }], true),
        );

        const second = answerAfter(
            first,
            answering([{ kind: 'block', sequence: 3, block: { type: 'people', named: 'people' } }]),
        );

        expect(second.blocks.map((arrived) => arrived.block.named)).toEqual(['answer', 'people']);
    });

    it('reads the revision the plan was written against off the run that started', () => {
        const answer = answerAfter(nothingRead, answering([{ kind: 'started', sequence: 1, planSchemaVersion: 4 }]));

        expect(answer.planSchemaVersion).toBe(4);
    });

    it('takes whether more is coming from the run rather than from its events', () => {
        expect(answerAfter(nothingRead, answering([], true)).running).toBe(true);
        expect(answerAfter(nothingRead, answering([])).running).toBe(false);
    });

    it('ignores an event no answer is drawn out of', () => {
        const answer = answerAfter(nothingRead, answering([{ kind: 'other', sequence: 5 }]));

        expect(answer.blocks).toEqual([]);
    });

    it('says the deployment is out of reach and keeps what had arrived', () => {
        const first = answerAfter(
            nothingRead,
            answering([{ kind: 'block', sequence: 1, block: { type: 'answer', named: 'answer' } }], true),
        );

        const second = answerAfter(first, { outcome: 'failed', failure: { reason: 'unavailable', status: null } });

        expect(second).toEqual({ ...first, unreachable: true });
    });

    it('stops waiting on a run this person does not hold', () => {
        const answer = answerAfter(nothingRead, { outcome: 'failed', failure: { reason: 'missing', status: 404 } });

        expect(answer).toEqual({ ...nothingRead, running: false });
    });

    it('takes back an unreachable deployment once a read answers again', () => {
        const lost = answerAfter(nothingRead, { outcome: 'failed', failure: { reason: 'unavailable', status: null } });

        expect(answerAfter(lost, answering([], true)).unreachable).toBe(false);
    });
});
