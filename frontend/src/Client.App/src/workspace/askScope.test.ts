// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { askScopeInForce, askScopeOnScreen, mostAskedQuestions, withAsked } from './askScope';
import { emptyWorkspace, type Workspace } from './useWorkspace';

const workAccount: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

function looking(at: Partial<Workspace>): Workspace {
    return { ...emptyWorkspace, ...at };
}

describe('askScopeOnScreen', () => {
    it('is the mailbox being read where nothing narrower is open', () => {
        expect(askScopeOnScreen(looking({ scope: { kind: 'account', accountId: 'work' } }))).toEqual({
            kind: 'mail',
            scope: { kind: 'account', accountId: 'work' },
        });
    });

    it('is the correspondence being read rather than the folder it was reached from', () => {
        expect(
            askScopeOnScreen(
                looking({
                    scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
                    conversation: { threadId: 'thread-1', openAt: null },
                }),
            ),
        ).toEqual({ kind: 'thread', threadId: 'thread-1' });
    });

    it('is the messages picked out, which are narrower than the correspondence holding them', () => {
        expect(
            askScopeOnScreen(
                looking({ conversation: { threadId: 'thread-1', openAt: null }, selected: ['one', 'two'] }),
            ),
        ).toEqual({ kind: 'selection', messages: ['one', 'two'] });
    });
});

describe('askScopeInForce', () => {
    it('is what somebody named in the field rather than what the screen is showing', () => {
        expect(
            askScopeInForce(
                looking({
                    conversation: { threadId: 'thread-1', openAt: null },
                    askScope: { kind: 'everything' },
                }),
                [workAccount],
            ),
        ).toEqual({ kind: 'mail', scope: { kind: 'everything' } });
    });

    // A scope outlives the answer it was chosen from, so a mailbox whose declaration has gone since would otherwise be
    // a question asked about nothing at all, named on the line whose whole job is to say what is in scope.
    it('falls back to the screen where the mailbox named is no longer declared', () => {
        expect(askScopeInForce(looking({ askScope: { kind: 'account', accountId: 'gone' } }), [workAccount])).toEqual({
            kind: 'mail',
            scope: { kind: 'everything' },
        });
    });
});

describe('withAsked', () => {
    it('puts the newest question at the front', () => {
        const asked = withAsked(
            [{ question: 'first', scope: { kind: 'mail', scope: { kind: 'everything' } } }],
            'second',
            { kind: 'thread', threadId: 'thread-1' },
        );

        expect(asked.map((one) => one.question)).toEqual(['second', 'first']);
    });

    // Asking the same question under a wider scope is the commonest thing somebody does after too narrow an answer,
    // so the sentence moves rather than repeating: two rows reading the same words is a list nobody can pick from.
    it('moves a question asked again rather than writing it twice', () => {
        const asked = withAsked(
            [
                { question: 'what did they promise', scope: { kind: 'thread', threadId: 'thread-1' } },
                { question: 'who signed it', scope: { kind: 'mail', scope: { kind: 'everything' } } },
            ],
            'what did they promise',
            { kind: 'mail', scope: { kind: 'everything' } },
        );

        expect(asked).toEqual([
            { question: 'what did they promise', scope: { kind: 'mail', scope: { kind: 'everything' } } },
            { question: 'who signed it', scope: { kind: 'mail', scope: { kind: 'everything' } } },
        ]);
    });

    it('drops the oldest once the list is as long as one tab keeps', () => {
        const asked = Array.from({ length: mostAskedQuestions }, (_, at) => ({
            question: `question ${String(at)}`,
            scope: { kind: 'mail', scope: { kind: 'everything' } } as const,
        }));

        const kept = withAsked(asked, 'the newest one', { kind: 'mail', scope: { kind: 'everything' } });

        expect(kept).toHaveLength(mostAskedQuestions);
        expect(kept[0]?.question).toBe('the newest one');
        expect(kept.some((one) => one.question === `question ${String(mostAskedQuestions - 1)}`)).toBe(false);
    });
});
