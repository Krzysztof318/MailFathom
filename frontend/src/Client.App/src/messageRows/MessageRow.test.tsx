// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
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

function drawRow(note?: string): HTMLElement {
    render(
        <LocalizationProvider>
            <ul>
                <MessageRow
                    email={email}
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
        </LocalizationProvider>,
    );

    return screen.getByRole('option');
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
});
