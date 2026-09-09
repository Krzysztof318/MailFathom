// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import {
    askScopeInForce,
    askScopeKey,
    askScopeOnScreen,
    askScopesOffered,
    mostAskedQuestions,
    withAsked,
    withoutFragmentText,
} from './askScope';
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

    it('is the message being read rather than the folder it was opened from', () => {
        expect(
            askScopeOnScreen(
                looking({ scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' }, selection: 'message-1' }),
            ),
        ).toEqual({ kind: 'message', messageId: 'message-1' });
    });

    it('is the passage highlighted, which is narrower than the message carrying it', () => {
        expect(
            askScopeOnScreen(
                looking({
                    selection: 'message-1',
                    fragment: { messageId: 'message-1', text: 'the delivery date we agreed' },
                }),
            ),
        ).toEqual({ kind: 'fragment', messageId: 'message-1', text: 'the delivery date we agreed' });
    });

    // A question scoped to words nobody can see any more is the disclosure the field exists to prevent, so the passage
    // stops counting the moment the reader moves on rather than waiting for something to clear it.
    it('ignores a passage whose message is no longer the one being read', () => {
        expect(
            askScopeOnScreen(
                looking({
                    selection: 'message-2',
                    fragment: { messageId: 'message-1', text: 'the delivery date we agreed' },
                }),
            ),
        ).toEqual({ kind: 'message', messageId: 'message-2' });
    });
});

describe('askScopesOffered', () => {
    // Four scopes at once, in increasing specificity, and every one of them reachable without deselecting anything:
    // this is the whole of what the field promises somebody who ticked rows inside a correspondence and then read a
    // paragraph of one of them.
    it('offers every scope the screen is pointing at, what is in force first', () => {
        const offered = askScopesOffered(
            looking({
                scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
                conversation: { threadId: 'thread-1', openAt: null },
                selection: 'message-1',
                selected: ['message-1', 'message-2'],
                fragment: { messageId: 'message-1', text: 'the delivery date' },
            }),
            [workAccount],
        );

        expect(offered.map(askScopeKey)).toEqual([
            'fragment:message-1',
            'selection',
            'thread:thread-1',
            'message:message-1',
            'folder:work:Invoices',
            'account:work',
            'everything',
        ]);
    });

    it('offers the mailbox a folder in scope belongs to, so widening does not jump to every mailbox', () => {
        const offered = askScopesOffered(looking({ scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' } }), [
            workAccount,
        ]);

        expect(offered.map(askScopeKey)).toEqual(['folder:work:Invoices', 'account:work', 'everything']);
    });

    it('offers no scope twice where the screen is already reading one of the mailboxes', () => {
        const offered = askScopesOffered(looking({ scope: { kind: 'account', accountId: 'work' } }), [workAccount]);

        expect(offered.map(askScopeKey)).toEqual(['account:work', 'everything']);
    });
});

describe('askScopeInForce', () => {
    it('is what somebody named in the field rather than what the screen is showing', () => {
        expect(
            askScopeInForce(
                looking({
                    conversation: { threadId: 'thread-1', openAt: null },
                    askScopeKey: 'everything',
                }),
                [workAccount],
            ),
        ).toEqual({ kind: 'mail', scope: { kind: 'everything' } });
    });

    // Narrowing is the same act as widening and costs the same one control: the screen keeps showing the folder and
    // the correspondence, and the next question is about the paragraph alone.
    it('is a passage somebody narrowed to while the screen still shows the correspondence', () => {
        expect(
            askScopeInForce(
                looking({
                    conversation: { threadId: 'thread-1', openAt: null },
                    selection: 'message-1',
                    selected: ['message-1', 'message-2'],
                    fragment: { messageId: 'message-1', text: 'the delivery date' },
                    askScopeKey: 'fragment:message-1',
                }),
                [workAccount],
            ),
        ).toEqual({ kind: 'fragment', messageId: 'message-1', text: 'the delivery date' });
    });

    // A scope outlives the answer it was chosen from, so a mailbox whose declaration has gone since would otherwise be
    // a question asked about nothing at all, named on the line whose whole job is to say what is in scope.
    it('falls back to the screen where the mailbox named is no longer declared', () => {
        expect(askScopeInForce(looking({ askScopeKey: 'account:gone' }), [workAccount])).toEqual({
            kind: 'mail',
            scope: { kind: 'everything' },
        });
    });

    it('falls back to the screen where the correspondence named has been closed', () => {
        expect(askScopeInForce(looking({ askScopeKey: 'thread:thread-1' }), [workAccount])).toEqual({
            kind: 'mail',
            scope: { kind: 'everything' },
        });
    });

    // Ticking a fifth row is not choosing something else, so the selection stays named and answers with what is ticked
    // now — which is what "changing the selection updates the scope immediately" has to mean for a named one too.
    it('follows the selection somebody named as rows are ticked and unticked', () => {
        expect(
            askScopeInForce(looking({ selected: ['one', 'two', 'three'], askScopeKey: 'selection' }), [workAccount]),
        ).toEqual({ kind: 'selection', messages: ['one', 'two', 'three'] });
    });
});

describe('withoutFragmentText', () => {
    it('keeps the question and reduces a passage to the message it came from', () => {
        expect(
            withoutFragmentText({
                question: 'what did they promise',
                scope: { kind: 'fragment', messageId: 'message-1', text: 'by the end of the month' },
            }),
        ).toEqual({ question: 'what did they promise', scope: { kind: 'message', messageId: 'message-1' } });
    });

    it('leaves a scope carrying no mail content as it was', () => {
        const asked = { question: 'who signed it', scope: { kind: 'thread', threadId: 'thread-1' } } as const;

        expect(withoutFragmentText(asked)).toBe(asked);
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
