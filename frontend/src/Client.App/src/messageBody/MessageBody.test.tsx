// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailBody, MailBodyText, MailDocument, MailDocumentBlock } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageBody } from './MessageBody';
import { LinkOpenerContext } from '../shellOperations/linkOpener';

function written(text: string): MailDocumentBlock {
    return {
        type: 'paragraph',
        content: [
            {
                text,
                emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
                foreground: null,
                link: null,
            },
        ],
        alignment: 'Inherited',
    };
}

const drawnDocument: MailDocument = {
    blocks: [written('A drawn message.')],
    refusal: 'None',
    removedRemoteReferenceCount: 0,
    retainedRemoteImageCount: 0,
    inlineImageCount: 0,
    undrawnInlineImageCount: 0,
    truncated: false,
};

const readable: MailBody = {
    storedEmailId: 'a-message',
    availability: 'Readable',
    plainText: { text: 'A message, as words.', originalCharacterCount: 20, truncation: 'None' },
    document: drawnDocument,
    selfContainedHtml: null,
    remoteImagesRequested: false,
};

function inThePane(body: MailBody, onShowRemotePictures: () => void = () => undefined, asking = false) {
    return (
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <MessageBody body={body} asking={asking} onShowRemotePictures={onShowRemotePictures} />
            </LinkOpenerContext>
        </LocalizationProvider>
    );
}

/** The same body drawn as a conversation draws one, where the history a message quoted is folded away. */
function inAConversation(body: MailBody) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <MessageBody body={body} asking={false} quotedHistoryOnRequest onShowRemotePictures={() => undefined} />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

/** A reply: what somebody wrote, above the message they were answering. */
const replyQuotingWhatItAnswers: MailBody = {
    ...readable,
    document: {
        ...drawnDocument,
        blocks: [...drawnDocument.blocks, { type: 'quote', depth: 1, blocks: [written('The message it answers.')] }],
    },
};

function drawing(body: MailBody, onShowRemotePictures: () => void = () => undefined, asking = false) {
    return render(inThePane(body, onShowRemotePictures, asking));
}

/** The representation the service serves a reader who chose the sender's own markup, and a body carrying one. */
const markup: MailBodyText = {
    text: '<html><head></head><body><p>As the sender wrote it.</p></body></html>',
    originalCharacterCount: 69,
    truncation: 'None',
};

const withMarkup: MailBody = { ...readable, selfContainedHtml: markup };

/** The same body drawn for a reader whose messages are the sender's own markup rather than the reduced text. */
function inTheEmbeddedView(body: MailBody, onShowRemotePictures: () => void = () => undefined) {
    return (
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <MessageBody body={body} asking={false} embeddedHtml onShowRemotePictures={onShowRemotePictures} />
            </LinkOpenerContext>
        </LocalizationProvider>
    );
}

describe('MessageBody', () => {
    it('draws the document when the reduction produced one', () => {
        drawing(readable);

        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    it.each([
        ['EncryptedNotReadableLocally', 'This message is encrypted and this deployment cannot read it.'],
        [
            'NotStoredExceededSizeLimit',
            'This message was larger than this deployment keeps, so its body was not stored.',
        ],
        ['NotStoredAwaitingStorageHeadroom', 'This message is waiting for storage room before its body is kept.'],
    ] as const satisfies readonly (readonly [MailBody['availability'], string])[])(
        'says what state a body it cannot show is in, for %s',
        (availability, sentence) => {
            drawing({
                ...readable,
                availability,
                document: null,
                plainText: { text: '', originalCharacterCount: 0, truncation: 'None' },
            });

            expect(screen.getByText(sentence)).toBeDefined();
        },
    );

    it.each([
        [
            'ReductionFailed',
            'This deployment could not read the formatted version of this message, so it is shown as words.',
        ],
        ['NothingRenderable', 'The formatted version of this message held nothing to draw, so it is shown as words.'],
    ] as const satisfies readonly (readonly [MailDocument['refusal'], string])[])(
        'reads a refused document as its words and names the reason, for %s',
        (refusal, sentence) => {
            drawing({ ...readable, document: { ...drawnDocument, blocks: [], refusal } });

            expect(screen.getByText(sentence)).toBeDefined();
            expect(screen.getByText('A message, as words.')).toBeDefined();
        },
    );

    // A sender who wrote no formatted version is not a refusal a reader is owed a sentence about: plain text is what
    // that message is, and the design draws it as the message rather than as a message with a note over it.
    it('draws a message written as text alone without a note over it', () => {
        drawing({ ...readable, document: { ...drawnDocument, blocks: [], refusal: 'NoHtmlPart' } });

        expect(screen.getByText('A message, as words.')).toBeDefined();
        expect(
            screen.queryByText('The sender wrote no formatted version of this message, so it is shown as words.'),
        ).toBeNull();
    });

    it('names a reason when the deployment sent no document at all rather than falling back silently', () => {
        drawing({ ...readable, document: null });

        expect(
            screen.getByText('This deployment sent no drawable version of this message, so it is shown as words.'),
        ).toBeDefined();
        expect(screen.getByText('A message, as words.')).toBeDefined();
    });

    it('says the words were cut short when a bound reached them', () => {
        drawing({
            ...readable,
            document: { ...drawnDocument, blocks: [], refusal: 'NoHtmlPart' },
            plainText: { text: 'The start of it', originalCharacterCount: 90_000, truncation: 'BodyCharacterLimit' },
        });

        expect(
            screen.getByText('The words of this message were cut short by a limit this deployment applies.'),
        ).toBeDefined();
    });

    it('says the message stops early when the reduction reached a bound', () => {
        drawing({ ...readable, document: { ...drawnDocument, truncated: true } });

        expect(screen.getByText('This message is longer than a reading pane draws, so it stops here.')).toBeDefined();
    });

    it('says what the message asked to load from another server, and what that would reveal', () => {
        drawing({ ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 3 } });

        expect(
            screen.getByText(
                'This message asked to fetch content from another server. That was removed, so opening it told the sender nothing.',
            ),
        ).toBeDefined();
        expect(screen.getByText('Removed references: 3')).toBeDefined();
        expect(
            screen.getByText(
                'Loading them tells the sender that this message was opened. This choice applies to this message only and is not remembered anywhere.',
            ),
        ).toBeDefined();
    });

    it('asks its caller to re-read the message when the reader asks for the pictures', () => {
        let asked = 0;
        drawing({ ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } }, () => {
            asked += 1;
        });

        fireEvent.click(screen.getByRole('button', { name: 'Load images from the sender' }));

        expect(asked).toBe(1);
    });

    it('keeps the button somebody pressed on the screen while the read it started is in flight', () => {
        drawing({ ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } }, () => undefined, true);

        const asking = screen.getByRole('button', { name: 'Load images from the sender' });

        expect(asking.getAttribute('aria-disabled')).toBe('true');
        expect(screen.getByText('Loading them…')).toBeDefined();
    });

    it('offers nothing to load and says nothing was removed when the message asked for nothing', () => {
        drawing(readable);

        expect(screen.queryByRole('button', { name: 'Load images from the sender' })).toBeNull();
        expect(screen.queryByText(/asked to fetch content from another server/)).toBeNull();
    });

    // Once the reader asked, the card has done its work: what is left is one quiet line, as the design draws it, rather
    // than a second card competing with the message it is about.
    it('says in one line that the sender\u2019s images are loaded on the read the reader asked for them', () => {
        drawing({
            ...readable,
            remoteImagesRequested: true,
            document: { ...drawnDocument, retainedRemoteImageCount: 2 },
        });

        const loaded = screen.getByText('Images from the sender are loaded for this message.');

        expect(loaded.tagName).toBe('P');
        expect(screen.queryByText(/Removed references/)).toBeNull();
        expect(screen.queryByRole('button', { name: 'Load images from the sender' })).toBeNull();
    });

    const messageRegion = 'The racking quote';

    // The line is a statement rather than a notice to act on, so it takes no focus itself and is announced instead. The
    // focus of whoever pressed the button it replaces goes to the message the line stands in, drawn here the way the
    // reading pane and a conversation draw one: a region that takes focus without being a stop.
    it('announces the line that replaces the card, and hands the keyboard to the message rather than to it', async () => {
        const asking = { ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } };
        const answered = {
            ...readable,
            remoteImagesRequested: true,
            document: { ...drawnDocument, retainedRemoteImageCount: 1 },
        };
        const inTheMessage = (body: MailBody) => (
            <article aria-label={messageRegion} tabIndex={-1}>
                {inThePane(body)}
            </article>
        );
        const drawn = render(inTheMessage(asking));
        screen.getByRole('button', { name: 'Load images from the sender' }).focus();

        drawn.rerender(inTheMessage(answered));

        const loaded = screen.getByRole('status');

        expect(loaded.textContent).toBe('Images from the sender are loaded for this message.');
        expect(loaded.hasAttribute('tabindex')).toBe(false);
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('article', { name: messageRegion }));
        });
    });

    it('moves nobody’s focus where the message opens with the pictures already asked for', () => {
        render(
            <article aria-label={messageRegion} tabIndex={-1}>
                {inThePane({ ...readable, remoteImagesRequested: true, document: drawnDocument })}
            </article>,
        );

        expect(screen.getByRole('status')).toBeDefined();
        expect(document.activeElement).toBe(document.body);
    });

    it('says how many of the message own pictures a bound left undrawn', () => {
        drawing({ ...readable, document: { ...drawnDocument, undrawnInlineImageCount: 4 } });

        expect(screen.getByText('Pictures too large to draw: 4')).toBeDefined();
    });

    it('draws the history a reply quoted inline where one message is what is being read', () => {
        drawing(replyQuotingWhatItAnswers);

        expect(screen.getByText('The message it answers.')).toBeDefined();
        expect(screen.queryByText('The conversation this message quoted')).toBeNull();
    });

    it('folds the history a reply quoted away in a conversation, where the message it quotes is a row of its own', () => {
        inAConversation(replyQuotingWhatItAnswers);

        expect(screen.getByText('A drawn message.')).toBeDefined();
        // What is folded is still in the document, so the assertion is on the disclosure being shut rather than on the
        // words being absent: a browser is what hides them, and jsdom draws no geometry.
        expect(screen.getByText('The conversation this message quoted').closest('details')?.open).toBe(false);
    });

    it('draws the history a reader asks for', async () => {
        inAConversation(replyQuotingWhatItAnswers);

        const quoted = screen.getByText('The conversation this message quoted');
        fireEvent.click(quoted);

        await waitFor(() => {
            expect(quoted.closest('details')?.open).toBe(true);
        });
        expect(screen.getByText('The message it answers.')).toBeDefined();
    });

    it('folds nothing away in a conversation where the message quoted nothing', () => {
        inAConversation(readable);

        expect(screen.getByText('A drawn message.')).toBeDefined();
        expect(screen.queryByText('The conversation this message quoted')).toBeNull();
    });

    // The second reading surface. What is asserted below is which of the two a message is drawn as, and what a reader
    // is told where the one they chose has nothing to draw — never that the markup rendered, which is a browser's
    // answer and the browser suite's to give.
    describe('drawn as the sender wrote it', () => {
        it('draws the reduced tree for a reader who chose it, whatever the deployment served beside it', () => {
            render(inThePane(withMarkup, () => undefined));

            expect(screen.getByText('A drawn message.')).toBeDefined();
            expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
        });

        it('draws the sender’s own markup for a reader who chose it', () => {
            render(inTheEmbeddedView(withMarkup));

            const frame = screen.getByTitle("The sender's own markup, drawn in isolation");

            expect(frame.getAttribute('srcdoc')).toContain('<p>As the sender wrote it.</p>');
            expect(screen.queryByText('A drawn message.')).toBeNull();
        });

        it('falls back to the reduced tree, saying so, where the deployment served no markup for this message', () => {
            render(inTheEmbeddedView(readable));

            expect(screen.getByText(/^The sender wrote no formatted version/u)).toBeDefined();
            expect(screen.getByText('A drawn message.')).toBeDefined();
            expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
        });

        it('falls back to the reduced tree, naming the bound, where the markup was cut short', () => {
            render(
                inTheEmbeddedView({
                    ...withMarkup,
                    selfContainedHtml: { ...markup, truncation: 'BodyCharacterLimit' },
                }),
            );

            expect(screen.getByText(/longer than one read returns/u)).toBeDefined();
            expect(screen.getByText('A drawn message.')).toBeDefined();
        });

        it('offers the ask for the sender’s pictures here too, this being the only place it can be made', () => {
            let asked = 0;
            render(
                inTheEmbeddedView(
                    { ...withMarkup, document: { ...drawnDocument, removedRemoteReferenceCount: 2 } },
                    () => {
                        asked += 1;
                    },
                ),
            );

            expect(screen.getByTitle("The sender's own markup, drawn in isolation")).toBeDefined();
            fireEvent.click(screen.getByRole('button', { name: 'Load images from the sender' }));

            expect(asked).toBe(1);
        });

        it('names the pictures rather than the length where that is the bound the markup met', () => {
            render(
                inTheEmbeddedView({
                    ...withMarkup,
                    selfContainedHtml: { ...markup, truncation: 'InlineImageOctetLimit' },
                }),
            );

            expect(screen.getByText(/^Some of the pictures this message carried/u)).toBeDefined();
        });
    });
});
