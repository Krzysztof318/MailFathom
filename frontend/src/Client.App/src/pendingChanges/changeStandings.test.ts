// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailMutationRecord, MailMutationRecordState, MailMutationResult } from '@mailfathom/client-backend';
import { isUndecided, readSubmission, standingOf, type ChangeSubmission } from './changeStandings';

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';
const recordId = '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91';

function stood(state: MailMutationRecordState, outcomeUnknown = false): MailMutationRecord {
    return { recordId, storedEmailId, state, outcomeUnknown };
}

function answered(results: readonly MailMutationResult[]): ChangeSubmission {
    return {
        act: 'markRead',
        asked: results.map((result) => result.storedEmailId),
        results,
        askAgain: () => undefined,
        letGo: () => undefined,
    };
}

describe('standingOf', () => {
    it.each([
        ['pending', 'waiting'],
        ['converging', 'waiting'],
        ['completed', 'converged'],
        ['dead-lettered', 'exhausted'],
        ['cancelled', 'converged'],
    ] as const)('reads a %s record as %s', (state, standing) => {
        expect(standingOf(stood(state))).toBe(standing);
    });

    // The mailbox may be in either of two states and MailFathom will not choose between them, so neither may this
    // client: a record marked completed over an unanswered command is completed about the command rather than the mail.
    it('reads a record whose command was never answered as one only a person can settle, whatever its state says', () => {
        expect(standingOf(stood('completed', true))).toBe('unanswered');
    });

    it.each([
        ['waiting', false],
        ['converged', false],
        ['exhausted', true],
        ['unanswered', true],
    ] as const)('says a %s change does%s wait on a person', (standing, waits) => {
        expect(isUndecided(standing)).toBe(waits);
    });
});

describe('readSubmission', () => {
    it('follows every record a written-down change became, naming the message each belongs to', () => {
        const reading = readSubmission(
            answered([
                {
                    storedEmailId,
                    outcome: 'recorded',
                    changes: [
                        { recordId, state: 'pending' },
                        { recordId: 'second', state: 'pending' },
                    ],
                },
            ]),
        );

        expect(reading.followed).toStrictEqual([
            { recordId, storedEmailId, act: 'markRead' },
            { recordId: 'second', storedEmailId, act: 'markRead' },
        ]);
        expect(reading.refused.size).toBe(0);
    });

    it('groups the messages the deployment refused by what it refused them with', () => {
        const reading = readSubmission(
            answered([
                { storedEmailId: 'gone', outcome: 'message-not-found', changes: [] },
                { storedEmailId: 'also-gone', outcome: 'message-not-found', changes: [] },
                { storedEmailId: 'elsewhere', outcome: 'account-no-longer-configured', changes: [] },
            ]),
        );

        expect(reading.followed).toStrictEqual([]);
        expect(reading.refused.get('message-not-found')).toStrictEqual(['gone', 'also-gone']);
        expect(reading.refused.get('account-no-longer-configured')).toStrictEqual(['elsewhere']);
    });

    it('reads a batch the deployment answered partly as both halves at once', () => {
        const reading = readSubmission(
            answered([
                { storedEmailId, outcome: 'recorded', changes: [{ recordId, state: 'pending' }] },
                { storedEmailId: 'gone', outcome: 'message-not-found', changes: [] },
            ]),
        );

        expect(reading.followed).toStrictEqual([{ recordId, storedEmailId, act: 'markRead' }]);
        expect(reading.refused.get('message-not-found')).toStrictEqual(['gone']);
    });

    it('reads a submission that never reached the deployment as one with nothing to follow', () => {
        const reading = readSubmission({ ...answered([]), results: null });

        expect(reading.followed).toStrictEqual([]);
        expect(reading.refused.size).toBe(0);
    });
});
