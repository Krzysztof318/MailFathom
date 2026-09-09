// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    ClientSignal,
    MailFathomTransport,
} from '@mailfathom/client-backend';
import { AttachmentExchangeContext, type AttachmentExchange } from '../deployment/attachmentExchange';
import { OpenAttachmentContext, type OpenedAttachment } from '../workspace/openAttachment';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { EmbeddedHtmlMessagesContext } from '../preferences/messageView';
import {
    ReadMarkingContext,
    nothingMarkedRead,
    type MessageOpened,
    type ReadMarking,
} from '../readMarking/useReadMarking';
import { IntentField } from '../shell/IntentField';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { emptyWorkspace, useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { WorkspaceProvider } from '../workspace/Workspace';
import {
    SignalledChangesContext,
    nothingSignalled,
    type SignalListener,
    type SignalledChanges,
} from '../signals/signalledChanges';
import { ReadingPane } from './ReadingPane';

// The network boundary is the transport and it is the whole of what these tests fake, so the routes the pane asks for,
// the parsing that reads the answers, and the failure mapping are all under test rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const deliversNothing: AttachmentExchange = {
    deliver: () => Promise.resolve('delivered'),
    read: () => Promise.resolve({ outcome: 'shown', content: '' }),
};

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

/**
 * A deployment that has taken the request and not answered it, which is what a surface that waits is proven against.
 *
 * It records what it was asked for, which is what makes a read that is in flight visible at all: an answer is what
 * every other assertion here waits on, and a read that never answers has nothing but the record to be seen by.
 */
const answersNothing: MailFathomTransport = (request) => {
    asked.push(request);

    return new Promise<ClientResponse>(() => undefined);
};

// What the pane wrote back, read the way the frame above it reads it: out of the workspace rather than out of the
// component. An `output` reports itself as a status region, so it is picked out by carrying a workspace.
function SelectionProbe() {
    const { workspace } = useWorkspace();

    return <output>{JSON.stringify(workspace)}</output>;
}

function selected(): string | null {
    const probe = screen.getAllByRole('status').find((element) => element.textContent.startsWith('{'));

    return (JSON.parse(probe?.textContent ?? '') as Workspace).selection;
}

// The pane opened on the message the workspace says is open, which is the arrangement a reload produces and the only
// one in which letting go of what is open means anything.
function drawingWithSelection(transport: MailFathomTransport): void {
    window.sessionStorage.setItem('mailfathom.workspace', JSON.stringify({ ...emptyWorkspace, selection: messageId }));

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <AttachmentExchangeContext value={deliversNothing}>
                            <OpenAttachmentContext value={() => undefined}>
                                <ReadingPane
                                    session={session}
                                    transport={transport}
                                    storedEmailId={messageId}
                                    online
                                    onShowFullHtml={() => undefined}
                                />
                            </OpenAttachmentContext>
                        </AttachmentExchangeContext>
                    </LinkOpenerContext>
                    <SelectionProbe />
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );
}

function drawing(
    transport: MailFathomTransport,
    storedEmailId: string | null = messageId,
    online = true,
    deliver: AttachmentExchange = deliversNothing,
    marking: ReadMarking = nothingMarkedRead,
    signalled: SignalledChanges = nothingSignalled,
): void {
    render(
        <SignalledChangesContext value={signalled}>
            <LocalizationProvider>
                <ToastsProvider>
                    <WorkspaceProvider>
                        <LinkOpenerContext value={() => Promise.resolve()}>
                            <AttachmentExchangeContext value={deliver}>
                                <OpenAttachmentContext value={() => undefined}>
                                    <ReadMarkingContext value={marking}>
                                        <ReadingPane
                                            session={session}
                                            transport={transport}
                                            storedEmailId={storedEmailId}
                                            online={online}
                                            onShowFullHtml={() => undefined}
                                        />
                                    </ReadMarkingContext>
                                </OpenAttachmentContext>
                            </AttachmentExchangeContext>
                        </LinkOpenerContext>
                    </WorkspaceProvider>
                </ToastsProvider>
            </LocalizationProvider>
        </SignalledChangesContext>,
    );
}

/** A deployment with a channel open, and the handle a test says something over it with. */
function deploymentSaying(): { changes: SignalledChanges; say: (signal: ClientSignal) => void } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
        },
        say: (signal) => {
            for (const listener of [...listeners]) {
                listener(signal);
            }
        },
    };
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

// The session's store outlives a test rather than a file, so a workspace one test opened the pane on would be the one
// the next test opened with.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('ReadingPane', () => {
    it('says nothing is open rather than drawing an empty message', () => {
        asked.length = 0;
        drawing(deploymentDescribing(), null);

        expect(screen.getByText('Nothing is open')).toBeDefined();
        expect(asked).toEqual([]);
    });

    it('says it is reading while the deployment has answered nothing', () => {
        drawing(answersNothing);

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'Reading this message…');
    });

    // What the skeleton standing in that space looks like is held against the design as images rather than asserted
    // here: it is `aria-hidden` by construction and jsdom computes no layout, so what this suite says about the wait
    // is what a person meets — the sentence until the message is there, and the message with no sentence after it.
    it('says it is reading until the message is there, and stops saying it once the message is', async () => {
        drawing(deploymentDescribing());

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'Reading this message…');

        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(screen.queryByText('Reading this message…')).toBeNull();
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

    // The body is read on the same terms as the description now that both leave together, and the network going is
    // one of them: a refusal the gap itself caused is not something to leave a reader pressing through under a
    // message that is otherwise on the screen, so it goes with the gap and the words arrive on their own afterwards.
    it('reads the body again once the network is back, rather than standing on the refusal the gap caused', async () => {
        let bodyStatus = 503;

        const transport: MailFathomTransport = (request) => {
            asked.push(request);

            const answer: ClientResponse = request.path.includes('/body')
                ? { status: bodyStatus, body: bodyStatus === 200 ? bodyAsWords : '', headers: {} }
                : { status: 200, body: description(), headers: {} };

            return Promise.resolve(answer);
        };

        const { rerender } = render(paneReading(transport, true));
        await screen.findByText('The message could not be read: unavailable.');

        rerender(paneReading(transport, false));
        expect(screen.queryByText('The message could not be read: unavailable.')).toBeNull();

        bodyStatus = 200;
        rerender(paneReading(transport, true));

        expect(await screen.findByText('The invoice is attached.')).toBeDefined();
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

        // Waited for rather than read once the heading is on the screen: the heading is the description arriving, and
        // the body is a second read that has not necessarily been made by then. Reading the record at that moment is
        // what reports one route where there are two, on a machine loaded enough to put the two commits apart.
        await waitFor(() => {
            expect([...new Set(asked.map((request) => request.path))].sort()).toEqual([
                `https://mail.example.invalid/api/client/messages/${messageId}`,
                `https://mail.example.invalid/api/client/messages/${messageId}/body`,
            ]);
        });
    });

    // The two reads need nothing from each other — the identity both of them carry is the one the reader pressed — so
    // neither is allowed to wait on the other having settled. Proven against a deployment that answers neither: both
    // paths reaching it while nothing has come back is the whole of what "at the same time" can mean here, and it is
    // what the pane could not do while the body was read by the component drawn under the description's own answer.
    it('asks for the description and the body at once, neither waiting on the other to answer', async () => {
        asked.length = 0;

        drawing(answersNothing);

        await waitFor(() => {
            expect([...new Set(asked.map((request) => request.path))].sort()).toEqual([
                `https://mail.example.invalid/api/client/messages/${messageId}`,
                `https://mail.example.invalid/api/client/messages/${messageId}/body`,
            ]);
        });
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

    // What is open outlives the message across a reload, so a reader returning to a message their deployment no longer
    // holds is owed the empty state rather than a failure with nothing behind it. Asserted on the workspace because
    // that is where the fact lives: the pane above this one draws nothing open once the selection has gone.
    it('lets go of a message the deployment no longer holds, so what is open is nothing rather than a failure', async () => {
        drawingWithSelection(deploymentDescribing(description(), 404));

        await waitFor(() => {
            expect(selected()).toBeNull();
        });
    });

    it.each([401, 403, 500, 503])(
        'keeps what is open where a status of %i says nothing about the message being there',
        async (status) => {
            drawingWithSelection(deploymentDescribing(description(), status));

            await screen.findByRole('alert');

            expect(selected()).toBe(messageId);
        },
    );

    it('names no file where the message carries none', async () => {
        drawing(deploymentDescribing());
        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(screen.queryByRole('list', { name: 'Files this message carries' })).toBeNull();
    });

    it('describes every file the message carries before any of them is fetched', async () => {
        drawing(deploymentDescribing(description({ attachments: [invoice, photograph] })));

        expect(await screen.findByRole('button', { name: 'Download invoice.pdf' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Download photo.jpg' })).toBeDefined();
        expect(screen.getByRole('list', { name: 'Files this message carries' })).toBeDefined();
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
            <ToastsProvider>
                <WorkspaceProvider>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <AttachmentExchangeContext value={deliversNothing}>
                            <OpenAttachmentContext value={() => undefined}>
                                <ReadingPane
                                    session={session}
                                    transport={transport}
                                    storedEmailId={messageId}
                                    online={online}
                                    onShowFullHtml={() => undefined}
                                />
                            </OpenAttachmentContext>
                        </AttachmentExchangeContext>
                    </LinkOpenerContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>
    );
}
// A second message opening is a view change, which is a thing only a rerender with another identifier produces — the
// first render is a landing rather than a navigation and deliberately moves focus nowhere.
function paneFor(storedEmailId: string) {
    return (
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <AttachmentExchangeContext value={deliversNothing}>
                            <OpenAttachmentContext value={() => undefined}>
                                <ReadingPane
                                    session={session}
                                    transport={deploymentDescribing()}
                                    storedEmailId={storedEmailId}
                                    online
                                    onShowFullHtml={() => undefined}
                                />
                            </OpenAttachmentContext>
                        </AttachmentExchangeContext>
                    </LinkOpenerContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>
    );
}

// A selection is a gesture over a real range, and what it is worth is what the intent field then says about it — so the
// field is mounted beside the pane, in the one workspace both read, and the assertion is the sentence a person sees.
// The pane with the way to the sender's own markup under test: what it offers, and what pressing it does.
function drawingOfferingMarkup(
    shown: (storedEmailId: string, subject: string | null) => void,
    embedded: boolean,
): void {
    render(
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <EmbeddedHtmlMessagesContext value={embedded}>
                        <LinkOpenerContext value={() => Promise.resolve()}>
                            <AttachmentExchangeContext value={deliversNothing}>
                                <OpenAttachmentContext value={() => undefined}>
                                    <ReadMarkingContext value={nothingMarkedRead}>
                                        <ReadingPane
                                            session={session}
                                            transport={deploymentDescribing()}
                                            storedEmailId={messageId}
                                            online
                                            onShowFullHtml={shown}
                                        />
                                    </ReadMarkingContext>
                                </OpenAttachmentContext>
                            </AttachmentExchangeContext>
                        </LinkOpenerContext>
                    </EmbeddedHtmlMessagesContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );
}

describe('ReadingPane and the sender own markup', () => {
    it('offers the sender own markup on the message card rather than in the head', async () => {
        drawingOfferingMarkup(vi.fn(), false);

        const offered = await screen.findByRole('button', { name: 'Show the full HTML version' });

        expect(offered.closest('header')).toBeNull();
    });

    it('asks before it shows anything, so pressing the control opens no markup on its own', async () => {
        const shown = vi.fn();

        drawingOfferingMarkup(shown, false);
        fireEvent.click(await screen.findByRole('button', { name: 'Show the full HTML version' }));

        expect(shown).not.toHaveBeenCalled();
        expect(screen.getByRole('heading', { name: 'Show the full HTML?' })).toBeDefined();
    });

    it('offers no such control where the reader chose to embed every message as its sender wrote it', async () => {
        drawingOfferingMarkup(vi.fn(), true);

        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(screen.queryByRole('button', { name: 'Show the full HTML version' })).toBeNull();
    });
});

describe('ReadingPane against a deployment that says what changed', () => {
    it('reads the message again when the deployment says that message changed', async () => {
        // Arrange
        asked.length = 0;
        const signalling = deploymentSaying();
        drawing(deploymentDescribing(), messageId, true, deliversNothing, nothingMarkedRead, signalling.changes);
        await screen.findByText('Quarterly invoice');
        const before = asked.filter((request) => !request.path.includes('/body')).length;

        // Act
        act(() => {
            signalling.say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: [messageId] });
        });

        // Assert
        await waitFor(() => {
            expect(asked.filter((request) => !request.path.includes('/body')).length).toBe(before + 1);
        });
    });

    it('leaves a message about other mail alone rather than reading this one again', async () => {
        // Arrange
        asked.length = 0;
        const signalling = deploymentSaying();
        drawing(deploymentDescribing(), messageId, true, deliversNothing, nothingMarkedRead, signalling.changes);
        await screen.findByText('Quarterly invoice');
        const before = asked.length;

        // Act
        act(() => {
            signalling.say({
                kind: 'mail.changed',
                account: 'work',
                folder: 'INBOX',
                emails: ['11111111-1111-4111-8111-111111111111'],
            });
        });

        // Assert
        expect(asked.length).toBe(before);
    });

    it('keeps the message a reader is part-way through on the screen while it reads it again', async () => {
        // Arrange
        asked.length = 0;
        let answersHeld = false;
        const signalling = deploymentSaying();
        const holdsTheSecondRead: MailFathomTransport = (request) => {
            asked.push(request);

            if (request.path.includes('/body')) {
                return Promise.resolve<ClientResponse>({ status: 200, body: bodyAsWords, headers: {} });
            }

            if (answersHeld) {
                return new Promise<ClientResponse>(() => undefined);
            }

            return Promise.resolve<ClientResponse>({ status: 200, body: description(), headers: {} });
        };

        drawing(holdsTheSecondRead, messageId, true, deliversNothing, nothingMarkedRead, signalling.changes);
        await screen.findByText('Quarterly invoice');
        answersHeld = true;

        // Act
        act(() => {
            signalling.say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: [messageId] });
        });

        // Assert
        expect(screen.getByText('Quarterly invoice')).toBeDefined();
        expect(screen.queryByText('Reading this message…')).toBeNull();
    });
});

// Following a search result's citation, which is one act across two components: the row records which file of which
// message it cited, and this pane is the only place that learns how large that file declares itself to be — the bound
// the download is read under, and the reason the citation is a coordinate rather than an opened file.
const followTheCitation = 'Follow the citation, as a search result would write it.';

describe('ReadingPane following a cited file', () => {
    // The citation is written the way a search row writes it — through the workspace, before the pane has read the
    // message — rather than by seeding a store, which `rememberWorkspace` deliberately never keeps it in.
    function citing(cited: Workspace['citedAttachment']): { opened: OpenedAttachment[]; cite: () => void } {
        const opened: OpenedAttachment[] = [];

        // A control the test presses rather than a function captured out of the render: writing to a variable declared
        // outside a component is a side effect during render whatever the variable is for, and a render React repeats
        // is a render that would do it twice.
        function Citing() {
            const { workspace, revise } = useWorkspace();

            return (
                <>
                    <button
                        type="button"
                        onClick={() => {
                            revise({ citedAttachment: cited });
                        }}
                    >
                        {followTheCitation}
                    </button>
                    <output>{JSON.stringify(workspace.citedAttachment)}</output>
                </>
            );
        }

        render(
            <LocalizationProvider>
                <ToastsProvider>
                    <WorkspaceProvider>
                        <LinkOpenerContext value={() => Promise.resolve()}>
                            <AttachmentExchangeContext value={deliversNothing}>
                                <OpenAttachmentContext
                                    value={(opening) => {
                                        opened.push(opening);
                                    }}
                                >
                                    <Citing />
                                    <ReadingPane
                                        session={session}
                                        transport={deploymentDescribing(
                                            description({ attachments: [invoice, photograph] }),
                                        )}
                                        storedEmailId={messageId}
                                        online
                                        onShowFullHtml={() => undefined}
                                    />
                                </OpenAttachmentContext>
                            </AttachmentExchangeContext>
                        </LinkOpenerContext>
                    </WorkspaceProvider>
                </ToastsProvider>
            </LocalizationProvider>,
        );

        return {
            opened,
            cite: () => {
                fireEvent.click(screen.getByRole('button', { name: followTheCitation }));
            },
        };
    }

    it('opens the file a result cited, with the size the message declared for it', async () => {
        const { opened, cite } = citing({ storedEmailId: messageId, position: 1 });

        act(cite);

        await waitFor(() => {
            expect(opened).toStrictEqual([{ storedEmailId: messageId, attachment: photograph }]);
        });
    });

    it('clears the citation as it follows it, so reopening the message opens no file nobody asked for', async () => {
        const { cite } = citing({ storedEmailId: messageId, position: 0 });

        act(cite);

        await waitFor(() => {
            expect(screen.getByText('null')).toBeDefined();
        });
    });

    it('opens nothing where the citation names a message other than the one being read', async () => {
        const { opened, cite } = citing({ storedEmailId: 'another-message', position: 0 });

        act(cite);
        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(opened).toStrictEqual([]);
    });

    // The everyday case, and the one nothing re-reads for: opening a message that is already the message open is a
    // no-op, so a citation followed inside the read would never be followed at all.
    it('opens the cited file of a message that was already read and on the screen', async () => {
        const { opened, cite } = citing({ storedEmailId: messageId, position: 1 });

        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        act(cite);

        await waitFor(() => {
            expect(opened).toStrictEqual([{ storedEmailId: messageId, attachment: photograph }]);
        });
    });

    it('spends a citation the reader abandoned by reading another message', async () => {
        const { cite } = citing({ storedEmailId: 'another-message', position: 0 });

        act(cite);

        await waitFor(() => {
            expect(screen.getByText('null')).toBeDefined();
        });
    });

    it('reads the message once although following the citation revises the workspace', async () => {
        asked.length = 0;

        const { opened, cite } = citing({ storedEmailId: messageId, position: 1 });

        act(cite);

        await waitFor(() => {
            expect(opened).toStrictEqual([{ storedEmailId: messageId, attachment: photograph }]);
        });

        expect(asked.filter((request) => !request.path.includes('/body'))).toHaveLength(1);
    });

    it('opens nothing where the message carries no file at the cited position', async () => {
        const { opened, cite } = citing({ storedEmailId: messageId, position: 9 });

        act(cite);
        await screen.findByRole('heading', { name: 'Quarterly invoice', level: 2 });

        expect(opened).toStrictEqual([]);
    });
});

describe('ReadingPane selection', () => {
    // The message being read is `workspace.selection` in the application — `App.tsx` hands the pane that very value —
    // and the passage in scope is read against it, so the harness sets it rather than leaving the pane drawing a
    // message the workspace has never heard of.
    function Opened(): null {
        const { revise } = useWorkspace();

        useEffect(() => {
            revise({ selection: messageId });
        }, [revise]);

        return null;
    }

    function readingBeside(): void {
        render(
            <LocalizationProvider>
                <ToastsProvider>
                    <WorkspaceProvider>
                        <Opened />
                        <LinkOpenerContext value={() => Promise.resolve()}>
                            <AttachmentExchangeContext value={deliversNothing}>
                                <OpenAttachmentContext value={() => undefined}>
                                    <IntentField accounts={[]} />
                                    <ReadingPane
                                        session={session}
                                        transport={deploymentDescribing()}
                                        storedEmailId={messageId}
                                        online
                                        onShowFullHtml={() => undefined}
                                    />
                                </OpenAttachmentContext>
                            </AttachmentExchangeContext>
                        </LinkOpenerContext>
                    </WorkspaceProvider>
                </ToastsProvider>
            </LocalizationProvider>,
        );
    }

    it('scopes the next question to nothing while nobody has selected anything', async () => {
        readingBeside();
        await screen.findByText('The invoice is attached.');

        expect(
            screen.queryAllByText('Asking about the part of this message you selected: “The invoice is attached.”'),
        ).toHaveLength(0);
    });

    it('carries the words somebody selected into the scope the next question is asked under', async () => {
        readingBeside();
        const words = await screen.findByText('The invoice is attached.');

        select(words);
        fireEvent.mouseUp(words);

        expect(
            await screen.findAllByText(
                'Asking about the part of this message you selected: “The invoice is attached.”',
            ),
        ).toHaveLength(2);
    });

    it('gives back the whole message as the scope when that is asked for', async () => {
        readingBeside();
        const words = await screen.findByText('The invoice is attached.');

        select(words);
        fireEvent.mouseUp(words);
        await screen.findAllByText('Asking about the part of this message you selected: “The invoice is attached.”');

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: `message:${messageId}` },
        });

        expect(
            screen.queryAllByText('Asking about the part of this message you selected: “The invoice is attached.”'),
        ).toHaveLength(0);
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
