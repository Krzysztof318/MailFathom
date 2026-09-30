// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test } from '@playwright/test';

import { FakeDeployment, type HubSocket } from './fakeDeployment';
import * as agent from './fixtures/agent';
import * as fixtures from './fixtures/deployment';
import * as discovery from './fixtures/discovery';
import * as mail from './fixtures/mail';
import * as notifications from './fixtures/notifications';

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
    const threaded = mail.conversation().messages[1]?.email.id ?? '';
    const searched = mail.searchResults.results[0]?.id ?? '';

    ask(deployment, 'POST', '/mutations/moves', {
        moves: [
            { storedEmailId: threaded, destinationFolder: 'ARCHIVE/2024' },
            { storedEmailId: searched, destinationFolder: 'ARCHIVE/2024' },
        ],
    });

    expect(listed(deployment, 'folder=ARCHIVE%2F2024')).toStrictEqual([threaded, searched]);
});

test('takes a deleted folder out of both folder routes, the mail filed in it out of every read, and refuses it after', () => {
    const deployment = new FakeDeployment('0.0.0');
    const searched = mail.searchResults.results[0]?.id ?? '';

    ask(deployment, 'POST', '/mutations/moves', {
        moves: [{ storedEmailId: searched, destinationFolder: 'ARCHIVE/2024' }],
    });

    expect(
        ask(deployment, 'POST', '/managed-folders/deletions', { account: 'work', folderId: 'ARCHIVE/2024' }),
    ).toMatchObject({
        change: 'Deleted',
        folder: { id: 'ARCHIVE/2024' },
        mailErasureDeferred: false,
    });

    const managed = ask(deployment, 'GET', '/managed-folders?account=work') as { folders: { id: string }[] };
    const folders = ask(deployment, 'GET', '/folders') as { accounts: { folders: { alias: string }[] }[] };
    const searchedAgain = ask(deployment, 'GET', '/emails/search?query=renewal') as { results: { id: string }[] };

    expect(managed.folders.map(({ id }) => id)).toStrictEqual(['INBOX', 'DRAFTS', 'ARCHIVE', 'FILED', 'TRASH']);
    expect(folders.accounts.flatMap((account) => account.folders.map(({ alias }) => alias))).toStrictEqual([
        'INBOX',
        'DRAFTS',
        'FILED',
        'TRASH',
    ]);
    expect(searchedAgain.results.map(({ id }) => id)).not.toContain(searched);
    expect(ask(deployment, 'GET', `/messages/${searched}`)).toBe(404);
    expect(
        ask(deployment, 'POST', '/managed-folders/deletions', { account: 'work', folderId: 'ARCHIVE/2024' }),
    ).toMatchObject({
        refusal: 'FolderMissing',
    });
});

test('answers a folder made, renamed, and moved at the place each act put it, under the alias it was made with', () => {
    const deployment = new FakeDeployment('0.0.0');
    const placed = () => {
        const folders = ask(deployment, 'GET', '/folders') as {
            accounts: { folders: { alias: string; path: string[] }[] }[];
        };

        return folders.accounts.flatMap((account) => account.folders).find(({ alias }) => alias === 'SUPPLIERS')?.path;
    };

    expect(
        ask(deployment, 'POST', '/managed-folders', { account: 'work', parentId: 'ARCHIVE/2024', name: 'Suppliers' }),
    ).toMatchObject({ change: 'Created', folder: { id: 'SUPPLIERS', parentId: 'ARCHIVE/2024', name: 'Suppliers' } });
    expect(placed()).toStrictEqual(['Archive', '2024', 'Suppliers']);

    ask(deployment, 'POST', '/managed-folders/renames', { account: 'work', folderId: 'SUPPLIERS', name: 'Vendors' });
    expect(placed()).toStrictEqual(['Archive', '2024', 'Vendors']);

    ask(deployment, 'POST', '/managed-folders/moves', { account: 'work', folderId: 'SUPPLIERS', parentId: 'INBOX' });
    expect(placed()).toStrictEqual(['INBOX', 'Vendors']);

    const managed = ask(deployment, 'GET', '/managed-folders?account=work') as { folders: { id: string }[] };

    expect(managed.folders.map(({ id }) => id)).toContain('SUPPLIERS');
    expect(
        ask(deployment, 'POST', '/managed-folders', { account: 'work', parentId: null, name: 'suppliers' }),
    ).toMatchObject({ folder: { id: 'SUPPLIERS-2' } });
});

test('answers a conversation with each message and its body where the reader asked for them', () => {
    const deployment = new FakeDeployment('0.0.0');
    const bare = ask(deployment, 'GET', `/threads/${mail.conversationId}`) as { messages: object[] };
    const whole = ask(deployment, 'GET', `/threads/${mail.conversationId}?content=true`) as {
        messages: { email: { preview: string }; message: object; body: { plainText: { text: string } } }[];
    };

    expect(bare.messages.every((message) => !('body' in message))).toBe(true);
    expect(whole.messages).toHaveLength(bare.messages.length);
    expect(whole.messages.every(({ email, body }) => body.plainText.text === email.preview)).toBe(true);
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

test('answers an inbox marked read past the count it states with nothing unread rather than fewer than nothing', () => {
    const deployment = new FakeDeployment('0.0.0');
    const unread = (ask(deployment, 'GET', '/emails?folder=INBOX&unread=true') as { emails: { id: string }[] }).emails;

    expect(unread.length).toBeGreaterThan(counts(deployment, 'INBOX').unread);

    ask(deployment, 'POST', '/mutations/flags', {
        changes: unread.map(({ id }) => ({ storedEmailId: id, flags: { seen: true } })),
    });

    expect(counts(deployment, 'INBOX').unread).toBe(0);
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

test('files a draft with its staged file in the drafts folder, reads it back whole, and takes it out once sent', () => {
    const deployment = new FakeDeployment('0.0.0');
    const composition = {
        account: 'work',
        subject: 'The yard',
        plainTextBody: 'Tuesday?',
        to: ['yard@example.invalid'],
    };

    const written = ask(deployment, 'POST', '/drafts', composition) as { draft: { draftId: string } };
    const { draftId } = written.draft;

    deployment.answer({
        method: 'POST',
        url: `http://deployment.invalid/api/client/drafts/${draftId}/attachments?fileName=plan.txt`,
        // Text that also reads as JSON, so a fake that parsed what it was handed would give back something else.
        body: '{ "bay": 4 }',
        contentType: 'text/plain',
    });

    const [filed = ''] = listed(deployment, 'folder=DRAFTS');

    expect(counts(deployment, 'DRAFTS')).toStrictEqual({ stored: 1, unread: 0 });
    expect(ask(deployment, 'GET', `/messages/${filed}`)).toMatchObject({
        folder: 'DRAFTS',
        headers: {
            subject: 'The yard',
            participants: expect.arrayContaining([
                { role: 'To', address: 'yard@example.invalid', displayName: null },
            ]) as unknown,
        },
        attachments: [{ position: 0, fileName: 'plan.txt', mediaType: 'text/plain', sizeOctets: 12 }],
    });
    expect(ask(deployment, 'GET', `/messages/${filed}/body`)).toMatchObject({ plainText: { text: 'Tuesday?' } });
    expect(
        deployment.answer({
            method: 'GET',
            url: `http://deployment.invalid/api/client/messages/${filed}/attachments/0`,
            body: null,
        }),
    ).toStrictEqual({ status: 200, body: '{ "bay": 4 }', contentType: 'text/plain' });

    ask(deployment, 'POST', `/drafts/${draftId}/send`);

    expect(listed(deployment, 'folder=DRAFTS')).toStrictEqual([]);
    expect(counts(deployment, 'DRAFTS')).toStrictEqual({ stored: 0, unread: 0 });
});

test('puts a send taken back into the drafts folder as it was sent, and knows nothing of one it never queued', () => {
    const deployment = new FakeDeployment('0.0.0');
    const composition = {
        account: 'work',
        subject: 'The yard',
        plainTextBody: 'Tuesday?',
        to: ['yard@example.invalid'],
    };

    const written = ask(deployment, 'POST', '/drafts', composition) as { draft: { draftId: string } };
    const [filed = ''] = listed(deployment, 'folder=DRAFTS');
    const queued = ask(deployment, 'POST', `/drafts/${written.draft.draftId}/send`) as { outgoingEmail: string };

    expect(listed(deployment, 'folder=DRAFTS')).toStrictEqual([]);
    expect(ask(deployment, 'POST', '/outbox/cancellation', { outgoingEmail: queued.outgoingEmail })).toStrictEqual({
        outgoingEmail: queued.outgoingEmail,
        outcome: 'Accepted',
    });
    expect(listed(deployment, 'folder=DRAFTS')).toStrictEqual([filed]);
    expect(counts(deployment, 'DRAFTS')).toStrictEqual({ stored: 1, unread: 0 });
    expect(ask(deployment, 'GET', `/messages/${filed}/body`)).toMatchObject({ plainText: { text: 'Tuesday?' } });
    expect(ask(deployment, 'POST', '/outbox/cancellation', { outgoingEmail: queued.outgoingEmail })).toMatchObject({
        outcome: 'RecordUnknown',
    });
});

test('mints a session for a password and renews one for a session, each with a token of its own', () => {
    const deployment = new FakeDeployment('0.0.0');
    const exchanged = (authorization: string): unknown =>
        JSON.parse(
            deployment.answer({
                method: 'POST',
                url: 'http://deployment.invalid/api/client/session/token',
                body: null,
                authorization,
            }).body,
        );

    expect(exchanged(fixtures.expectedAuthorization)).toStrictEqual(fixtures.mintedSession);
    expect(exchanged(fixtures.expectedSessionAuthorization)).toStrictEqual(fixtures.renewedSession);
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
    expect(ask(deployment, 'PUT', `${conversation}/proposals/7`, { decision: 'declined' })).toStrictEqual({
        sequence: read.entries.length + 1,
    });
});

test('answers the centre and the bell with every notification raised, marked, and erased since', () => {
    const deployment = new FakeDeployment('0.0.0');
    const centre = () =>
        ask(deployment, 'GET', '/notifications?pageSize=50') as { notifications: { id: string; read: boolean }[] };

    deployment.raiseNotification();

    expect(centre().notifications[0]).toMatchObject({ id: notifications.arrivingNotification.id, read: false });
    expect(ask(deployment, 'GET', '/notifications/unread-count')).toStrictEqual({ unreadCount: 3 });

    expect(
        ask(deployment, 'POST', `/notifications/${notifications.notificationId}/read-state`, { read: true }),
    ).toStrictEqual({ id: notifications.notificationId, read: true, unreadCount: 2 });
    expect(
        ask(deployment, 'POST', '/notifications/deletions', {
            notificationIds: [notifications.arrivingNotification.id, notifications.notificationId],
        }),
    ).toStrictEqual({ deleted: 2, unreadCount: 1 });
    expect(ask(deployment, 'POST', '/notifications/read')).toStrictEqual({ markedRead: 1, unreadCount: 0 });

    expect(centre().notifications.map(({ id }) => id)).not.toContain(notifications.notificationId);
    expect(centre().notifications.every(({ read }) => read)).toBe(true);
    expect(ask(deployment, 'GET', '/notifications/unread-count')).toStrictEqual({ unreadCount: 0 });
});

test('keeps what each request carried, and names the ones it has no answer for', () => {
    const deployment = new FakeDeployment('0.0.0');

    ask(deployment, 'POST', '/mutations/moves', {
        moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }],
    });
    ask(deployment, 'GET', '/emails?folder=ARCHIVE%2F2024');
    ask(deployment, 'POST', '/calendar/import', {});

    expect(deployment.requests('POST', '/mutations/moves').map(({ body }) => body)).toStrictEqual([
        { moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }] },
    ]);
    expect(deployment.requests('GET', '/emails')[0]?.query.get('folder')).toBe('ARCHIVE/2024');
    expect(deployment.unimplemented).toStrictEqual(['POST /calendar/import']);
});

test('places an event it was written in every window it falls in, where an amendment moves it, and nowhere once deleted', () => {
    const deployment = new FakeDeployment('0.0.0');
    const titles = (from: string, until: string) =>
        (
            ask(deployment, 'GET', `/calendar?from=${from}&until=${until}&count=200`) as {
                events: { id: string; title: string }[];
            }
        ).events.map(({ title }) => title);

    const { id } = ask(deployment, 'POST', '/calendar', {
        title: 'Dentist appointment',
        start: '2026-09-29T13:00:00.000Z',
        end: '2026-09-29T13:30:00.000Z',
        isAllDay: false,
        reminders: [],
        sourceMessage: null,
    }) as { id: string };

    expect(titles('2026-09-28T00:00:00.000Z', '2026-10-05T00:00:00.000Z')).toContain('Dentist appointment');
    expect(titles('2026-09-29T00:00:00.000Z', '2026-09-30T00:00:00.000Z')).toContain('Dentist appointment');
    expect(titles('2026-09-30T00:00:00.000Z', '2026-10-01T00:00:00.000Z')).not.toContain('Dentist appointment');

    ask(deployment, 'PUT', `/calendar/${id}`, {
        title: 'Dental check-up',
        start: '2026-09-30T15:00:00.000Z',
        end: '2026-09-30T15:45:00.000Z',
        isAllDay: false,
        reminders: [],
    });

    expect(titles('2026-09-29T00:00:00.000Z', '2026-09-30T00:00:00.000Z')).not.toContain('Dentist appointment');
    expect(titles('2026-09-30T00:00:00.000Z', '2026-10-01T00:00:00.000Z')).toContain('Dental check-up');
    expect(ask(deployment, 'DELETE', `/calendar/${id}`)).toBe(204);
    expect(titles('2026-09-30T00:00:00.000Z', '2026-10-01T00:00:00.000Z')).not.toContain('Dental check-up');
    expect(ask(deployment, 'DELETE', `/calendar/${id}`)).toBe(404);
});

test('holds a corpus event where an amendment put it, rather than where the next window would place it', () => {
    const deployment = new FakeDeployment('0.0.0');
    const window = (from: string, until: string) =>
        ask(deployment, 'GET', `/calendar?from=${from}&until=${until}&count=200`) as {
            events: { id: string; title: string; start: string }[];
        };

    ask(deployment, 'PUT', '/calendar/calendar-1', {
        title: 'Warehouse handover',
        start: '2026-10-07T09:00:00.000Z',
        end: '2026-10-07T10:00:00.000Z',
        isAllDay: false,
        reminders: [],
    });

    expect(window('2026-09-28T00:00:00.000Z', '2026-10-05T00:00:00.000Z').events.map(({ id }) => id)).not.toContain(
        'calendar-1',
    );
    expect(window('2026-10-07T00:00:00.000Z', '2026-10-08T00:00:00.000Z').events).toContainEqual(
        expect.objectContaining({ id: 'calendar-1', start: '2026-10-07T09:00:00.000Z' }),
    );
});

test('lists a task it was written in the committed half, marks it done, and forgets it once erased', () => {
    const deployment = new FakeDeployment('0.0.0');
    const committed = () =>
        (ask(deployment, 'GET', '/tasks?pageSize=50') as { tasks: { id: string; completed: boolean }[] }).tasks;

    const { id } = ask(deployment, 'POST', '/tasks', {
        title: 'Book the carrier for Friday',
        dueOn: '2026-09-29',
        reminders: [],
        dueDayOffsetMinutes: null,
        sourceMessageId: null,
    }) as { id: string };

    expect(committed()).toContainEqual(expect.objectContaining({ id, completed: false }));

    ask(deployment, 'POST', `/tasks/${id}/completion`, { completed: true });

    expect(committed()).toContainEqual(expect.objectContaining({ id, completed: true }));
    expect(ask(deployment, 'DELETE', `/tasks/${id}`)).toStrictEqual({ id, erased: true });
    expect(committed().map((task) => task.id)).not.toContain(id);
    expect(ask(deployment, 'POST', `/tasks/${id}/completion`, { completed: false })).toBe(404);
});

test('walks the address book in name order across pages, with a person it was written and without one erased', () => {
    const deployment = new FakeDeployment('0.0.0');
    const page = (query: string) =>
        ask(deployment, 'GET', `/contacts?${query}`) as {
            contacts: { id: string; displayName: string }[];
            nextCursor: string | null;
        };

    const written = ask(deployment, 'POST', '/contacts', {
        displayName: 'Beatrice Holm',
        addresses: ['beatrice@carrier.example'],
        preferredAddress: 'beatrice@carrier.example',
        note: null,
    }) as { outcome: string; contact: { id: string } };

    expect(written.outcome).toBe('Written');

    const first = page('pageSize=50');

    expect(first.contacts.slice(0, 2).map(({ displayName }) => displayName)).toStrictEqual([
        'Anna Marlow',
        'Beatrice Holm',
    ]);
    expect(page(`pageSize=50&cursor=${first.nextCursor ?? ''}`).contacts).toHaveLength(1);

    expect(ask(deployment, 'DELETE', `/contacts/${written.contact.id}`)).toStrictEqual({
        contact: written.contact.id,
        wasHeld: true,
        addressesErased: 1,
    });
    expect(page('pageSize=50').nextCursor).toBeNull();
    expect(ask(deployment, 'GET', `/contacts/${written.contact.id}/correspondence`)).toBe(404);
});

test('searches either book by a fragment of a name or of an address, whatever its case', () => {
    const deployment = new FakeDeployment('0.0.0');
    const found = (route: string) =>
        (ask(deployment, 'GET', route) as { contacts: { displayName: string }[] }).contacts.map(
            ({ displayName }) => displayName,
        );

    expect(found('/contacts?search=MARLOW&pageSize=5')).toStrictEqual(['Anna Marlow']);
    expect(found('/contacts/collected?search=b.rowe@&pageSize=5')).toStrictEqual(['Bartosz Rowe']);
    expect(found('/contacts?search=%20&pageSize=5')).toHaveLength(5);
});

test('continues a search from a position in the book, filling each page with what matches from there', () => {
    const deployment = new FakeDeployment('0.0.0');
    const page = (query: string) =>
        ask(deployment, 'GET', `/contacts?${query}`) as {
            contacts: { displayName: string }[];
            nextCursor: string | null;
        };

    const walked = page('pageSize=1');

    expect(walked.contacts.map(({ displayName }) => displayName)).toStrictEqual(['Anna Marlow']);
    expect(page(`search=marlow&cursor=${walked.nextCursor ?? ''}`).contacts).toStrictEqual([]);

    const first = page('search=correspondent&pageSize=2');
    const next = page(`search=correspondent&pageSize=2&cursor=${first.nextCursor ?? ''}`);

    expect(first.contacts).toHaveLength(2);
    expect(next.contacts).toHaveLength(2);
    expect(next.contacts.map(({ displayName }) => displayName)).not.toContain(first.contacts[1]?.displayName);
});

/** What ends every message of SignalR's JSON protocol. */
const recordSeparator = '\u001e';

/** The handshake a SignalR client opens every connection with. */
const handshake = `${JSON.stringify({ protocol: 'json', version: 1 })}${recordSeparator}`;

/**
 * A connection to the hub as the client opens one, standing in for the socket a page would route: it records what the
 * hub sent, as the messages the protocol frames, and whether the hub closed it.
 */
class OpenedConnection implements HubSocket {
    readonly received: unknown[] = [];
    closed = false;
    private readonly listeners: ((message: string | Buffer) => void)[] = [];
    private readonly address: string;

    constructor(ticket: string) {
        this.address = `ws://deployment.invalid/api/client/signals?access_token=${encodeURIComponent(ticket)}`;
    }

    url(): string {
        return this.address;
    }

    onMessage(handler: (message: string | Buffer) => void): void {
        this.listeners.push(handler);
    }

    // The hub is told of a close the client made, and nothing here makes one.
    onClose(): void {
        return undefined;
    }

    send(message: string): void {
        this.received.push(
            ...message
                .split(recordSeparator)
                .filter((frame) => frame !== '')
                .map((frame): unknown => JSON.parse(frame)),
        );
    }

    close(): Promise<void> {
        this.closed = true;

        return Promise.resolve();
    }

    /** Sends the hub one message, as the client's own socket would. */
    say(message: string): void {
        for (const listener of this.listeners) {
            listener(message);
        }
    }
}

/** A ticket minted the way the client mints one before every connection. */
function ticketFrom(deployment: FakeDeployment): string {
    return (ask(deployment, 'POST', '/signals/ticket') as { ticket: string }).ticket;
}

/** A deployment serving the channel, with one connection standing on it. */
function connected(): { deployment: FakeDeployment; connection: OpenedConnection } {
    const deployment = new FakeDeployment('0.0.0');

    deployment.serveSignals();

    const connection = new OpenedConnection(ticketFrom(deployment));

    deployment.connect(connection);
    connection.say(handshake);

    return { deployment, connection };
}

test('refuses the ticket until the channel is served, and opens one connection per ticket after', () => {
    const deployment = new FakeDeployment('0.0.0');

    expect(ask(deployment, 'POST', '/signals/ticket')).toBe(404);

    deployment.serveSignals();

    const ticket = ticketFrom(deployment);
    const first = new OpenedConnection(ticket);
    const second = new OpenedConnection(ticket);

    deployment.connect(first);
    first.say(handshake);
    first.say(`${JSON.stringify({ type: 6 })}${recordSeparator}`);
    deployment.connect(second);

    expect(first.received).toStrictEqual([{}, { type: 6 }]);
    expect(second.closed).toBe(true);
    expect(deployment.channelsOpen()).toBe(1);
});

test('closes the channel when it stops answering and opens none, and opens one again once it answers', () => {
    const { deployment, connection } = connected();
    const ticket = ticketFrom(deployment);

    deployment.stopAnswering();

    const refused = new OpenedConnection(ticket);

    deployment.connect(refused);

    expect(connection.closed).toBe(true);
    expect(refused.closed).toBe(true);
    expect(deployment.channelsOpen()).toBe(0);

    deployment.answerAgain();

    const reopened = new OpenedConnection(ticketFrom(deployment));

    deployment.connect(reopened);
    reopened.say(handshake);

    expect(deployment.channelsOpen()).toBe(1);
});

test('lists mail it delivered at the head of the inbox, moves both counts, and says it arrived', () => {
    const { deployment, connection } = connected();
    const before = counts(deployment, 'INBOX');

    deployment.deliver();

    expect(connection.received.at(-1)).toStrictEqual({
        type: 1,
        target: 'signal',
        arguments: [{ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 1 }],
    });
    expect(listed(deployment, 'folder=INBOX').slice(0, 2)).toStrictEqual([mail.arrivingRow.id, 'message-0']);
    expect(listed(deployment, 'folder=INBOX')).toHaveLength(mail.rowsPerPage);
    expect(listed(deployment, 'folder=INBOX&cursor=99')[0]).toBe('message-99');
    expect(counts(deployment, 'INBOX')).toStrictEqual({ stored: before.stored + 1, unread: before.unread + 1 });
});

test('answers every read after a change made elsewhere with that change, and says it in the shape asked for', () => {
    const { deployment, connection } = connected();
    const before = counts(deployment, 'INBOX');

    deployment.flagElsewhere('message-8', { flagged: true });

    expect(connection.received.at(-1)).toMatchObject({
        arguments: [
            {
                kind: 'mail.flags.changed',
                folder: 'INBOX',
                flags: [{ email: 'message-8', isSeen: null, isFlagged: true }],
            },
        ],
    });

    deployment.changeElsewhere('message-12', { seen: true });

    expect(connection.received.at(-1)).toMatchObject({
        arguments: [{ kind: 'mail.changed', folder: 'INBOX', emails: ['message-12'] }],
    });
    expect(ask(deployment, 'GET', '/messages/message-8')).toMatchObject({ flagged: true });
    expect(ask(deployment, 'GET', '/messages/message-12')).toMatchObject({ unread: false });
    expect(listed(deployment, 'folder=INBOX&flagged=true')).toStrictEqual(['message-8']);
    expect(counts(deployment, 'INBOX')).toStrictEqual({ stored: before.stored, unread: before.unread - 1 });
});

test('answers the folders and the accounts with what moved elsewhere, and says which of the two moved', () => {
    const { deployment, connection } = connected();

    deployment.makeFolderElsewhere('Receipts 2026');

    expect(connection.received.at(-1)).toMatchObject({ arguments: [{ kind: 'folders.changed', account: 'work' }] });
    expect(counts(deployment, 'RECEIPTS 2026')).toStrictEqual({ stored: 0, unread: 0 });

    deployment.troubleTheAccounts();

    expect(connection.received.at(-1)).toMatchObject({ arguments: [{ kind: 'account.state', account: 'club' }] });
    expect(ask(deployment, 'GET', '/accounts')).toMatchObject({
        accounts: [{ id: 'work' }, { id: 'club', synchronizationState: 'Failing' }, { id: 'personal' }],
    });
});

test('writes a refused change down as one the account stopped retrying and leaves the mailbox as it was, once', () => {
    const deployment = new FakeDeployment('0.0.0');
    const before = counts(deployment, 'INBOX');

    deployment.refuseNextChange();

    const refused = ask(deployment, 'POST', '/mutations/flags', {
        changes: [{ storedEmailId: 'message-12', flags: { seen: true } }],
    }) as { results: { outcome: string; changes: { recordId: string; state: string }[] }[] };
    const recordId = refused.results[0]?.changes[0]?.recordId ?? '';

    expect(refused.results[0]).toMatchObject({ outcome: 'recorded', changes: [{ state: 'pending' }] });
    expect(ask(deployment, 'GET', `/mutations?record=${recordId}`)).toMatchObject({
        changes: [{ recordId, state: 'dead-lettered', outcomeUnknown: false }],
    });
    expect(counts(deployment, 'INBOX')).toStrictEqual(before);

    deployment.refuseNextChange();
    ask(deployment, 'POST', '/mutations/moves', {
        moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }],
    });

    expect(listed(deployment, 'folder=INBOX')).toContain('message-4');
    expect(listed(deployment, 'folder=ARCHIVE%2F2024')).toStrictEqual([]);

    ask(deployment, 'POST', '/mutations/flags', {
        changes: [{ storedEmailId: 'message-12', flags: { seen: true } }],
    });

    expect(counts(deployment, 'INBOX')).toStrictEqual({ stored: before.stored, unread: before.unread - 1 });
});
