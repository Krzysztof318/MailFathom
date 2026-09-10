// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import {
    actLeaving,
    actPending,
    nothingActed,
    opensAsDraft,
    type MailboxAct,
    type MailboxActs,
} from './useMailboxActs';

const email: MailTimelineEntry = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'The quarter is closed',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'writer@nordwind.example',
    senderDisplayName: 'Writer',
    toAddresses: ['user@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
    enrichment: null,
    threadMessageCount: null,
};

function asking(
    act: MailboxAct,
    storedEmailId = email.id,
    from = email.folder,
    leaves = act === 'archive' || act === 'move' || act === 'delete',
): MailboxActs {
    return { ...nothingActed, asked: new Map([[storedEmailId, { act, from, leaves }]]) };
}

// What retires a pending act, which is the whole of why this client polls nothing: an act writes a record and the
// account's own pass issues the mail-server command later, so what says it has landed is the deployment reporting the
// row differently — never a timer and never a second read this client asked for.
describe('actPending', () => {
    it('says nothing about a message nothing was asked of', () => {
        expect(actPending(nothingActed, email)).toBeNull();
    });

    it('says nothing about a message another one’s act was asked of', () => {
        expect(actPending(asking('archive', 'another-message'), email)).toBeNull();
    });

    it.each(['archive', 'delete', 'move'] as const)(
        'keeps saying a message is being %sd until the folder it is leaving stops listing it',
        (act) => {
            expect(actPending(asking(act), email)?.act).toBe(act);
        },
    );

    it('stops saying a message is being flagged once the deployment reports it flagged', () => {
        expect(actPending(asking('flag'), email)?.act).toBe('flag');
        expect(actPending(asking('flag'), { ...email, flagged: true })).toBeNull();
    });

    it('stops saying a flag is being taken off once the deployment reports the message unflagged', () => {
        expect(actPending(asking('unflag'), { ...email, flagged: true })?.act).toBe('unflag');
        expect(actPending(asking('unflag'), email)).toBeNull();
    });

    it('stops saying a message is being marked unread once the deployment reports it unread', () => {
        expect(actPending(asking('markUnread'), email)?.act).toBe('markUnread');
        expect(actPending(asking('markUnread'), { ...email, unread: true })).toBeNull();
    });

    // An act is about a message in a place, so the sentence belongs to the place it was asked from: the same message
    // drawn in the folder it was filed into has arrived rather than being on its way.
    it('says nothing about the same message drawn in the folder the act filed it into', () => {
        expect(actPending(asking('move'), { ...email, folder: 'Archive' })).toBeNull();
    });
});

// What the list acts on rather than what a row says: an act a person performs is theirs, so the message goes from the
// folder it is leaving at the press instead of when a mailbox is next seen to agree.
describe('actLeaving', () => {
    it('says nothing about a message nothing was asked of', () => {
        expect(actLeaving(nothingActed, email)).toBe(false);
    });

    it('says a message asked to be filed elsewhere is leaving the folder it was asked in', () => {
        expect(actLeaving(asking('archive'), email)).toBe(true);
    });

    it('says nothing about that message once it is drawn in the folder it went to', () => {
        expect(actLeaving(asking('archive'), { ...email, folder: 'Archive' })).toBe(false);
    });

    it('says a delete that destroys the mail leaves no list, there being nowhere for the message to go', () => {
        expect(actLeaving(asking('delete', email.id, email.folder, false), email)).toBe(false);
    });

    it('says a flag change leaves no list either, the message staying exactly where it is', () => {
        expect(actLeaving(asking('flag'), email)).toBe(false);
    });
});

// Where a message opens is decided by what its own account calls the folder it is in rather than by what the folder
// is named: a deployment whose drafts folder is `Entwürfe` still opens one in the composer.
describe('opensAsDraft', () => {
    it('says a message in the folder its account labels for drafts is one to carry on writing', () => {
        expect(opensAsDraft({ ...nothingActed, folderRoleOf: () => 'Drafts' }, email)).toBe(true);
    });

    it('says a message in any other folder is one to read', () => {
        expect(opensAsDraft({ ...nothingActed, folderRoleOf: () => 'Inbox' }, email)).toBe(false);
    });

    // A session that may not file mail reads no folders at all, so nothing says which folder is for drafts — and a
    // message opened as mail is the answer that costs a reader nothing.
    it('says a message is one to read where no folder was read at all', () => {
        expect(opensAsDraft(nothingActed, email)).toBe(false);
    });
});
