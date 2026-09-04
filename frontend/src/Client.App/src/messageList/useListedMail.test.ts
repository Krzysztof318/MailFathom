// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
import { actedMessages, nothingListed, type ListedMail } from './useListedMail';

const drawn: readonly ActedMessage[] = [
    { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox' },
    { storedEmailId: 'message-2', account: 'home', folder: 'home-inbox' },
];

const listing: ListedMail = {
    ...nothingListed,
    placeOf: (storedEmailId) => drawn.find((message) => message.storedEmailId === storedEmailId) ?? null,
};

// The workspace keeps a selection as identities alone, which is what lets it outlive the pages the list scrolled away
// from — and an act on one has to name the account it is in and the folder it is leaving. This is the join.
describe('actedMessages', () => {
    it('names each picked-out message the list has drawn, in the order they were picked', () => {
        expect(actedMessages(listing, ['message-2', 'message-1'])).toStrictEqual([drawn[1], drawn[0]]);
    });

    it('leaves out what no list has drawn, so an act is never asked about a message it cannot place', () => {
        expect(actedMessages(listing, ['message-1', 'message-nobody-drew'])).toStrictEqual([drawn[0]]);
    });

    it('names nothing at all where no list is on the screen', () => {
        expect(actedMessages(nothingListed, ['message-1'])).toStrictEqual([]);
    });
});
