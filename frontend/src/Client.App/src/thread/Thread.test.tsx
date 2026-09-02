// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { WorkspaceProvider } from '../workspace/Workspace';
import type { OpenConversation } from '../workspace/openConversation';
import { Thread } from './Thread';

// The network boundary is the transport and it is the whole of what these tests fake, so the routes the conversation
// asks for, the parsing that reads the answers, and the failure mapping are all under test rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const threadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

const asked: ClientRequest[] = [];

beforeEach(() => {
    asked.length = 0;
});

function row(id: string, overrides: Readonly<Record<string, unknown>> = {}): Readonly<Record<string, unknown>> {
    return {
        id,
        account: 'work',
        folder: 'INBOX',
        threadId,
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
        preview: `What ${id} added.`,
        ...overrides,
    };
}

function pageOf(
    ids: readonly string[],
    overrides: Readonly<Record<string, unknown>> = {},
    rows: Readonly<Record<string, Readonly<Record<string, unknown>>>> = {},
): string {
    return JSON.stringify({
        threadId,
        messages: ids.map((id, at) => ({ position: at, answeredId: null, email: row(id, rows[id] ?? {}) })),
        participants: [
            { address: 'auditor@example.invalid', displayName: 'The auditor', messageCount: 2 },
            { address: 'owner@example.invalid', displayName: null, messageCount: 1 },
        ],
        messageCount: ids.length,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: 100,
        ...overrides,
    });
}

/** One message's whole text, named after the message so a test can tell which of them was read. */
function bodyAsWords(path: string): string {
    const storedEmailId = path.split('/messages/')[1]?.split('/')[0] ?? '';

    return JSON.stringify({
        storedEmailId,
        availability: 'Readable',
        plainText: {
            text: `The whole of what ${storedEmailId} says.`,
            originalCharacterCount: 32,
            truncation: 'None',
        },
        document: null,
        remoteImagesRequested: false,
    });
}

/** A deployment answering the conversation with what a test named, and every message body the same way. */
function deploymentAnswering(...pages: readonly string[]): MailFathomTransport {
    let served = 0;

    return (request) => {
        asked.push(request);

        if (request.path.includes('/body')) {
            return Promise.resolve({ status: 200, body: bodyAsWords(request.path), headers: {} });
        }

        const page = pages[Math.min(served, pages.length - 1)] ?? pageOf([]);
        served += 1;

        return Promise.resolve({ status: 200, body: page, headers: {} });
    };
}

function deploymentRefusing(status: number): MailFathomTransport {
    return (request) => {
        asked.push(request);

        return Promise.resolve({ status, body: '', headers: {} });
    };
}

/** A deployment that has taken the request and not answered it, which is what a surface that waits is proven against. */
const answersNothing: MailFathomTransport = () => new Promise<ClientResponse>(() => undefined);

function inTheFrame(transport: MailFathomTransport, conversation: OpenConversation, online: boolean): ReactElement {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <Thread session={session} transport={transport} conversation={conversation} online={online} />
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

function drawing(
    transport: MailFathomTransport,
    conversation: OpenConversation = { threadId, openAt: null },
    online = true,
) {
    return render(inTheFrame(transport, conversation, online));
}

function bodiesAsked(): string[] {
    return asked.filter((request) => request.path.includes('/body')).map((request) => request.path);
}

describe('Thread', () => {
    it('draws the conversation in order, each message on a line of its own', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])));

        expect(await screen.findByText('What one added.')).toBeDefined();
        expect(screen.getByText('What two added.')).toBeDefined();
        expect(screen.getAllByRole('listitem')).toHaveLength(3);
    });

    it('names the conversation by its first message and says how many messages it holds', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two'])));

        expect(await screen.findByRole('heading', { name: 'The quarterly figures', level: 2 })).toBeDefined();
        expect(screen.getByText('Messages in this conversation: 2')).toBeDefined();
    });

    it('names everybody who wrote from the answer rather than from the messages it happens to hold', async () => {
        drawing(deploymentAnswering(pageOf(['one'])));

        expect(await screen.findByText('Written by The auditor and owner@example.invalid')).toBeDefined();
    });

    it('says a conversation has authors it does not name', async () => {
        drawing(deploymentAnswering(pageOf(['one'], { moreParticipantsNotNamed: true })));

        expect(await screen.findByText('More people wrote in this conversation than are named here.')).toBeDefined();
    });

    it('says a conversation runs past what one read assembles', async () => {
        drawing(deploymentAnswering(pageOf(['one'], { moreMessagesNotAssembled: true })));

        expect(
            await screen.findByText(
                'This conversation is longer than one read assembles, so what is shown is the beginning of it.',
            ),
        ).toBeDefined();
    });

    it('reads no body for a message nobody has opened', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])));
        await screen.findByText('What one added.');

        // The last of them opens, so exactly one body is read out of three messages rather than none or all three.
        await waitFor(() => {
            expect(bodiesAsked()).toHaveLength(1);
        });
        expect(bodiesAsked()[0]).toContain('/messages/three/body');
    });

    it('reads a message the reader opens, and not before', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two'])));
        const summary = await screen.findByText('What one added.');

        expect(bodiesAsked().some((path) => path.includes('/messages/one/'))).toBe(false);

        fireEvent.click(summary);

        await waitFor(() => {
            expect(bodiesAsked().some((path) => path.includes('/messages/one/'))).toBe(true);
        });
        expect(await screen.findByText('The whole of what one says.')).toBeDefined();
    });

    it('opens a conversation nobody has read at its last word', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])));

        expect(await screen.findByText('What one added.')).toBeDefined();
        expect(screen.queryByText('What three added.')).toBeNull();
    });

    it('opens at what has not been read, so catching up is not scrolling to the bottom', async () => {
        const unread = { one: { unread: true }, two: { unread: true } };
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'], {}, unread)));

        await screen.findByText('What three added.');

        expect(screen.queryByText('What one added.')).toBeNull();
        expect(screen.queryByText('What two added.')).toBeNull();
    });

    it('opens at the message it was opened at, and puts the reader there', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])), { threadId, openAt: 'two' });

        expect(await screen.findByText('What one added.')).toBeDefined();
        expect(screen.queryByText('What two added.')).toBeNull();

        await waitFor(() => {
            expect(document.activeElement?.textContent).toContain('The auditor');
        });
    });

    it('puts the reader at the message it opened itself at, where nobody named one', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])));

        expect(await screen.findByText('What one added.')).toBeDefined();

        await waitFor(() => {
            const focused = document.activeElement;

            expect(focused?.tagName).toBe('SUMMARY');
            expect(focused?.closest('details')?.open).toBe(true);
        });
    });

    it('puts the reader at what it opened instead, where the message it was opened at is not in the conversation', async () => {
        drawing(deploymentAnswering(pageOf(['one', 'two', 'three'])), { threadId, openAt: 'a-message-of-another' });

        expect(await screen.findByText('The whole of what three says.')).toBeDefined();

        await waitFor(() => {
            expect(document.activeElement?.tagName).toBe('SUMMARY');
            expect(document.activeElement?.closest('details')?.open).toBe(true);
        });
    });

    it('leaves focus where the reader put it when they collapse a message, which is not a view change', async () => {
        drawing(
            deploymentAnswering(pageOf(['one', 'two', 'three'], {}, { one: { unread: true }, two: { unread: true } })),
        );

        await waitFor(() => {
            expect(document.activeElement?.tagName).toBe('SUMMARY');
        });

        const arrivedAt = document.activeElement;

        fireEvent.click(arrivedAt ?? document.body);

        // Collapsed again, which is what puts the contribution back on the line and what changes `expanded`.
        expect(await screen.findByText('What one added.')).toBeDefined();
        expect(document.activeElement).toBe(arrivedAt);
    });

    it('reads on until the message it was opened at is in hand, keeping the history above it', async () => {
        drawing(
            deploymentAnswering(
                pageOf(['one', 'two'], { nextCursor: 'onwards', messageCount: 4 }),
                pageOf(['three', 'four'], { messageCount: 4 }),
            ),
            { threadId, openAt: 'four' },
        );

        expect(await screen.findByText('What three added.')).toBeDefined();
        expect(screen.getByText('What one added.')).toBeDefined();
        expect(screen.getAllByRole('listitem')).toHaveLength(4);
    });

    it('reads the rest of a conversation the reader asks for rather than truncating it', async () => {
        drawing(
            deploymentAnswering(
                pageOf(['one', 'two'], { nextCursor: 'onwards', messageCount: 4 }),
                pageOf(['three', 'four'], { messageCount: 4 }),
            ),
        );

        fireEvent.click(await screen.findByRole('button', { name: 'Read more of this conversation' }));

        expect(await screen.findByText('What three added.')).toBeDefined();
        expect(screen.getByText('That is the whole of this conversation.')).toBeDefined();
    });

    it('says a conversation it has read the whole of is whole', async () => {
        drawing(deploymentAnswering(pageOf(['one'])));

        expect(await screen.findByText('That is the whole of this conversation.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Read more of this conversation' })).toBeNull();
    });

    it('says it is reading before anything has arrived', () => {
        drawing(answersNothing);

        expect(screen.getByText('Reading this conversation…')).toBeDefined();
    });

    it('says the machine is offline rather than wording a failure politely', () => {
        drawing(answersNothing, { threadId, openAt: null }, false);

        expect(
            screen.getByText('This machine is offline, so this conversation cannot be opened.', { exact: false }),
        ).toBeDefined();
        expect(asked).toHaveLength(0);
    });

    it('says a conversation holds nothing this credential may see rather than drawing an empty list', async () => {
        drawing(deploymentAnswering(pageOf([], { messageCount: 0 })));

        expect(
            await screen.findByText('There is no message in this conversation that you are allowed to see.'),
        ).toBeDefined();
    });

    it('says it is still reading where the page it holds shows nothing, rather than calling the conversation empty', async () => {
        let served = 0;
        const stalling: MailFathomTransport = (request) => {
            asked.push(request);
            served += 1;

            return served === 1
                ? Promise.resolve({
                      status: 200,
                      body: pageOf([], { nextCursor: 'onwards', messageCount: 4 }),
                      headers: {},
                  })
                : new Promise<ClientResponse>(() => undefined);
        };

        render(inTheFrame(stalling, { threadId, openAt: 'four' }, true));

        expect(await screen.findByText('Reading this conversation…')).toBeDefined();
        expect(screen.queryByText('There is no message in this conversation that you are allowed to see.')).toBeNull();
    });

    it('takes the reader to the message they came for when a failed page is read again, not to half a conversation', async () => {
        const answers: readonly { readonly status: number; readonly body: string }[] = [
            { status: 200, body: pageOf(['one', 'two'], { nextCursor: 'onwards', messageCount: 4 }) },
            { status: 503, body: '' },
            { status: 200, body: pageOf(['three', 'four'], { messageCount: 4 }) },
        ];

        let served = 0;
        const failingMidSearch: MailFathomTransport = (request) => {
            asked.push(request);

            if (request.path.includes('/body')) {
                return Promise.resolve({ status: 200, body: bodyAsWords(request.path), headers: {} });
            }

            const answer = answers[Math.min(served, answers.length - 1)] ?? { status: 500, body: '' };
            served += 1;

            return Promise.resolve({ ...answer, headers: {} });
        };

        render(inTheFrame(failingMidSearch, { threadId, openAt: 'four' }, true));

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(await screen.findByText('The whole of what four says.')).toBeDefined();

        await waitFor(() => {
            expect(document.activeElement?.closest('details')?.textContent).toContain('The whole of what four says.');
        });
    });

    it('says what failed, and offers the one way out a deployment that did not answer has', async () => {
        drawing(deploymentRefusing(503));

        expect(await screen.findByText('This conversation could not be read: unavailable.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('offers no second attempt at a failure a second attempt repeats', async () => {
        drawing(deploymentRefusing(403));

        expect(await screen.findByText('This conversation could not be read: unauthorized.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('keeps what is drawn when reading further fails, and says which part is missing', async () => {
        let served = 0;
        const transport: MailFathomTransport = (request) => {
            asked.push(request);

            if (request.path.includes('/body')) {
                return Promise.resolve({ status: 200, body: bodyAsWords(request.path), headers: {} });
            }

            served += 1;

            return Promise.resolve(
                served === 1
                    ? {
                          status: 200,
                          body: pageOf(['one', 'two'], { nextCursor: 'onwards', messageCount: 4 }),
                          headers: {},
                      }
                    : { status: 503, body: '', headers: {} },
            );
        };

        drawing(transport);
        fireEvent.click(await screen.findByRole('button', { name: 'Read more of this conversation' }));

        expect(await screen.findByText('Part of this conversation could not be read: unavailable.')).toBeDefined();
        expect(screen.getByText('What one added.')).toBeDefined();
        expect(screen.getAllByRole('listitem')).toHaveLength(2);
    });

    it('offers the way back to the message it was opened from in every state', () => {
        drawing(answersNothing);

        expect(screen.getByRole('button', { name: 'Back to the message' })).toBeDefined();
    });

    it('drops a failure the network gap itself caused, so coming back reads again rather than asking to be pressed', async () => {
        const conversation: OpenConversation = { threadId, openAt: null };
        const { rerender } = drawing(deploymentRefusing(503), conversation);
        await screen.findByText('This conversation could not be read: unavailable.');

        rerender(inTheFrame(deploymentRefusing(503), conversation, false));

        expect(
            screen.getByText('This machine is offline, so this conversation cannot be opened.', { exact: false }),
        ).toBeDefined();
        expect(screen.queryByText('This conversation could not be read: unavailable.')).toBeNull();
    });
});
