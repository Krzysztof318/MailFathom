// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { runAskedFor } from './runAsk';

const question = 'What did we agree the rate would be?';

describe('runAskedFor', () => {
    it('carries the words somebody typed unchanged', () => {
        const asked = runAskedFor({ question, scope: { kind: 'mail', scope: { kind: 'everything' } } });

        expect(asked.question).toBe(question);
    });

    it('asks every mailbox as the empty scope rather than as a field left out', () => {
        expect(runAskedFor({ question, scope: { kind: 'mail', scope: { kind: 'everything' } } })).toEqual({
            question,
            accounts: [],
            folders: [],
            thread: null,
            emails: [],
        });
    });

    it('names every folder playing one role the way the service reads one', () => {
        const asked = runAskedFor({ question, scope: { kind: 'mail', scope: { kind: 'role', role: 'Inbox' } } });

        expect(asked.folders).toEqual(['role:Inbox']);
        expect(asked.accounts).toEqual([]);
    });

    it('asks one account without naming a folder of it', () => {
        const asked = runAskedFor({
            question,
            scope: { kind: 'mail', scope: { kind: 'account', accountId: 'work' } },
        });

        expect(asked).toMatchObject({ accounts: ['work'], folders: [] });
    });

    it('asks one folder of one account by the alias the client names a folder by', () => {
        const asked = runAskedFor({
            question,
            scope: { kind: 'mail', scope: { kind: 'folder', accountId: 'work', alias: 'ARCHIVE/2024' } },
        });

        expect(asked).toMatchObject({ accounts: ['work'], folders: ['ARCHIVE/2024'] });
    });

    it('asks about a correspondence as the conversation rather than as its messages', () => {
        const asked = runAskedFor({ question, scope: { kind: 'thread', threadId: 'a-thread' } });

        expect(asked).toMatchObject({ thread: 'a-thread', emails: [] });
    });

    it('asks about the messages somebody picked out, in the order the list drew them', () => {
        const asked = runAskedFor({ question, scope: { kind: 'selection', messages: ['second', 'first'] } });

        expect(asked).toMatchObject({ emails: ['second', 'first'], thread: null });
    });

    it('asks about the one message being read', () => {
        const asked = runAskedFor({ question, scope: { kind: 'message', messageId: 'a-message' } });

        expect(asked).toMatchObject({ emails: ['a-message'] });
    });

    it('asks about a highlighted passage as the message it was cut from, the route carrying nothing finer', () => {
        const asked = runAskedFor({
            question,
            scope: { kind: 'fragment', messageId: 'a-message', text: 'Monthly remuneration is EUR 1,200 net' },
        });

        expect(asked).toEqual({ question, accounts: [], folders: [], thread: null, emails: ['a-message'] });
    });
});
