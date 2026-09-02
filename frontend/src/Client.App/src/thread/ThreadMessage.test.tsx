// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
    MailThreadMessage,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { ThreadMessage } from './ThreadMessage';

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
    expanded: boolean,
    held: MailThreadMessage = message(),
    handlers: {
        readonly onExpanded?: (expanded: boolean) => void;
        readonly onOpenOnItsOwn?: () => void;
    } = {},
): void {
    render(
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <ul>
                    <ThreadMessage
                        session={session}
                        transport={answersNothing}
                        message={held}
                        expanded={expanded}
                        onExpanded={handlers.onExpanded ?? (() => undefined)}
                        onOpenOnItsOwn={handlers.onOpenOnItsOwn ?? (() => undefined)}
                        onSummary={() => undefined}
                    />
                </ul>
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('ThreadMessage', () => {
    it('draws a collapsed message as who wrote it and what it added', () => {
        drawing(false);

        expect(screen.getByText('The auditor')).toBeDefined();
        expect(screen.getByText('The figures you asked for are attached.')).toBeDefined();
    });

    it('asks the deployment for nothing while the message is collapsed', () => {
        drawing(false);

        expect(asked).toEqual([]);
    });

    it('reads the message once it is open, and says where in the mailbox it stands', () => {
        drawing(true);

        expect(screen.getByText('In work, Sent')).toBeDefined();
        expect(asked.some((request) => request.path.includes('/messages/a-message/body'))).toBe(true);
    });

    it('draws the contribution only while the message is collapsed, rather than twice on one screen', () => {
        drawing(true);

        expect(screen.queryByText('The figures you asked for are attached.')).toBeNull();
    });

    it('says what a message added is not extracted rather than drawing an empty line', () => {
        drawing(false, message({ preview: null }));

        expect(screen.getByText('What this message added has not been read into this deployment yet.')).toBeDefined();
    });

    it('names a message nobody wrote a sender for by something rather than by nothing', () => {
        drawing(false, message({ senderDisplayName: null, senderAddress: null }));

        expect(screen.getByText('No sender')).toBeDefined();
    });

    it('names a message whose sender wrote no display name by the address they wrote from', () => {
        drawing(false, message({ senderDisplayName: null }));

        expect(screen.getByText('auditor@example.invalid')).toBeDefined();
    });

    it('marks a message nobody has read', () => {
        drawing(false, message({ unread: true }));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('reports the expansion a reader asked for, without deciding it here', async () => {
        const onExpanded = vi.fn();
        drawing(false, message(), { onExpanded });

        const summary = screen.getByText('The auditor').closest('summary');

        expect(summary).not.toBeNull();
        fireEvent.click(summary ?? screen.getByText('The auditor'));

        // The browser toggles the disclosure and reports it afterwards, so what this waits on is the report rather
        // than the click.
        await waitFor(() => {
            expect(onExpanded).toHaveBeenCalledWith(true);
        });
    });

    it('recognises a sender by their initials, which is what a conversation of several people is scanned down', () => {
        drawing(false);

        expect(screen.getByText('TA')).toBeDefined();
    });

    it('takes the one initial a sender who wrote a single-word name offers, rather than two', () => {
        drawing(false, message({ senderDisplayName: 'Prince' }));

        expect(screen.getByText('P')).toBeDefined();
    });

    it('takes the initials from the address where the sender wrote no name', () => {
        drawing(false, message({ senderDisplayName: null }));

        expect(screen.getByText('A')).toBeDefined();
    });

    it('invents no initials for a sender this deployment could not name', () => {
        drawing(false, message({ senderDisplayName: null, senderAddress: null }));

        expect(screen.queryByText('NS')).toBeNull();
    });

    it('says a collapsed message carries files, which is what a reader opens it for', () => {
        drawing(false, message({ hasAttachments: true, attachmentCount: 2 }));

        expect(screen.getByText('2 attached')).toBeDefined();
    });

    it('offers the way to the message on its own, where everything a conversation does not draw is', () => {
        const onOpenOnItsOwn = vi.fn();
        drawing(true, message(), { onOpenOnItsOwn });

        fireEvent.click(screen.getByRole('button', { name: 'Open this message on its own' }));

        expect(onOpenOnItsOwn).toHaveBeenCalled();
    });
});
