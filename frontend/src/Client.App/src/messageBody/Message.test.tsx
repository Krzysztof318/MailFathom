// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { ClientMessageView, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { MessageViewContext } from '../preferences/messageView';
import { Message } from './Message';
import { useMessageBody } from './useMessageBody';

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

// A surface reading one message, which is what every test below is holding: the read is started where the surface
// mounts and the component under test draws what it produced, exactly as the reading pane and a conversation do it.
function ReadMessage({
    storedEmailId,
    quotedHistoryOnRequest = false,
    onBodyDrawn = () => undefined,
}: {
    readonly storedEmailId: string;
    readonly quotedHistoryOnRequest?: boolean;
    readonly onBodyDrawn?: () => void;
}) {
    return (
        <Message
            body={useMessageBody(session, transport, storedEmailId, true)}
            storedEmailId={storedEmailId}
            quotedHistoryOnRequest={quotedHistoryOnRequest}
            onBodyDrawn={onBodyDrawn}
        />
    );
}

function readingOneMessage(storedEmailId = 'stub-message') {
    return render(reading(storedEmailId));
}

/** Everything a message is drawn inside of, which is what the composition root supplies around one. */
function Screen({ children }: { readonly children: ReactNode }) {
    return (
        <StrictMode>
            <LocalizationProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>{children}</LinkOpenerContext>
            </LocalizationProvider>
        </StrictMode>
    );
}

function reading(storedEmailId: string) {
    return (
        <Screen>
            <ReadMessage storedEmailId={storedEmailId} />
        </Screen>
    );
}

/** The same message read as a conversation reads one, where the history it quoted is folded away. */
function readingInAConversation() {
    return render(
        <Screen>
            <ReadMessage storedEmailId="stub-message" quotedHistoryOnRequest />
        </Screen>,
    );
}

/** The same message, with somebody listening for its words having reached the screen. */
function readingReported(onBodyDrawn: () => void, storedEmailId = 'stub-message') {
    return (
        <Screen>
            <ReadMessage storedEmailId={storedEmailId} onBodyDrawn={onBodyDrawn} />
        </Screen>
    );
}

/** The same message read under whichever of the three renderings a reader chose. */
function readingUnder(view: ClientMessageView) {
    return (
        <Screen>
            <MessageViewContext value={view}>
                <ReadMessage storedEmailId="stub-message" />
            </MessageViewContext>
        </Screen>
    );
}

/** The cleaned rendering of the same message: the one block worth reading, with the sender's wrapper dropped. */
const cleanedDocument = {
    ...readableBody.document,
    blocks: [
        {
            type: 'paragraph',
            version: 1,
            content: [{ text: 'Only the part worth reading.', emphasis: 'None', foreground: null, link: null }],
            alignment: 'Inherited',
        },
    ],
};

// A deployment that answers the cleaned route with one outcome and the body route with the ordinary reduced document.
// The two are separate reads of the same message, which is what lets the pane draw the second while the first is still
// out — so a double that answered one of them for both would prove nothing about the wait.
function answeringCleaning(cleaning: string, document: unknown = cleanedDocument): void {
    answer = (path) =>
        Promise.resolve(
            path.includes('/body/cleaned')
                ? { status: 200, body: JSON.stringify({ storedEmailId: 'stub-message', cleaning, document }) }
                : bodyAnswering(false),
        );
}

/** The same deployment with the derivation still running, which is the wait the pane has to say out loud. */
function answeringCleaningNotYet(): void {
    answer = (path) =>
        path.includes('/body/cleaned') ? new Promise<Answer>(() => undefined) : Promise.resolve(bodyAnswering(false));
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

    // The words standing in that space are held against the design as images rather than asserted here: they are
    // `aria-hidden` by construction and jsdom computes no layout, so what this suite can say about the wait is what a
    // person meets — the sentence while the read is in flight, and no message until one has arrived.
    it('says the message is opening while the read is in flight', () => {
        // An answer that never comes, which is the whole of the state under test.
        answer = () => new Promise<Answer>(() => undefined);

        readingOneMessage();

        expect(screen.getByRole('status').textContent).toBe('Opening the message…');
        expect(screen.queryByText('A drawn message.')).toBeNull();
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
        render(readingUnder('reduced'));

        await screen.findByText('A drawn message.');

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body`]);
    });

    it('asks for the sender’s own markup only for a reader who chose it', async () => {
        answeringWithMarkupWhenAsked();

        render(readingUnder('embeddedHtml'));

        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`]);
    });

    it('reads the message again when a reader changes to the view the answer in hand cannot draw', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        opened.rerender(readingUnder('embeddedHtml'));
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(readsAsked()).toStrictEqual([
            `${baseAddress}/api/client/messages/stub-message/body`,
            `${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`,
        ]);
    });

    // What the wait is reported as belongs to the read that started it. The pictures are the one ask with a surface of
    // its own — the button, with the wait beneath it — so a read begun by changing the view must not borrow it.
    it('says the pictures are loading while the ask for them is in flight', async () => {
        render(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        answer = () => new Promise<Answer>(() => undefined);
        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));

        expect(await screen.findByText('Loading them…')).toBeDefined();
    });

    it('says nothing about the pictures while the read a changed view started is in flight', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        answer = () => new Promise<Answer>(() => undefined);
        opened.rerender(readingUnder('embeddedHtml'));

        expect(screen.getByRole('button', { name: 'Load pictures from the sender' })).toBeDefined();
        expect(screen.queryByText('Loading them…')).toBeNull();
    });

    it('draws the reduced tree from the answer it already holds rather than reading the message again', async () => {
        answeringWithMarkupWhenAsked();

        const opened = render(readingUnder('embeddedHtml'));
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        opened.rerender(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`]);
    });
});

// The third rendering is a read of its own, so what it owes a reader is the pair the other two never needed: a wait said
// out loud over a document already on the screen, and a sentence wherever the cleaning did not happen. A pane that is
// empty, or silent, while a model decides is the failure these are about.
describe('Message and the cleaned rendering', () => {
    beforeEach(() => {
        answering(bodyAnswering(false));
        asked = [];
    });

    it('draws the cleaned document for a reader who chose it', async () => {
        answeringCleaning('Cleaned');

        render(readingUnder('cleaned'));

        expect(await screen.findByText('Only the part worth reading.')).toBeDefined();
    });

    it('asks the cleaned route beside the body rather than instead of it', async () => {
        answeringCleaning('Cleaned');

        render(readingUnder('cleaned'));
        await screen.findByText('Only the part worth reading.');

        expect(readsAsked()).toStrictEqual([
            `${baseAddress}/api/client/messages/stub-message/body`,
            `${baseAddress}/api/client/messages/stub-message/body/cleaned`,
        ]);
    });

    it('asks for no cleaning at all for a reader on the reduced document', async () => {
        render(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body`]);
    });

    it('asks for no cleaning for a reader being shown the sender’s own markup', async () => {
        answeringWithMarkupWhenAsked();

        render(readingUnder('embeddedHtml'));
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(readsAsked()).toStrictEqual([`${baseAddress}/api/client/messages/stub-message/body?fullHtml=true`]);
    });

    it('says it is deciding what to keep, over the document already on the screen', async () => {
        answeringCleaningNotYet();

        render(readingUnder('cleaned'));

        expect(await screen.findByText('Deciding what of this message to keep…')).toBeDefined();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    it('says nothing about the cleaning once one has been drawn', async () => {
        answeringCleaning('Cleaned');

        render(readingUnder('cleaned'));
        await screen.findByText('Only the part worth reading.');

        expect(screen.queryByText('Deciding what of this message to keep…')).toBeNull();
    });

    // A cleaning that found nothing to drop has already drawn the document it would have composed, so a sentence there
    // would report a failure to somebody who is reading exactly what they asked for.
    it('says nothing where there was nothing to clean, and draws the reduced document', async () => {
        answeringCleaning('NothingToClean', null);

        render(readingUnder('cleaned'));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByText(/simplified version is shown/u)).toBeNull();
    });

    it.each([
        [
            'NotActivated',
            'This deployment does not simplify messages with a model, so the simplified version is shown.',
        ],
        [
            'AllowanceExhausted',
            "This deployment's allowance for the period is spent, so the simplified version is shown.",
        ],
        ['ProviderUnavailable', 'The model this deployment asks did not answer, so the simplified version is shown.'],
        ['AnswerRejected', 'The answer the model gave could not be used, so the simplified version is shown.'],
    ])('states %s over the reduced document rather than leaving the pane empty', async (cleaning, said) => {
        answeringCleaning(cleaning, null);

        render(readingUnder('cleaned'));

        expect(await screen.findByText(said)).toBeDefined();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    // The sentence replaces a wait that was announced, and arrives just as late, so a reader who has moved past the top
    // of the message is told about it the same way rather than only shown it.
    it('announces the reason it fell back rather than only drawing it', async () => {
        answeringCleaning('ProviderUnavailable', null);

        render(readingUnder('cleaned'));

        const said = await screen.findByText(
            'The model this deployment asks did not answer, so the simplified version is shown.',
        );

        expect(said.getAttribute('role')).toBe('status');
    });

    // The reduced document is drawn and readable while the derivation runs, so the line above it holds its place for as
    // long as the rendering is in force: a sentence that appeared and then vanished would move the message twice under
    // the cursor of somebody already reading it.
    it('holds the line above the message open once a cleaning it says nothing about has been drawn', async () => {
        answeringCleaning('Cleaned');

        render(readingUnder('cleaned'));
        await screen.findByText('Only the part worth reading.');

        expect(screen.getByRole('status').textContent).toBe('');
    });

    it('states a cleaning that could not be read at all, and draws the reduced document', async () => {
        answer = (path) =>
            Promise.resolve(path.includes('/body/cleaned') ? { status: 503, body: '' } : bodyAnswering(false));

        render(readingUnder('cleaned'));

        expect(
            await screen.findByText(
                'This message could not be simplified by a model, so the simplified version is shown.',
            ),
        ).toBeDefined();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    // Nothing is written down, which is what the issue asking for this rendering requires: a second open asks the
    // deployment to decide again rather than reading back what it decided the first time.
    it('asks again when a reader changes to the cleaned rendering over a message already drawn', async () => {
        answeringCleaning('Cleaned');

        const opened = render(readingUnder('reduced'));
        await screen.findByText('A drawn message.');

        opened.rerender(readingUnder('cleaned'));
        await screen.findByText('Only the part worth reading.');

        expect(readsAsked()).toStrictEqual([
            `${baseAddress}/api/client/messages/stub-message/body`,
            `${baseAddress}/api/client/messages/stub-message/body/cleaned`,
        ]);
    });
});
