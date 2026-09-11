// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { nothingMarkedRead, type MarkedIn, type ReadMarking } from '../readMarking/useReadMarking';
import { readActFor } from './drawnActs';
import { nothingActed, type ActedMessage, type AskedAct, type MailboxActs } from './useMailboxActs';

const inbox = { account: 'work', folder: 'work-inbox' };

function message(storedEmailId: string, unread: boolean): ActedMessage {
    return { storedEmailId, ...inbox, unread };
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
        asked: new Map(asked.map(([id, act]) => [id, { act, from: inbox.folder, leaves: false }])),
    };
}

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
