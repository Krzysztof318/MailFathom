// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test } from '@playwright/test';

import { FakeDeployment } from './fakeDeployment';
import * as agent from './fixtures/agent';
import * as discovery from './fixtures/discovery';
import * as mail from './fixtures/mail';

// The fake deployment every browser check signs in to, asked directly rather than through a page: what a journey
// relies on is that a read after a write answers what the write left behind, and a check that got that wrong would
// pass or fail for a reason nobody could see on the screen. Nothing here starts a browser.

/** One request put to the fake, and the JSON it answered with. */
function ask(deployment: FakeDeployment, method: string, route: string, body?: unknown): unknown {
    const answer = deployment.answer({
        method,
        url: `http://deployment.invalid/api/client${route}`,
        body: body === undefined ? null : JSON.stringify(body),
    });

    return answer.body === '' ? answer.status : JSON.parse(answer.body);
}

/** The identities of the rows one read of a folder answered with. */
function listed(deployment: FakeDeployment, query: string): string[] {
    const page = ask(deployment, 'GET', `/emails?${query}`) as { emails: { id: string }[] };

    return page.emails.map(({ id }) => id);
}

/** The two counts the folders route answers for one folder of the corpus mailbox. */
function counts(deployment: FakeDeployment, alias: string): { stored: number; unread: number } {
    const folders = ask(deployment, 'GET', '/folders') as {
        accounts: { folders: { alias: string; storedEmailCount: number; unreadEmailCount: number }[] }[];
    };
    const folder = folders.accounts.flatMap((account) => account.folders).find((held) => held.alias === alias);

    return { stored: folder?.storedEmailCount ?? Number.NaN, unread: folder?.unreadEmailCount ?? Number.NaN };
}

test('leaves a deleted message out of every later read, and puts it back when the delete is withdrawn', () => {
    const deployment = new FakeDeployment('0.0.0');

    const deleted = ask(deployment, 'POST', '/mutations/deletes', { deletes: [{ storedEmailId: 'message-1' }] }) as {
        results: { change: { recordId: string } }[];
    };
    const recordId = deleted.results[0]?.change.recordId ?? '';

    expect(listed(deployment, 'folder=INBOX').slice(0, 2)).toStrictEqual(['message-0', 'message-2']);
    expect(listed(deployment, 'folder=INBOX')).toHaveLength(100);
    expect(ask(deployment, 'GET', '/messages/message-1')).toBe(404);

    ask(deployment, 'POST', '/mutations/deletes/withdrawals', { recordIds: [recordId] });

    expect(listed(deployment, 'folder=INBOX').slice(0, 2)).toStrictEqual(['message-0', 'message-1']);
    expect(ask(deployment, 'GET', `/mutations?record=${recordId}`)).toMatchObject({
        changes: [{ recordId, storedEmailId: 'message-1', state: 'cancelled' }],
    });
});

test('files a moved message in the folder it was moved to, and moves both folders counts with it', () => {
    const deployment = new FakeDeployment('0.0.0');
    const before = { inbox: counts(deployment, 'INBOX'), archive: counts(deployment, 'ARCHIVE/2024') };

    const moved = ask(deployment, 'POST', '/mutations/moves', {
        moves: [{ storedEmailId: 'message-0', destinationFolder: 'ARCHIVE/2024' }],
    });

    expect(moved).toMatchObject({
        results: [{ storedEmailId: 'message-0', outcome: 'recorded', destinationFolder: 'ARCHIVE/2024' }],
    });
    expect(listed(deployment, 'folder=INBOX')[0]).toBe('message-1');
    expect(listed(deployment, 'folder=ARCHIVE%2F2024')).toStrictEqual(['message-0']);

    // Message zero is one the corpus states unread, so it takes one of each count across with it.
    expect(counts(deployment, 'INBOX')).toStrictEqual({
        stored: before.inbox.stored - 1,
        unread: before.inbox.unread - 1,
    });
    expect(counts(deployment, 'ARCHIVE/2024')).toStrictEqual({
        stored: before.archive.stored + 1,
        unread: before.archive.unread + 1,
    });
});

test('files a message the folder never listed, one out of a thread or a search answer, where it was moved to', () => {
    const deployment = new FakeDeployment('0.0.0');
    const threaded = mail.conversation.messages[1]?.email.id ?? '';
    const searched = mail.searchResults.results[0]?.id ?? '';

    ask(deployment, 'POST', '/mutations/moves', {
        moves: [
            { storedEmailId: threaded, destinationFolder: 'ARCHIVE/2024' },
            { storedEmailId: searched, destinationFolder: 'ARCHIVE/2024' },
        ],
    });

    expect(listed(deployment, 'folder=ARCHIVE%2F2024')).toStrictEqual([threaded, searched]);
});

test('answers a later read with the flags a change left, and filters on them', () => {
    const deployment = new FakeDeployment('0.0.0');

    ask(deployment, 'POST', '/mutations/flags', {
        changes: [{ storedEmailId: 'message-0', flags: { seen: true, flagged: true } }],
    });

    const page = ask(deployment, 'GET', '/emails?folder=INBOX') as {
        emails: { id: string; unread: boolean; flagged: boolean }[];
    };

    expect(page.emails[0]).toMatchObject({ id: 'message-0', unread: false, flagged: true });
    expect(listed(deployment, 'folder=INBOX&flagged=true')).toStrictEqual(['message-0']);
});

test('pages the folder on from where the last page ended, and back from where it began', () => {
    const deployment = new FakeDeployment('0.0.0');

    ask(deployment, 'POST', '/mutations/deletes', { deletes: [{ storedEmailId: 'message-150' }] });

    const second = ask(deployment, 'GET', '/emails?folder=INBOX&cursor=100') as {
        emails: { id: string }[];
        nextCursor: string;
        previousCursor: string;
    };

    expect(second.emails).toHaveLength(100);
    expect(second.emails.map(({ id }) => id)).not.toContain('message-150');
    expect(second.nextCursor).toBe('201');
    expect(listed(deployment, `folder=INBOX&direction=backward&cursor=${second.previousCursor}`)).toHaveLength(100);
});

test('keeps a draft across saves as a revision each time, and forgets it once it is sent', () => {
    const deployment = new FakeDeployment('0.0.0');
    const composition = {
        account: 'work',
        subject: 'The yard',
        plainTextBody: 'Tuesday?',
        to: ['yard@example.invalid'],
    };

    const written = ask(deployment, 'POST', '/drafts', composition) as { draft: { draftId: string; revision: number } };
    const { draftId } = written.draft;
    const revised = ask(deployment, 'PUT', `/drafts/${draftId}`, { ...composition, subject: 'The yard, Tuesday' });

    expect(revised).toMatchObject({
        draft: {
            draftId,
            subject: 'The yard, Tuesday',
            revision: 2,
            recipients: [{ role: 'To', address: 'yard@example.invalid' }],
        },
    });
    expect(ask(deployment, 'POST', `/drafts/${draftId}/send`)).toHaveProperty('outgoingEmail');
    expect(ask(deployment, 'PUT', `/drafts/${draftId}`, composition)).toBe(404);
});

test('answers a question running once when asked to, and finished on the read after', () => {
    const deployment = new FakeDeployment('0.0.0');

    deployment.startNextAnswerRunning();
    ask(deployment, 'POST', '/discovery/runs', { question: 'How many bays?' });

    expect(ask(deployment, 'GET', `/discovery/runs/${discovery.runId}`)).toMatchObject({ running: true });
    expect(ask(deployment, 'GET', `/discovery/runs/${discovery.runId}?since=6`)).toMatchObject({ running: false });

    ask(deployment, 'POST', '/discovery/runs', { question: 'And the dates?' });

    expect(ask(deployment, 'GET', `/discovery/runs/${discovery.runId}`)).toMatchObject({ running: false });
});

test('writes the answer to a proposal into the conversation, and refuses a second one', () => {
    const deployment = new FakeDeployment('0.0.0');
    const conversation = `/agent/conversations/${agent.answeredConversationId}`;

    const answered = ask(deployment, 'PUT', `${conversation}/proposals/6`, { decision: 'declined' });

    expect(answered).toStrictEqual({ sequence: 12 });
    expect(ask(deployment, 'GET', `${conversation}?since=11`)).toMatchObject({
        entries: [{ sequence: 12, entry: { entry: 'resolution', proposedAt: 6, state: 'Declined' } }],
    });
    expect(ask(deployment, 'PUT', `${conversation}/proposals/6`, { decision: 'accepted' })).toBe(409);
});

test('opens a conversation started here on the question it was asked, with nothing in it decided yet', () => {
    const deployment = new FakeDeployment('0.0.0');
    const conversation = '/agent/conversations/0198f4a1-0000-7000-8000-00000000b001';
    const scope = { kind: 'DiscoveryRun', subject: discovery.runId };

    ask(deployment, 'POST', `${conversation}/messages`, { messageId: agent.answeredQuestionId, text: 'Bays?', scope });

    const read = ask(deployment, 'GET', conversation) as { entries: { entry: Record<string, unknown> }[] };

    expect(read.entries[0]?.entry).toMatchObject({ entry: 'message', text: 'Bays?', scope });
    expect(read.entries.filter(({ entry }) => entry['entry'] === 'resolution')).toStrictEqual([]);
    expect(ask(deployment, 'PUT', `${conversation}/proposals/7`, { decision: 'declined' })).toHaveProperty('sequence');
});

test('keeps what each request carried, and names the ones it has no answer for', () => {
    const deployment = new FakeDeployment('0.0.0');

    ask(deployment, 'POST', '/mutations/moves', {
        moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }],
    });
    ask(deployment, 'GET', '/emails?folder=ARCHIVE%2F2024');
    ask(deployment, 'POST', '/contacts', {});

    expect(deployment.requests('POST', '/mutations/moves').map(({ body }) => body)).toStrictEqual([
        { moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }] },
    ]);
    expect(deployment.requests('GET', '/emails')[0]?.query.get('folder')).toBe('ARCHIVE/2024');
    expect(deployment.unimplemented).toStrictEqual(['POST /contacts']);
});
