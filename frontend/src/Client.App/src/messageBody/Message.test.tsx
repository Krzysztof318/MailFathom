// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
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
    return render(
        <StrictMode>
            <LocalizationProvider>
                <Message session={session} transport={transport} storedEmailId={storedEmailId} />
            </LocalizationProvider>
        </StrictMode>,
    );
}

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

        opened.rerender(
            <StrictMode>
                <LocalizationProvider>
                    <Message session={session} transport={transport} storedEmailId="another-message" />
                </LocalizationProvider>
            </StrictMode>,
        );

        expect(await screen.findByText('Load pictures from the sender')).toBeDefined();
        expect(readsAsked()).toEqual([`${baseAddress}/api/client/messages/another-message/body`]);
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
});
