// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailAttachment } from '@mailfathom/client-backend';
import {
    AttachmentDeliveryContext,
    type AttachmentDelivery,
    type AttachmentDeliveryOutcome,
} from '../deployment/attachmentDelivery';
import { LocalizationProvider } from '../localization/Localization';
import { Attachment } from './Attachment';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const invoice: MailAttachment = {
    position: 1,
    fileName: 'invoice.pdf',
    wasFileNameNormalized: false,
    mediaType: 'application/pdf',
    sizeOctets: 2_048,
};

/** What a download was asked to do, so a test asserts on the request and the name rather than on a call being made. */
interface Asked {
    readonly request: ClientRequest;
    readonly fileName: string;
    readonly arrived: (octets: number) => void;
    readonly abandoned: AbortSignal;
}

/** A delivery that records what it was asked and answers when the test says so, never on its own. */
function deliveryHeldOpen(): {
    deliver: AttachmentDelivery;
    asked: Asked[];
    answer: (outcome: AttachmentDeliveryOutcome) => void;
} {
    const asked: Asked[] = [];
    let settle: ((outcome: AttachmentDeliveryOutcome) => void) | null = null;

    return {
        asked,
        answer: (outcome) => {
            settle?.(outcome);
        },
        deliver: (request, fileName, arrived, abandoned) => {
            asked.push({ request, fileName, arrived, abandoned });

            return new Promise<AttachmentDeliveryOutcome>((resolve) => {
                settle = resolve;
            });
        },
    };
}

function drawing(attachment: MailAttachment, deliver: AttachmentDelivery): void {
    render(
        <LocalizationProvider>
            <AttachmentDeliveryContext value={deliver}>
                <ul>
                    <Attachment session={session} storedEmailId={messageId} attachment={attachment} />
                </ul>
            </AttachmentDeliveryContext>
        </LocalizationProvider>,
    );
}

describe('Attachment', () => {
    it('describes the file before anything is fetched, so a reader decides before it arrives', () => {
        drawing(invoice, () => Promise.resolve('delivered'));

        expect(screen.getByText('invoice.pdf')).toBeDefined();
        expect(screen.getByText('pdf')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Download invoice.pdf' }).getAttribute('title')).toBe(
            'application/pdf',
        );
        expect(screen.getByText(sizeReadAs(2_048))).toBeDefined();
    });

    it('names an unnamed part rather than offering a control with nothing to say', () => {
        drawing({ ...invoice, fileName: null }, () => Promise.resolve('delivered'));

        expect(screen.getByRole('button', { name: 'Download Unnamed file' })).toBeDefined();
    });

    it('names the kind of an unnamed part from what it declares itself to be', () => {
        drawing({ ...invoice, fileName: null, mediaType: 'image/svg+xml' }, () => Promise.resolve('delivered'));

        expect(screen.getByText('svg')).toBeDefined();
    });

    it('says where the sender wrote a file name the deployment would not use', () => {
        drawing({ ...invoice, wasFileNameNormalized: true }, () => Promise.resolve('delivered'));

        expect(
            screen.getByText(
                'The sender wrote a file name this deployment would not use, so what is shown is the name it was given instead.',
            ),
        ).toBeDefined();
    });

    it('fetches nothing until the download is asked for', () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        expect(held.asked).toEqual([]);
    });

    it('asks for the file at the position the message described it at, under the size it stated', () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        expect(held.asked[0]?.request).toEqual({
            method: 'GET',
            path: `https://mail.example.invalid/api/client/messages/${messageId}/attachments/1`,
            headers: { Accept: 'application/octet-stream', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 2_048,
        });
    });

    it('says how much has arrived while the file is still arriving', async () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        act(() => {
            held.asked[0]?.arrived(1_024);
        });

        expect(await screen.findByText(`${sizeReadAs(1_024)} of ${sizeReadAs(2_048)}`)).toBeDefined();
    });

    it('starts one download however often the control is pressed while that one is arriving', () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        expect(held.asked.length).toBe(1);
    });

    it('offers a way out of a download in flight, and abandons it when that is taken', () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        fireEvent.click(screen.getByRole('button', { name: 'Stop downloading' }));

        expect(held.asked[0]?.abandoned.aborted).toBe(true);
    });

    it('says the file was downloaded once it has been', async () => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        held.answer('delivered');

        expect(await screen.findByText('invoice.pdf was downloaded.')).toBeDefined();
    });

    it.each([
        [
            'unauthenticated',
            'This deployment no longer accepts the credential, so the file was not downloaded. Sign in again.',
        ],
        ['unauthorized', 'This credential may not read mail on this deployment, so the file was not downloaded.'],
        ['unavailable', 'The deployment did not answer, so the file was not downloaded. Try again.'],
        [
            'largerThanDescribed',
            'The deployment sent more than this message said the file holds, so nothing was saved. Report this as a defect.',
        ],
        ['abandoned', 'The download was stopped, so nothing was saved.'],
    ] as const)('says what became of a download that answered %s', async (outcome, said) => {
        const held = deliveryHeldOpen();
        drawing(invoice, held.deliver);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        held.answer(outcome);

        expect(await screen.findByText(said)).toBeDefined();
    });
});

// The size is `Intl`'s under the active language, so a test asks it the same question the screen asked rather than
// spelling out an answer that would be about this machine.
function sizeReadAs(octets: number): string {
    return new Intl.NumberFormat('en', {
        style: 'unit',
        unit: 'kilobyte',
        unitDisplay: 'short',
        maximumFractionDigits: 1,
    }).format(octets / 1_000);
}
