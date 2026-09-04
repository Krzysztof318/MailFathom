// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { EmbeddedHtmlMessagesContext } from '../preferences/messageView';
import { Message } from './Message';

// The transport is handed in, so nothing here replaces a module: the request, the parsing, and the failure mapping
// stay the real ones, and only the answer they are given is the test's.
const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };
const baseAddress = session.baseAddress;

type Answer = Omit<ClientResponse, 'headers'>;

let asked: string[] = [];
let answer: (path: string) => Promise<Answer> = () => Promise.resolve({ status: 200, body: '' });

const transport: MailFathomTransport = async (request) => {
    asked.push(request.path);

    return { ...(await answer(request.path)), headers: {} };
};

function answering(response: Answer): void {
    answer = () => Promise.resolve(response);
}

const readableBody = {
    storedEmailId: 'stub-message',
    availability: 'Readable',
    plainText: { text: 'As words.', originalCharacterCount: 9, truncation: 'None' },
    document: {
        schemaVersion: 1,
        blocks: [
            {
                type: 'paragraph',
                version: 1,
                content: [{ text: 'A drawn message.', emphasis: 'None', foreground: null, link: null }],
                alignment: 'Inherited',
            },
        ],
        refusal: 'None',
        removedRemoteReferenceCount: 2,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false,
    },
    remoteImagesRequested: false,
};

function bodyAnswering(remoteImages: boolean): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            ...readableBody,
            document: {
                ...readableBody.document,
                removedRemoteReferenceCount: remoteImages ? 0 : 2,
                retainedRemoteImageCount: remoteImages ? 1 : 0,
            },
            remoteImagesRequested: remoteImages,
        }),
    };
}

// Which reads were made, in the order they were first made. `StrictMode` is what `main.tsx` mounts, and it invokes an
// effect twice on the first mount, so a repeat of a read already made is the mode rather than the component.
function readsAsked(): string[] {
    return [...new Set(asked)];
}

function readingOneMessage(storedEmailId = 'stub-message') {
    return render(reading(storedEmailId));
}

function reading(storedEmailId: string) {
    return (
        <StrictMode>
            <LocalizationProvider>
                <Message session={session} transport={transport} storedEmailId={storedEmailId} />
            </LocalizationProvider>
        </StrictMode>
    );
}

/** The same message read as a conversation reads one, where the history it quoted is folded away. */
function readingInAConversation() {
    return render(
        <StrictMode>
            <LocalizationProvider>
                <Message session={session} transport={transport} storedEmailId="stub-message" quotedHistoryOnRequest />
            </LocalizationProvider>
        </StrictMode>,
    );
}

/** The same message, with somebody listening for its words having reached the screen. */
function readingReported(onBodyDrawn: () => void, storedEmailId = 'stub-message') {
    return (
        <StrictMode>
            <LocalizationProvider>
                <Message
                    session={session}
                    transport={transport}
                    storedEmailId={storedEmailId}
                    onBodyDrawn={onBodyDrawn}
                />
            </LocalizationProvider>
        </StrictMode>
    );
}

/** The same message read by somebody whose messages are the sender's own markup, or the reduced text. */
function readingUnder(embeddedHtmlMessages: boolean) {
    return (
        <StrictMode>
            <LocalizationProvider>
                <EmbeddedHtmlMessagesContext value={embeddedHtmlMessages}>
                    <Message session={session} transport={transport} storedEmailId="stub-message" />
                </EmbeddedHtmlMessagesContext>
            </LocalizationProvider>
        </StrictMode>
    );
}

// A deployment that serves the sender's own markup to a read that asked for it, and the reduced tree alone to one that
// did not. The answer varies with the ask because the package refuses a body carrying markup nobody asked for, which is
// the service's contract rather than this test's convenience — and an answer that asked for the markup carries the
// reduced tree as well, which is the whole reason changing the view back costs no read.
function answeringWithMarkupWhenAsked(): void {
    answer = (path) =>
        Promise.resolve(
            path.includes('fullHtml=true')
                ? {
                      status: 200,
                      body: JSON.stringify({
                          ...readableBody,
                          selfContainedHtml: {
                              text: '<html><head></head><body><p>As the sender wrote it.</p></body></html>',
                              originalCharacterCount: 69,
                              truncation: 'None',
                          },
                      }),
                  }
                : bodyAnswering(false),
        );
}

/** A reply: what somebody wrote, above the message they were answering. */
const replyQuotingWhatItAnswers: Answer = {
    status: 200,
    body: JSON.stringify({
        ...readableBody,
        document: {
            ...readableBody.document,
            blocks: [
                ...readableBody.document.blocks,
                {
                    type: 'quote',
                    version: 1,
                    depth: 1,
                    blocks: [
                        {
                            type: 'paragraph',
                            version: 1,
                            content: [
                                { text: 'The message it answers.', emphasis: 'None', foreground: null, link: null },
                            ],
                            alignment: 'Inherited',
                        },
                    ],
                },
            ],
        },
    }),
};

describe('Message', () => {
    beforeEach(() => {
        answering(bodyAnswering(false));
        asked = [];
    });

    it('draws the message the read answered with', async () => {
        readingOneMessage();

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('asks for the body without asking for remote pictures', async () => {
        readingOneMessage();
        await screen.findByText('A drawn message.');

        expect(readsAsked()).toEqual([`${baseAddress}/api/client/messages/stub-message/body`]);
    });

    it('says why the message could not be read rather than drawing an empty pane', async () => {
        answering({ status: 500, body: '' });

        readingOneMessage();

        expect(await screen.findByText('The message could not be read: unavailable.')).toBeDefined();
    });

    it('re-reads the one message with the ask when the reader asks for its pictures', async () => {
        readingOneMessage();
        await screen.findByText('A drawn message.');
        answering(bodyAnswering(true));

        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));

        expect(await screen.findByText('Pictures are being loaded from the sender for this message.')).toBeDefined();
        expect(readsAsked()).toEqual([
            `${baseAddress}/api/client/messages/stub-message/body`,
            `${baseAddress}/api/client/messages/stub-message/body?remoteImages=true`,
        ]);
    });

    it('offers a way back to the message when the read that failed was the ask for its pictures', async () => {
        readingOneMessage();
        await screen.findByText('A drawn message.');
        answering({ status: 500, body: '' });
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));
        await screen.findByText('The message could not be read: unavailable.');
        answering(bodyAnswering(false));

        fireEvent.click(screen.getByRole('button', { name: 'Show the message without them' }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('offers a read again when the message could not be reached, rather than only a reload', async () => {
        answering({ status: 500, body: '' });
        readingOneMessage();
        await screen.findByText('The message could not be read: unavailable.');
        answering(bodyAnswering(false));

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('offers no read again for a failure a second attempt answers identically', async () => {
        answering({ status: 401, body: '' });

        readingOneMessage();

        await screen.findByText('The message could not be read: unauthenticated.');
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('carries no picture ask onto the next message, whose sender asked for nothing', async () => {
        const opened = readingOneMessage();
        await screen.findByText('A drawn message.');
        answering(bodyAnswering(true));
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));
        await screen.findByText('Pictures are being loaded from the sender for this message.');
        answering(bodyAnswering(false));
        asked = [];

        opened.rerender(reading('another-message'));

        expect(await screen.findByText('Load pictures from the sender')).toBeDefined();
        expect(readsAsked()).toEqual([`${baseAddress}/api/client/messages/another-message/body`]);
    });

    it('draws no answered pictures when the reader comes back before the message they left has answered', async () => {
        const opened = readingOneMessage();
        await screen.findByText('A drawn message.');
        answering(bodyAnswering(true));
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));
        await screen.findByText('Pictures are being loaded from the sender for this message.');

        // The message they moved to never answers, so what stands on the screen when they come back is decided
        // entirely by what this component was still holding from the visit they asked for the pictures in.
        answer = (path) =>
            path.includes('another-message')
                ? new Promise<Answer>(() => undefined)
                : Promise.resolve(bodyAnswering(false));
        opened.rerender(reading('another-message'));
        opened.rerender(reading('stub-message'));

        expect(await screen.findByRole('button', { name: 'Load pictures from the sender' })).toBeDefined();
        expect(screen.queryByText('Pictures are being loaded from the sender for this message.')).toBeNull();
    });

    it('remembers nothing about the ask, so opening the message again asks again', async () => {
        const opened = readingOneMessage();
        await screen.findByText('A drawn message.');
        answering(bodyAnswering(true));
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));
        await screen.findByText('Pictures are being loaded from the sender for this message.');

        opened.unmount();
        answering(bodyAnswering(false));
        asked = [];
        readingOneMessage();

        expect(await screen.findByText('Load pictures from the sender')).toBeDefined();
        expect(readsAsked()).toEqual([`${baseAddress}/api/client/messages/stub-message/body`]);
    });

    it('folds the history a reply quoted away where it is read as part of a conversation', async () => {
        answering(replyQuotingWhatItAnswers);

        readingInAConversation();

        // What is folded is still in the document, so the assertion is on the disclosure being shut rather than on
        // the words being absent: a browser is what hides them, and jsdom draws no geometry.
        const quoted = await screen.findByText('The conversation this message quoted');

        expect(quoted.closest('details')?.open).toBe(false);
    });

    it('draws that history inline where the message is what is being read on its own', async () => {
        answering(replyQuotingWhatItAnswers);

        readingOneMessage();

        expect(await screen.findByText('The message it answers.')).toBeDefined();
        expect(screen.queryByText('The conversation this message quoted')).toBeNull();
    });

    // Opening a message is its words having reached the screen, which is what ADR 0026 marks read on. It is said once
    // however many times the effect runs: `StrictMode` invokes it twice on mount, and saying it twice would be this
    // client reporting a message opened that nobody opened again.
    it('says the body was drawn once the message is on the screen, and says it once', async () => {
        const drawn: number[] = [];

        render(readingReported(() => drawn.push(1)));
        await screen.findByText('A drawn message.');

        await waitFor(() => {
            expect(drawn).toHaveLength(1);
        });
    });

    it('says nothing for a read the reader moved past before it answered', () => {
        answer = () => new Promise<Answer>(() => undefined);

        const drawn: number[] = [];
        const opened = render(readingReported(() => drawn.push(1)));

        opened.unmount();

        expect(drawn).toStrictEqual([]);
    });

    it('says nothing for a message that could not be read, nothing having been put in front of anybody', async () => {
        answering({ status: 503, body: '' });

        const drawn: number[] = [];

        render(readingReported(() => drawn.push(1)));
        await screen.findByText('The message could not be read: unavailable.');

        expect(drawn).toStrictEqual([]);
    });

    // Asking for the sender's pictures re-reads the same message, and the reader did not open it twice.
    it('says nothing again when the reader asks for the sender’s pictures', async () => {
        const drawn: number[] = [];

        render(readingReported(() => drawn.push(1)));
        await screen.findByText('A drawn message.');
        answering(bodyAnswering(true));
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));
        await screen.findByText('Pictures are being loaded from the sender for this message.');

        await waitFor(() => {
            expect(drawn).toHaveLength(1);
        });
    });

    it('says it again for the next message the reader opens', async () => {
        const drawn: number[] = [];
        const opened = render(readingReported(() => drawn.push(1)));

        await screen.findByText('A drawn message.');
        opened.rerender(readingReported(() => drawn.push(1), 'another-message'));
        await screen.findByText('A drawn message.');

        await waitFor(() => {
            expect(drawn).toHaveLength(2);
        });
    });
});

// The read is composed here, so which view a reader chose is part of what is asked for rather than something the
// drawing decides afterwards. That makes the representation cost a read — which is what these are about: it is asked
// for only where it will be drawn, and a view changed back draws what is already held rather than reading again.
describe('Message and the view a reader chose', () => {
    beforeEach(() => {
        answering(bodyAnswering(false));
        asked = [];
    });

    it('asks for nothing but the reduced tree for a reader who chose it', async () => {
        render(readingUnder(false));

        await screen.findByText('A drawn message.');

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body`]);
    });

    it('asks for the sender’s own markup only for a reader who chose it', async () => {
        answeringWithMarkupWhenAsked();

        render(readingUnder(true));

        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`]);
    });

    it('reads the message again when a reader changes to the view the answer in hand cannot draw', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder(false));
        await screen.findByText('A drawn message.');

        opened.rerender(readingUnder(true));
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(readsAsked()).toStrictEqual([
            `${baseAddress}/api/client/messages/stub-message/body`,
            `${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`,
        ]);
    });

    // What the wait is reported as belongs to the read that started it. The pictures are the one ask with a surface of
    // its own — the button, with the wait beneath it — so a read begun by changing the view must not borrow it.
    it('says the pictures are loading while the ask for them is in flight', async () => {
        render(readingUnder(false));
        await screen.findByText('A drawn message.');

        answer = () => new Promise<Answer>(() => undefined);
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));

        expect(await screen.findByText('Loading them…')).toBeDefined();
    });

    it('says nothing about the pictures while the read a changed view started is in flight', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder(false));
        await screen.findByText('A drawn message.');

        answer = () => new Promise<Answer>(() => undefined);
        opened.rerender(readingUnder(true));

        expect(screen.getByRole('button', { name: 'Load pictures from the sender' })).toBeDefined();
        expect(screen.queryByText('Loading them…')).toBeNull();
    });

    it('draws the reduced tree from the answer it already holds rather than reading the message again', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder(true));
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        opened.rerender(readingUnder(false));
        await screen.findByText('A drawn message.');

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`]);
    });
});
