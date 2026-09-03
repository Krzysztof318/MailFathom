// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadMarkingContext, nothingMarkedRead, type MarkedIn, type ReadMarking } from '../readMarking/useReadMarking';
import { MessageRow } from './MessageRow';

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
    toAddresses: ['owner@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
};

function drawRow(note?: string, unread = false, marking: ReadMarking = nothingMarkedRead): HTMLElement {
    render(
        <LocalizationProvider>
            <ReadMarkingContext value={marking}>
                <ul>
                    <MessageRow
                        email={{ ...email, unread }}
                        position={1}
                        open={false}
                        selected={false}
                        focusable
                        note={note}
                        onOpen={() => undefined}
                        onPoint={() => undefined}
                        onPointerEnter={() => undefined}
                        onElement={() => undefined}
                    />
                </ul>
            </ReadMarkingContext>
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
}

/** What a client that has marked exactly this message read carries, which is what a row reads its state through. */
function marked(storedEmailId: string, place: MarkedIn = { account: 'work', folder: 'INBOX' }): ReadMarking {
    return { marked: new Map([[storedEmailId, place]]), markRead: () => undefined };
}

// The line the row's height reserves whether or not anything is in it, which is what lets the search's row and the
// folder's row be one row. Whether it is announced is the whole of what the two cases differ by, and nothing a reader
// sees says which happened — so the attribute is what is asserted here.
describe('MessageRow', () => {
    it('announces the reserved line when the row has something to say about the message', () => {
        const reserved = drawRow('Found by what it means.').lastElementChild;

        expect(reserved?.textContent).toBe('Found by what it means.');
        expect(reserved?.getAttribute('aria-hidden')).toBeNull();
    });

    it('keeps the reserved line out of the accessibility tree when the row has nothing to say', () => {
        const reserved = drawRow().lastElementChild;

        expect(reserved?.textContent).toBe('');
        expect(reserved?.getAttribute('aria-hidden')).toBe('true');
    });

    // Unread is drawn as weight and colour, which a test cannot read, and said in as many words for somebody who is
    // looking at neither — so the words are what says whether the row drew the message read.
    it('says a message the deployment reports as unread is unread', () => {
        drawRow(undefined, true);

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('says nothing of the sort for a message the deployment reports as read', () => {
        drawRow();

        expect(screen.queryByText('Unread')).toBeNull();
    });

    // The row draws from the pending mutation rather than waiting for the account's own pass to observe the flag,
    // which is the whole of what a folder's count and its rows have to agree about.
    it('draws a message this client has marked read as read, though the deployment still reports it unread', () => {
        drawRow(undefined, true, marked(email.id));

        expect(screen.queryByText('Unread')).toBeNull();
    });

    it('leaves a message another one’s marking has nothing to do with unread', () => {
        drawRow(undefined, true, marked('another-message'));

        expect(screen.getByText('Unread')).toBeDefined();
    });
});
