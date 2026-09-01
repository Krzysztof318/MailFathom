// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailBody, MailDocument } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageBody } from './MessageBody';
import { LinkOpenerContext } from '../shellOperations/linkOpener';

const drawnDocument: MailDocument = {
    blocks: [
        {
            type: 'paragraph',
            content: [
                {
                    text: 'A drawn message.',
                    emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
                    foreground: null,
                    link: null,
                },
            ],
            alignment: 'Inherited',
        },
    ],
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

function drawing(body: MailBody, onShowRemotePictures: () => void = () => undefined, asking = false) {
    return render(inThePane(body, onShowRemotePictures, asking));
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
        ['NoHtmlPart', 'The sender wrote no formatted version of this message, so it is shown as words.'],
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
                'This message asked to load content from another server. It was removed, so opening it reported nothing to the sender.',
            ),
        ).toBeDefined();
        expect(screen.getByText('References removed: 3')).toBeDefined();
        expect(
            screen.getByText(
                'Loading them tells the sender that you opened this message. It is asked for this message alone and remembered nowhere.',
            ),
        ).toBeDefined();
    });

    it('asks its caller to re-read the message when the reader asks for the pictures', () => {
        let asked = 0;
        drawing({ ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } }, () => {
            asked += 1;
        });

        fireEvent.click(screen.getByRole('button', { name: 'Load pictures from the sender' }));

        expect(asked).toBe(1);
    });

    it('keeps the button somebody pressed on the screen while the read it started is in flight', () => {
        drawing({ ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } }, () => undefined, true);

        const asking = screen.getByRole('button', { name: 'Load pictures from the sender' });

        expect(asking.getAttribute('aria-disabled')).toBe('true');
        expect(screen.getByText('Loading them…')).toBeDefined();
    });

    it('offers nothing to load and says nothing was removed when the message asked for nothing', () => {
        drawing(readable);

        expect(screen.queryByRole('button', { name: 'Load pictures from the sender' })).toBeNull();
        expect(screen.queryByText(/asked to load content from another server/)).toBeNull();
    });

    it('says pictures are being loaded on the read the reader asked for them', () => {
        drawing({
            ...readable,
            remoteImagesRequested: true,
            document: { ...drawnDocument, retainedRemoteImageCount: 2 },
        });

        expect(screen.getByText('Pictures are being loaded from the sender for this message.')).toBeDefined();
        expect(screen.getByText('Pictures loaded from the sender: 2')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Load pictures from the sender' })).toBeNull();
    });

    it('takes the focus of whoever pressed the button the answered notice replaces', () => {
        const asking = { ...readable, document: { ...drawnDocument, removedRemoteReferenceCount: 1 } };
        const answered = {
            ...readable,
            remoteImagesRequested: true,
            document: { ...drawnDocument, retainedRemoteImageCount: 1 },
        };
        const drawn = drawing(asking);
        screen.getByRole('button', { name: 'Load pictures from the sender' }).focus();

        drawn.rerender(inThePane(answered));

        expect(document.activeElement).toBe(
            screen.getByText('Pictures are being loaded from the sender for this message.').parentElement,
        );
    });

    it('takes the focus of nobody where the notice is on the screen from the first paint', () => {
        const before = document.activeElement;

        drawing({
            ...readable,
            remoteImagesRequested: true,
            document: { ...drawnDocument, retainedRemoteImageCount: 1 },
        });

        expect(document.activeElement).toBe(before);
    });

    it('says how many of the message own pictures a bound left undrawn', () => {
        drawing({ ...readable, document: { ...drawnDocument, undrawnInlineImageCount: 4 } });

        expect(screen.getByText('Pictures too large to draw: 4')).toBeDefined();
    });
});
