// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { AttachmentDeliveryContext, type AttachmentDelivery } from '../deployment/attachmentDelivery';
import { LocalizationProvider } from '../localization/Localization';
import {
    ReadMarkingContext,
    nothingMarkedRead,
    type MessageOpened,
    type ReadMarking,
} from '../readMarking/useReadMarking';
import { IntentField } from '../shell/IntentField';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { WorkspaceProvider } from '../workspace/Workspace';
import { ReadingPane } from './ReadingPane';

// The network boundary is the transport and it is the whole of what these tests fake, so the routes the pane asks for,
// the parsing that reads the answers, and the failure mapping are all under test rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const deliversNothing: AttachmentDelivery = () => Promise.resolve('delivered');

function description(overrides: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        storedEmailId: messageId,
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
        ...overrides,
    });
}

const bodyAsWords = JSON.stringify({
    storedEmailId: messageId,
    availability: 'Readable',
    plainText: { text: 'The invoice is attached.', originalCharacterCount: 24, truncation: 'None' },
    document: null,
    remoteImagesRequested: false,
});

const asked: ClientRequest[] = [];

/** A deployment answering both reads the pane makes, with the description a test named and a body it always sends. */
function deploymentDescribing(described = description(), status = 200): MailFathomTransport {
    return (request) => {
        asked.push(request);

        const answer: ClientResponse = request.path.includes('/body')
            ? { status: 200, body: bodyAsWords, headers: {} }
            : { status, body: described, headers: {} };

        return Promise.resolve(answer);
    };
}

/** A deployment that has taken the request and not answered it, which is what a surface that waits is proven against. */
const answersNothing: MailFathomTransport = () => new Promise<ClientResponse>(() => undefined);

function drawing(
    transport: MailFathomTransport,
    storedEmailId: string | null = messageId,
    online = true,
    deliver: AttachmentDelivery = deliversNothing,
    marking: ReadMarking = nothingMarkedRead,
): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <AttachmentDeliveryContext value={deliver}>
                        <ReadMarkingContext value={marking}>
                            <ReadingPane
                                session={session}
                                transport={transport}
                                storedEmailId={storedEmailId}
                                online={online}
                            />
                        </ReadMarkingContext>
                    </AttachmentDeliveryContext>
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

/** A client that would mark read, recording what the drawn body said was opened rather than submitting it. */
function recordingMarkings(): { marking: ReadMarking; opened: MessageOpened[] } {
    const opened: MessageOpened[] = [];

    return {
        opened,
        marking: {
            marked: new Map(),
            markRead: (message) => {
                opened.push(message);
            },
        },
    };
}

describe('ReadingPane', () => {
    it('says nothing is open rather than drawing an empty message', () => {
        asked.length = 0;
        drawing(deploymentDescribing(), null);

        expect(screen.getByText('Open a message to read it here.')).toBeDefined();
        expect(asked).toEqual([]);
    });

    it('says it is reading while the deployment has answered nothing', () => {
        drawing(answersNothing);

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'Reading this message…');
    });

    it('says the machine is offline rather than reporting the deployment as unreachable', () => {
        drawing(answersNothing, messageId, false);

        expect(
            screen.getByText(
                'This machine is offline, so this message cannot be opened. It opens on its own once the network comes back.',
            ),
        ).toBeDefined();
    });

    // The sentence above promises the message opens on its own, so the network coming back is what has to make
    // that true — and asking for nothing while there is none is what leaves a read to come back to.
    it('asks for nothing without a network, and reads on its own once one is back', async () => {
        asked.length = 0;
        const transport = deploymentDescribing();
        const { rerender } = render(paneReading(transport, false));

        expect(asked).toEqual([]);

        rerender(paneReading(transport, true));

        expect(await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 })).toBeDefined();
    });

    it('draws the headers and the body of the message it read', async () => {
        drawing(deploymentDescribing());

        expect(await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 })).toBeDefined();
        expect(await screen.findByText('The invoice is attached.')).toBeDefined();
    });

    // Nothing here writes to a mailbox, and the strongest statement of that a test can make is which routes were
    // reached: the request type admits no verb but `GET`, so an assertion on the verb would be an assertion about the
    // compiler rather than about the pane.
    it('asks for the description and for the body, and reaches no other route on the deployment', async () => {
        asked.length = 0;
        drawing(deploymentDescribing());
        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect([...new Set(asked.map((request) => request.path))]).toEqual([
            `https://mail.example.invalid/api/client/messages/${messageId}`,
            `https://mail.example.invalid/api/client/messages/${messageId}/body`,
        ]);
    });

    it('says what failed and offers the way out where reading again is one', async () => {
        drawing(deploymentDescribing(description(), 503));

        expect(await screen.findByText('This message could not be opened: unavailable.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('offers no way out of a refusal that would repeat identically on a second attempt', async () => {
        drawing(deploymentDescribing(description(), 403));

        expect(await screen.findByText('This message could not be opened: unauthorized.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('names no file where the message carries none', async () => {
        drawing(deploymentDescribing());
        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(screen.queryByText('Files this message carries')).toBeNull();
    });

    it('describes every file the message carries before any of them is fetched', async () => {
        drawing(deploymentDescribing(description({ attachments: [invoice, photograph] })));

        expect(await screen.findByRole('button', { name: 'Download invoice.pdf' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Download photo.jpg' })).toBeDefined();
    });

    it('says what a message carries besides its files, where any of it is true', async () => {
        drawing(
            deploymentDescribing(
                description({
                    carried: {
                        attachmentCount: 0,
                        totalSizeOctets: 0,
                        inlineResourceCount: 0,
                        encrypted: false,
                        unverifiedSignature: true,
                        unexpandedTnefPart: true,
                    },
                }),
            ),
        );

        expect(
            await screen.findByText('This message carries a signature, and nothing here has verified it.'),
        ).toBeDefined();
        expect(
            screen.getByText(
                'This message carries a winmail.dat part, which was recorded without being opened, so whatever it holds is not listed above.',
            ),
        ).toBeDefined();
    });

    it('leaves focus where it was when the pane opens, because landing on a message is not a navigation', async () => {
        render(paneFor(messageId));

        expect(await screen.findByRole('article', { name: 'Quarterly invoice' })).not.toBe(document.activeElement);
    });

    it('places focus on the message opened next, rather than leaving it on whatever opened it', async () => {
        const { rerender } = render(paneFor(messageId));
        await screen.findByRole('article', { name: 'Quarterly invoice' });

        rerender(paneFor('11111111-1111-4111-8111-111111111111'));

        await waitFor(() => {
            expect(screen.getByRole('article', { name: 'Quarterly invoice' })).toBe(document.activeElement);
        });
    });
});

const invoice = {
    position: 0,
    fileName: 'invoice.pdf',
    wasFileNameNormalized: false,
    mediaType: 'application/pdf',
    sizeOctets: 2_048,
};

const photograph = {
    position: 1,
    fileName: 'photo.jpg',
    wasFileNameNormalized: false,
    mediaType: 'image/jpeg',
    sizeOctets: 100_000,
};

// The same pane across a network gap, which is a thing only a rerender with another `online` produces.
function paneReading(transport: MailFathomTransport, online: boolean) {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <AttachmentDeliveryContext value={deliversNothing}>
                        <ReadingPane
                            session={session}
                            transport={transport}
                            storedEmailId={messageId}
                            online={online}
                        />
                    </AttachmentDeliveryContext>
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}
// A second message opening is a view change, which is a thing only a rerender with another identifier produces — the
// first render is a landing rather than a navigation and deliberately moves focus nowhere.
function paneFor(storedEmailId: string) {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <AttachmentDeliveryContext value={deliversNothing}>
                        <ReadingPane
                            session={session}
                            transport={deploymentDescribing()}
                            storedEmailId={storedEmailId}
                            online
                        />
                    </AttachmentDeliveryContext>
                </LinkOpenerContext>
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

// A selection is a gesture over a real range, and what it is worth is what the intent field then says about it — so the
// field is mounted beside the pane, in the one workspace both read, and the assertion is the sentence a person sees.
describe('ReadingPane selection', () => {
    function readingBeside(): void {
        render(
            <LocalizationProvider>
                <WorkspaceProvider>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <AttachmentDeliveryContext value={deliversNothing}>
                            <IntentField accounts={[]} />
                            <ReadingPane
                                session={session}
                                transport={deploymentDescribing()}
                                storedEmailId={messageId}
                                online
                            />
                        </AttachmentDeliveryContext>
                    </LinkOpenerContext>
                </WorkspaceProvider>
            </LocalizationProvider>,
        );
    }

    it('scopes the next question to nothing while nobody has selected anything', async () => {
        readingBeside();
        await screen.findByText('The invoice is attached.');

        expect(
            screen.queryByText('Asking about the part of this message you selected: “The invoice is attached.”'),
        ).toBeNull();
    });

    it('carries the words somebody selected into the scope the next question is asked under', async () => {
        readingBeside();
        const words = await screen.findByText('The invoice is attached.');

        select(words);
        fireEvent.mouseUp(words);

        expect(
            await screen.findByText('Asking about the part of this message you selected: “The invoice is attached.”'),
        ).toBeDefined();
    });

    it('gives back the whole message as the scope when that is asked for', async () => {
        readingBeside();
        const words = await screen.findByText('The invoice is attached.');

        select(words);
        fireEvent.mouseUp(words);
        fireEvent.click(await screen.findByRole('button', { name: 'Ask about the whole message instead' }));

        expect(
            screen.queryByText('Asking about the part of this message you selected: “The invoice is attached.”'),
        ).toBeNull();
    });

    it('offers the way into the conversation where the service threaded the message', async () => {
        asked.length = 0;
        drawing(deploymentDescribing(description({ threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d' })));

        expect(await screen.findByRole('button', { name: 'Show the whole conversation' })).toBeDefined();
    });

    it('offers no conversation for a message the service threaded with nothing', async () => {
        asked.length = 0;
        drawing(deploymentDescribing());

        await screen.findByRole('heading', { name: 'Quarterly invoice' });

        expect(screen.queryByRole('button', { name: 'Show the whole conversation' })).toBeNull();
    });

    // Opening a message is its words having reached the pane, and where it stands travels with it, because the folder
    // whose count has to answer for the marking is the folder the deployment counted the message in.
    it('says which message was opened, and where in the mailbox it stands', async () => {
        asked.length = 0;

        const { marking, opened } = recordingMarkings();

        drawing(deploymentDescribing(), messageId, true, deliversNothing, marking);
        await screen.findByText('The invoice is attached.');

        await waitFor(() => {
            expect(opened).toStrictEqual([
                { storedEmailId: messageId, account: 'work', folder: 'INBOX', unread: true },
            ]);
        });
    });

    it('says nothing was opened while the message is still being read', () => {
        asked.length = 0;

        const { marking, opened } = recordingMarkings();

        drawing(answersNothing, messageId, true, deliversNothing, marking);

        expect(opened).toStrictEqual([]);
    });
});

function select(within: Element): void {
    const range = document.createRange();
    range.selectNodeContents(within);

    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
}
