// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { AgentConversationEntry, AnswerBlock } from '@mailfathom/client-backend';
import { answerInFlight, threadOf } from './conversationThread';

// The fold never reads inside a block, so the one a newer service would write stands in for any of them.
const block: AnswerBlock = { named: 'somethingNew', type: null };

const asked: AgentConversationEntry = {
    kind: 'message',
    sequence: 1,
    messageId: 'q-1',
    author: 'person',
    text: 'How many bays?',
    scope: { kind: 'thread', subject: 't-1' },
};

const started: AgentConversationEntry = { kind: 'answerStarted', sequence: 2, run: 'q-1' };

describe('threadOf', () => {
    it('folds what an answer was written as into the answer, under the scope of the question it answers', () => {
        const turns = threadOf([
            asked,
            started,
            { kind: 'status', sequence: 3, run: 'q-1', status: 'Reading' },
            { kind: 'status', sequence: 4, run: 'q-1', status: 'Counting' },
            { kind: 'block', sequence: 5, run: 'q-1', block },
            { kind: 'answerEnded', sequence: 6, run: 'q-1', outcome: 'completed' },
        ]);

        expect(turns).toMatchObject([
            { kind: 'question', text: 'How many bays?' },
            {
                kind: 'answer',
                run: 'q-1',
                scope: { kind: 'thread' },
                status: 'Counting',
                blocks: [{ sequence: 5, phase: null }],
                ending: 'completed',
            },
        ]);
    });

    it('holds a proposal pending until a resolution decides it', () => {
        const proposed: AgentConversationEntry = { kind: 'proposal', sequence: 3, run: 'q-1', block };

        const pending = threadOf([asked, started, proposed]);
        const declined = threadOf([
            asked,
            started,
            proposed,
            { kind: 'resolution', sequence: 4, proposedAt: 3, state: 'declined' },
        ]);

        expect(pending[1]).toMatchObject({ blocks: [{ phase: 'pending' }] });
        expect(declined[1]).toMatchObject({ blocks: [{ phase: 'declined' }] });
    });

    it('reads a line the agent wrote after a stop as the note the stop left', () => {
        const turns = threadOf([
            asked,
            started,
            { kind: 'answerEnded', sequence: 3, run: 'q-1', outcome: 'stopped' },
            { kind: 'message', sequence: 4, messageId: 'n-1', author: 'agent', text: 'Stopped.', scope: null },
        ]);

        expect(turns[2]).toEqual({ kind: 'note', sequence: 4, text: 'Stopped.', afterStop: true });
    });

    it('draws nothing for an entry kind this build does not read', () => {
        expect(threadOf([{ kind: 'other', sequence: 1 }])).toEqual([]);
    });
});

describe('answerInFlight', () => {
    it('is the last answer while it has not ended', () => {
        expect(answerInFlight(threadOf([asked, started]))).toMatchObject({ run: 'q-1' });
    });

    it('is nothing once the last answer ended', () => {
        const turns = threadOf([asked, started, { kind: 'answerEnded', sequence: 3, run: 'q-1', outcome: 'failed' }]);

        expect(answerInFlight(turns)).toBeNull();
    });
});
