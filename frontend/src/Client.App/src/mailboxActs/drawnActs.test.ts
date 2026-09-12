// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { nothingMarkedRead, type MarkedIn, type ReadMarking } from '../readMarking/useReadMarking';
import { flagActFor, readActFor, underway } from './drawnActs';
import { nothingActed, type ActedMessage, type AskedAct, type MailboxActs } from './useMailboxActs';

const inbox = { account: 'work', folder: 'work-inbox' };

function message(storedEmailId: string, unread: boolean, flagged = false): ActedMessage {
    return { storedEmailId, ...inbox, unread, flagged };
}

/** What a client that has marked exactly these messages read carries. */
function marked(...storedEmailIds: readonly string[]): ReadMarking {
    const place: MarkedIn = inbox;

    return { marked: new Map(storedEmailIds.map((id) => [id, place])), markRead: () => undefined };
}

/** What a client holding exactly these acts carries, which is what a second press reads the first press's state from. */
function asking(...asked: readonly (readonly [string, AskedAct['act']])[]): MailboxActs {
    return {
        ...nothingActed,
        asked: new Map(asked.map(([id, act]) => [id, { act, from: inbox.folder, leaves: false, destroys: false }])),
    };
}

describe('flagActFor', () => {
    it('offers to take the flag off where every message is flagged, which is the one state that turns it round', () => {
        const flagged = [message('message-1', false, true), message('message-2', false, true)];

        expect(flagActFor(nothingActed, flagged)).toBe('unflag');
    });

    it('offers to put a flag on where no message carries one', () => {
        const plain = [message('message-1', false), message('message-2', false)];

        expect(flagActFor(nothingActed, plain)).toBe('flag');
    });

    it('offers to put a flag on where the messages are a mixture of the two, which is the default', () => {
        const mixed = [message('message-1', false, true), message('message-2', false)];

        expect(flagActFor(nothingActed, mixed)).toBe('flag');
    });

    it('reads one message the same way it reads several, which is what the head of a message reads', () => {
        expect(flagActFor(nothingActed, [message('message-1', false, true)])).toBe('unflag');
        expect(flagActFor(nothingActed, [message('message-1', false)])).toBe('flag');
    });

    // Which is what makes pressing the control twice give a reader both directions rather than the same one twice: a
    // mailbox mutation converges minutes after it is written down, so the deployment goes on reporting the old state.
    it('reads a message asked to be flagged as flagged, so the next press offers to take it off', () => {
        expect(flagActFor(asking(['message-1', 'flag']), [message('message-1', false)])).toBe('unflag');
    });

    it('reads a message asked to be unflagged as unflagged, which is the same rule in the other direction', () => {
        expect(flagActFor(asking(['message-1', 'unflag']), [message('message-1', false, true)])).toBe('flag');
    });

    // A control over nothing is refused before it can be pressed, so what this decides is only the name it wears while
    // it says so — and a strip whose wording changed as a selection was cleared would be reading as a second act.
    it('offers to put a flag on where nothing is picked out, rather than reading an empty list as all flagged', () => {
        expect(flagActFor(nothingActed, [])).toBe('flag');
    });
});

describe('readActFor', () => {
    it('offers to mark unread where every message is read, which is the one state that turns it round', () => {
        const read = [message('message-1', false), message('message-2', false)];

        expect(readActFor(nothingActed, nothingMarkedRead, read)).toBe('markUnread');
    });

    it('offers to mark read where every message is unread', () => {
        const unread = [message('message-1', true), message('message-2', true)];

        expect(readActFor(nothingActed, nothingMarkedRead, unread)).toBe('markRead');
    });

    it('offers to mark read where the messages are a mixture of the two, which is the default', () => {
        const mixed = [message('message-1', false), message('message-2', true)];

        expect(readActFor(nothingActed, nothingMarkedRead, mixed)).toBe('markRead');
    });

    it('reads one message the same way it reads several', () => {
        expect(readActFor(nothingActed, nothingMarkedRead, [message('message-1', true)])).toBe('markRead');
        expect(readActFor(nothingActed, nothingMarkedRead, [message('message-1', false)])).toBe('markUnread');
    });

    // What the deployment last reported and what the reader is looking at differ for minutes at a time: a message whose
    // body has been drawn is marked read by this client while the stored flag still says otherwise. Offering to mark it
    // read again would be offering to do what has been done.
    it('reads a message this client has marked read as read, whatever the deployment last reported', () => {
        const opened = [message('message-1', true)];

        expect(readActFor(nothingActed, marked('message-1'), opened)).toBe('markUnread');
    });

    // Which is what makes pressing the control twice give a reader both directions rather than the same one twice.
    it('reads a message asked to be marked unread as unread, so the next press offers to mark it read', () => {
        const acted = [message('message-1', false)];

        expect(readActFor(asking(['message-1', 'markUnread']), nothingMarkedRead, acted)).toBe('markRead');
    });

    it('reads a message asked to be marked read as read, which is the same rule in the other direction', () => {
        const acted = [message('message-1', true)];

        expect(readActFor(asking(['message-1', 'markRead']), nothingMarkedRead, acted)).toBe('markUnread');
    });

    it('lets an act of its own win over a marking, the act being the later of the two statements', () => {
        const acted = [message('message-1', true)];

        expect(readActFor(asking(['message-1', 'markUnread']), marked('message-1'), acted)).toBe('markRead');
    });

    it('leaves an act about something else out of it, as every other reading of a row does', () => {
        const acted = [message('message-1', false)];

        expect(readActFor(asking(['message-1', 'archive']), nothingMarkedRead, acted)).toBe('markUnread');
    });

    // It is refused before it can be pressed, so what this decides is only the name the control wears while it says so.
    it('reads nothing at all the way it reads messages that are all read, so a cleared selection keeps the name', () => {
        expect(readActFor(nothingActed, nothingMarkedRead, [])).toBe('markUnread');
    });
});

describe('underway', () => {
    /** A message drawn in the folder an act filed it into, which is where the reader meets it next. */
    function arrivedIn(folder: string): ActedMessage {
        return { storedEmailId: 'message-1', account: 'work', folder, unread: false, flagged: false };
    }

    /** What a client that asked for this act, out of this folder, carries. */
    function askedFrom(act: AskedAct['act'], from: string): MailboxActs {
        return {
            ...nothingActed,
            asked: new Map([['message-1', { act, from, leaves: true, destroys: false }]]),
        };
    }

    it('reads an act as under way in the folder it was asked in, which is where the row is still waiting', () => {
        expect(underway(askedFrom('delete', 'work-inbox'), 'delete', [arrivedIn('work-inbox')])).toBe(true);
    });

    // Filing a message in the trash and destroying it there are one act name and two different things, so a client
    // that read the first as the second refused to destroy the one message somebody had just put in front of it —
    // while every other message in that folder, having no act remembered, was destroyed on the first press.
    it('reads no act as under way where the message has arrived somewhere else, the act being finished there', () => {
        expect(underway(askedFrom('delete', 'work-inbox'), 'delete', [arrivedIn('work-trash')])).toBe(false);
    });

    it('reads nothing at all as nothing under way, so a control over an empty selection is refused rather than held', () => {
        expect(underway(askedFrom('archive', 'work-inbox'), 'archive', [])).toBe(false);
    });
});

// The three rules above read a remembered act the way the row itself is drawn, which is what stops a control from
// offering to undo something the row beside it never showed.
describe('an act asked in another folder', () => {
    const inTrash: ActedMessage = {
        storedEmailId: 'message-1',
        account: 'work',
        folder: 'work-trash',
        unread: false,
        flagged: false,
    };

    function askedInTheInbox(act: AskedAct['act']): MailboxActs {
        return {
            ...nothingActed,
            asked: new Map([['message-1', { act, from: 'work-inbox', leaves: true, destroys: false }]]),
        };
    }

    it('leaves the flag control pointing the way the row is drawn, rather than at what another folder was asked', () => {
        expect(flagActFor(askedInTheInbox('flag'), [inTrash])).toBe('flag');
    });

    it('leaves the read control pointing the way the row is drawn, which is the same rule', () => {
        expect(readActFor(askedInTheInbox('markUnread'), nothingMarkedRead, [inTrash])).toBe('markUnread');
    });
});
