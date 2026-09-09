// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailBody,
    MailFathomTransport,
    MailMessage,
    MailThreadMessage,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadMarkingContext, nothingMarkedRead, type ReadMarking } from '../readMarking/useReadMarking';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { WorkspaceProvider } from '../workspace/Workspace';
import { ThreadMessage } from './ThreadMessage';
import type { ArrivalMark } from './threadOpening';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asked: ClientRequest[] = [];

// The record is one per file rather than one per test, so what one test asked for would be what the next one read.
beforeEach(() => {
    asked.length = 0;
});

const answersNothing: MailFathomTransport = (request) => {
    asked.push(request);

    return new Promise<ClientResponse>(() => undefined);
};

/** The message as the conversation route described it, which is what everything but the words is drawn from. */
const described: MailMessage = {
    storedEmailId: 'a-message',
    account: 'work',
    folder: 'Sent',
    threadId: 'a-conversation',
    sizeOctets: 1_024,
    headers: {
        subject: 'The quarterly figures',
        sentAt: '2026-08-31T09:40:00+00:00',
        receivedAt: '2026-08-31T09:41:00+00:00',
        participants: [{ role: 'From', address: 'auditor@example.invalid', displayName: 'The auditor' }],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    body: { availability: 'Readable', plainText: true, html: true },
    sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
    attachments: [],
    carried: null,
    unread: false,
    flagged: false,
    answered: false,
};

/** The words the same answer carried, which is what makes drawing this message cost nothing on the wire. */
const said: MailBody = {
    storedEmailId: 'a-message',
    availability: 'Readable',
    plainText: { text: 'The figures you asked for are attached.', originalCharacterCount: 38, truncation: 'None' },
    document: null,
    selfContainedHtml: null,
    remoteImagesRequested: false,
};

function message(overrides: Partial<MailThreadMessage> = {}): MailThreadMessage {
    return {
        position: 1,
        answeredId: 'the-one-before',
        email: {
            id: 'a-message',
            account: 'work',
            folder: 'Sent',
            threadId: 'a-conversation',
            subject: 'The quarterly figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: '2026-08-31T09:40:00+00:00',
            senderAddress: 'auditor@example.invalid',
            senderDisplayName: 'The auditor',
            toAddresses: ['user@example.invalid'],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 1_024,
            preview: 'The figures you asked for are attached.',
            enrichment: null,
            threadMessageCount: null,
        },
        message: described,
        body: said,
        ...overrides,
    };
}

/** The same message with nothing the deployment could open, which is the gap a correspondence is still drawn with. */
function unopened(): MailThreadMessage {
    return message({ message: null, body: null });
}

function drawing(
    held: MailThreadMessage = message(),
    handlers: {
        readonly onOpenOnItsOwn?: () => void;
        readonly onShowFullHtml?: () => void;
        readonly onRegion?: (element: HTMLElement | null) => void;
    } = {},
    marking: ReadMarking = nothingMarkedRead,
    mark: ArrivalMark | null = null,
): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <ReadMarkingContext value={marking}>
                        <ul>
                            <ThreadMessage
                                session={session}
                                transport={answersNothing}
                                message={held}
                                mark={mark}
                                online
                                onOpenOnItsOwn={handlers.onOpenOnItsOwn ?? (() => undefined)}
                                onShowFullHtml={handlers.onShowFullHtml ?? (() => undefined)}
                                onRegion={handlers.onRegion ?? (() => undefined)}
                            />
                        </ul>
                    </ReadMarkingContext>
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

/** A marking that records what was opened, so what a drawn message reports is asserted rather than a call count. */
function recordingMarkings(): { readonly marking: ReadMarking; readonly opened: string[] } {
    const opened: string[] = [];

    return {
        marking: {
            marked: new Map(),
            markRead: (message) => {
                opened.push(message.storedEmailId);
            },
        },
        opened,
    };
}

describe('ThreadMessage', () => {
    it('draws the message out in full, as the surface reading one on its own draws it', () => {
        drawing();

        expect(screen.getByText('The auditor')).toBeDefined();
        expect(screen.getByText('The figures you asked for are attached.')).toBeDefined();
        expect(screen.getByText('In work, Sent')).toBeDefined();
    });

    // The conversation is one document and every message in it is written out, so there is no control here that would
    // fold one away: what a reader decides is how much of the correspondence stands in front of them, and that is the
    // conversation's own decision rather than each message's.
    it('offers nothing that would collapse the message it draws', () => {
        drawing();

        expect(screen.queryByRole('button', { expanded: true })).toBeNull();
        expect(screen.queryByRole('button', { expanded: false })).toBeNull();
    });

    // The words arrived with the conversation, which is the whole of what makes revealing the earlier messages free.
    it('reads nothing from the deployment for a message the conversation carried', () => {
        drawing();

        expect(asked).toEqual([]);
    });

    // A message whose local copy the deployment could not open arrives without one. The reader is owed the gap rather
    // than a message drawn empty, and the way to it is the control that opens the message on its own.
    it('says a message the conversation could not carry, rather than drawing it empty', () => {
        drawing(unopened());

        expect(
            screen.getByText('The message from The auditor could not be read here. Open it on its own to read it.'),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Open this message on its own' })).toBeDefined();
    });

    it('names a message nobody wrote a sender for by something rather than by nothing', () => {
        const held = unopened();

        drawing({ ...held, email: { ...held.email, senderDisplayName: null, senderAddress: null } });

        expect(
            screen.getByText('The message from No sender could not be read here. Open it on its own to read it.'),
        ).toBeDefined();
    });

    it('names the region it puts a reader in, so arriving at a message announces more than a tag', () => {
        drawing();

        expect(screen.getByRole('article', { name: 'Message from The auditor' })).toBeDefined();
    });

    it('hands out the element a conversation places the reader on', () => {
        const onRegion = vi.fn();
        drawing(message(), { onRegion });

        expect(onRegion).toHaveBeenCalledWith(screen.getByRole('article', { name: 'Message from The auditor' }));
    });

    // A message read inside its conversation was read, which is one rule wherever a message is drawn.
    it('marks the message read, the words it drew being what opening it means', () => {
        const { marking, opened } = recordingMarkings();

        drawing(message(), {}, marking);

        expect(opened).toEqual(['a-message']);
    });

    it('marks nothing read for a message it drew no words for', () => {
        const { marking, opened } = recordingMarkings();

        drawing(unopened(), {}, marking);

        expect(opened).toEqual([]);
    });

    it('offers the way to the message on its own, where everything a conversation does not draw is', () => {
        const onOpenOnItsOwn = vi.fn();
        drawing(message(), { onOpenOnItsOwn });

        fireEvent.click(screen.getByRole('button', { name: 'Open this message on its own' }));

        expect(onOpenOnItsOwn).toHaveBeenCalled();
    });

    it('offers the sender own markup, which is the one ask a drawn message still makes of its surface', () => {
        const onShowFullHtml = vi.fn();
        drawing(message(), { onShowFullHtml });

        fireEvent.click(screen.getByRole('button', { name: 'Show the full HTML version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the HTML' }));

        expect(onShowFullHtml).toHaveBeenCalled();
    });

    it('says in words that this is the message somebody opened, rather than only drawing a rule beside it', () => {
        drawing(message(), {}, nothingMarkedRead, 'list');

        expect(screen.getByText('Opened from the list')).toBeDefined();
    });

    it('says in words that a search result is what brought somebody to this message', () => {
        drawing(message(), {}, nothingMarkedRead, 'result');

        expect(screen.getByText('Brought here from a search result')).toBeDefined();
    });

    it('says neither of those about a message the conversation only holds', () => {
        drawing();

        expect(screen.queryByText('Opened from the list')).toBeNull();
        expect(screen.queryByText('Brought here from a search result')).toBeNull();
    });
});
