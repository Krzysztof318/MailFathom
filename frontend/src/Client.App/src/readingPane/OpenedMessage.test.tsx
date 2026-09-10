// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailBody, MailMessage } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { EmbeddedHtmlMessagesContext } from '../preferences/messageView';
import { ReadMarkingContext, nothingMarkedRead, type ReadMarking } from '../readMarking/useReadMarking';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { WorkspaceProvider } from '../workspace/Workspace';
import type { MessageBodyRead } from '../messageBody/useMessageBody';
import { OpenedMessage } from './OpenedMessage';

// The drawing one message takes wherever it is drawn. What is asserted here is that it is one drawing rather than two:
// the surface reading a message on its own and the conversation reading it inside a correspondence hand this the same
// two values and get the same thing back, so a difference between the two screens cannot be introduced in one of them.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedEmailId = '00000000-0000-4000-8000-000000000000';

const described: MailMessage = {
    storedEmailId,
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    sizeOctets: 40_960,
    headers: {
        subject: 'Quarterly invoice',
        sentAt: '2026-08-31T09:41:00+00:00',
        receivedAt: '2026-08-31T09:41:10+00:00',
        participants: [{ role: 'From', address: 'billing@example.invalid', displayName: 'Billing' }],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    body: { availability: 'Readable', plainText: true, html: true },
    sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
    attachments: [],
    carried: null,
    unread: true,
    flagged: false,
    answered: false,
};

const said: MailBody = {
    storedEmailId,
    availability: 'Readable',
    plainText: { text: 'The invoice is attached.', originalCharacterCount: 24, truncation: 'None' },
    document: null,
    selfContainedHtml: null,
    remoteImagesRequested: false,
};

/** A read that has already answered, which is the state both surfaces hand this component in the ordinary case. */
const read: MessageBodyRead = {
    drawn: { outcome: 'read', value: said },
    reading: false,
    askingForPictures: false,
    askedForPictures: false,
    embeddedHtml: false,
    readAgain: () => undefined,
    showRemotePictures: () => undefined,
    showWithoutRemotePictures: () => undefined,
};

function drawing(
    message: MailMessage = described,
    body: MessageBodyRead = read,
    marking: ReadMarking = nothingMarkedRead,
    embeddedHtml = false,
    onShowFullHtml: () => void = () => undefined,
): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <EmbeddedHtmlMessagesContext value={embeddedHtml}>
                        <ReadMarkingContext value={marking}>
                            <OpenedMessage
                                session={session}
                                message={message}
                                body={body}
                                onShowFullHtml={onShowFullHtml}
                            />
                        </ReadMarkingContext>
                    </EmbeddedHtmlMessagesContext>
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('OpenedMessage', () => {
    it('draws who wrote the message and what it says', () => {
        drawing();

        expect(screen.getByText('Billing')).toBeDefined();
        expect(screen.getByText('The invoice is attached.')).toBeDefined();
    });

    it('names an author the message wrote nobody for, rather than drawing an empty line', () => {
        drawing({ ...described, headers: { ...described.headers, participants: [] } });

        expect(screen.getByText('This message names nobody as its author.')).toBeDefined();
    });

    // ADR 0026: the body being drawn is what opening a message means, and it is one rule wherever a message is drawn.
    it('marks the message read, and says where in the mailbox it stands', () => {
        const opened: { storedEmailId: string; account: string; folder: string; unread: boolean }[] = [];

        drawing(described, read, {
            marked: new Map(),
            markRead: (message) => {
                opened.push(message);
            },
        });

        expect(opened).toStrictEqual([{ storedEmailId, account: 'work', folder: 'INBOX', unread: true }]);
    });

    it('offers the sender own markup, which is the ask a reader makes about one message', () => {
        const onShowFullHtml = vi.fn();

        drawing(described, read, nothingMarkedRead, false, onShowFullHtml);
        fireEvent.click(screen.getByRole('button', { name: 'Show the original message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the original' }));

        expect(onShowFullHtml).toHaveBeenCalled();
    });

    // With the embedded view chosen the markup is already under this line, so a control offering to open it would open
    // a second copy of what is being read.
    it('offers no way to the markup where the reader is already reading it', () => {
        drawing(described, read, nothingMarkedRead, true);

        expect(screen.queryByRole('button', { name: 'Show the original message' })).toBeNull();
    });

    // The screen's own head is the caller's: a conversation says its subject once above every message in it, and a
    // message read on its own says it above that one.
    it('draws no subject of its own, that being the surface head each caller draws', () => {
        drawing();

        expect(screen.queryByRole('heading', { name: 'Quarterly invoice' })).toBeNull();
    });
});
