// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
    MailThreadMessage,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadMarkingContext, nothingMarkedRead, type ReadMarking } from '../readMarking/useReadMarking';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
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

function message(overrides: Partial<MailThreadMessage['email']> = {}): MailThreadMessage {
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
            toAddresses: ['owner@example.invalid'],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 1_024,
            preview: 'The figures you asked for are attached.',
            ...overrides,
        },
    };
}

function drawing(
    held: MailThreadMessage = message(),
    handlers: {
        readonly onOpenOnItsOwn?: () => void;
        readonly onRegion?: (element: HTMLElement | null) => void;
    } = {},
    marking: ReadMarking = nothingMarkedRead,
    mark: ArrivalMark | null = null,
): void {
    render(
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <ReadMarkingContext value={marking}>
                    <ul>
                        <ThreadMessage
                            session={session}
                            transport={answersNothing}
                            message={held}
                            mark={mark}
                            onOpenOnItsOwn={handlers.onOpenOnItsOwn ?? (() => undefined)}
                            onRegion={handlers.onRegion ?? (() => undefined)}
                        />
                    </ul>
                </ReadMarkingContext>
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

/** What a client that has marked exactly this message read carries, which is what the head reads its state through. */
function marked(storedEmailId: string): ReadMarking {
    return {
        marked: new Map([[storedEmailId, { account: 'work', folder: 'Sent' }]]),
        markRead: () => undefined,
    };
}

describe('ThreadMessage', () => {
    it('draws a message as who wrote it, reads what it says, and names where in the mailbox it stands', () => {
        drawing();

        expect(screen.getByText('The auditor')).toBeDefined();
        expect(screen.getByText('In work, Sent')).toBeDefined();
        expect(asked.some((request) => request.path.includes('/messages/a-message/body'))).toBe(true);
    });

    it('draws no contribution line, which the body below it would be saying twice on one screen', () => {
        drawing();

        expect(screen.queryByText('The figures you asked for are attached.')).toBeNull();
    });

    it('carries no card, which the design project draws on a collapsed message alone', () => {
        drawing();

        const region = screen.getByRole('article');

        expect(region.className).not.toContain('border');
        expect(region.className).not.toContain('bg-panel');
    });

    it('names the region it puts a reader in, so arriving at a message announces more than a tag', () => {
        drawing();

        expect(screen.getByRole('article', { name: 'Message from The auditor' })).toBeDefined();
    });

    it('hands out the element a conversation places the reader on', () => {
        const onRegion = vi.fn();
        drawing(message(), { onRegion });

        expect(onRegion).toHaveBeenCalledWith(screen.getByRole('article'));
    });

    it('names a message nobody wrote a sender for by something rather than by nothing', () => {
        drawing(message({ senderDisplayName: null, senderAddress: null }));

        expect(screen.getByText('No sender')).toBeDefined();
    });

    it('names a message whose sender wrote no display name by the address they wrote from', () => {
        drawing(message({ senderDisplayName: null }));

        expect(screen.getByText('auditor@example.invalid')).toBeDefined();
    });

    it('marks a message nobody has read', () => {
        drawing(message({ unread: true }));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    // The list's row and this head are the same message in two places, so a reader who opened it here would otherwise
    // find it still unread there.
    it('draws a message this client has marked read as read, though the deployment still reports it unread', () => {
        drawing(message({ unread: true }), {}, marked('a-message'));

        expect(screen.queryByText('Unread')).toBeNull();
    });

    it('recognises a sender by their initials, which is what a conversation of several people is scanned down', () => {
        drawing();

        expect(screen.getByText('TA')).toBeDefined();
    });

    it('takes the one initial a sender who wrote a single-word name offers, rather than two', () => {
        drawing(message({ senderDisplayName: 'Prince' }));

        expect(screen.getByText('P')).toBeDefined();
    });

    it('takes the initials from the address where the sender wrote no name', () => {
        drawing(message({ senderDisplayName: null }));

        expect(screen.getByText('A')).toBeDefined();
    });

    it('invents no initials for a sender this deployment could not name', () => {
        drawing(message({ senderDisplayName: null, senderAddress: null }));

        expect(screen.queryByText('NS')).toBeNull();
    });

    it('says a message carries files, which is what a reader looks for on its head', () => {
        drawing(message({ hasAttachments: true, attachmentCount: 2 }));

        expect(screen.getByText('2 attached')).toBeDefined();
    });

    it('offers the way to the message on its own, where everything a conversation does not draw is', () => {
        const onOpenOnItsOwn = vi.fn();
        drawing(message(), { onOpenOnItsOwn });

        fireEvent.click(screen.getByRole('button', { name: 'Open this message on its own' }));

        expect(onOpenOnItsOwn).toHaveBeenCalled();
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
