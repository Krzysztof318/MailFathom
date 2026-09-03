// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailAttachment } from '@mailfathom/client-backend';
import {
    AttachmentExchangeContext,
    type AttachmentExchange,
    type AttachmentDeliveryOutcome,
} from '../deployment/attachmentExchange';
import { LocalizationProvider } from '../localization/Localization';
import { OpenAttachmentContext, type OpenedAttachment } from '../workspace/openAttachment';
import { Attachments } from './Attachments';

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

const photograph: MailAttachment = {
    position: 2,
    fileName: 'photo.jpg',
    wasFileNameNormalized: false,
    mediaType: 'image/jpeg',
    sizeOctets: 4_096,
};

/** What a download was asked to do, so a test asserts on the request and the name rather than on a call being made. */
interface Asked {
    readonly request: ClientRequest;
    readonly fileName: string;
    readonly arrived: (octets: number) => void;
    readonly abandoned: AbortSignal;
}

/** An exchange whose delivery records what it was asked and answers when the test says so, never on its own. */
function deliveryHeldOpen(): {
    exchange: AttachmentExchange;
    asked: Asked[];
    answer: (outcome: AttachmentDeliveryOutcome, at?: number) => void;
} {
    const asked: Asked[] = [];
    const settling: ((outcome: AttachmentDeliveryOutcome) => void)[] = [];

    return {
        asked,
        answer: (outcome, at) => {
            settling[at ?? settling.length - 1]?.(outcome);
        },
        exchange: {
            deliver: (request, fileName, arrived, abandoned) => {
                asked.push({ request, fileName, arrived, abandoned });

                return new Promise<AttachmentDeliveryOutcome>((resolve) => {
                    settling.push(resolve);
                });
            },

            // The strip never shows a file, so a read reaching this is a defect rather than a case to answer.
            read: () => Promise.reject(new Error('The strip asked to show a file rather than to download one.')),
        },
    };
}

function drawing(
    attachments: readonly MailAttachment[],
    exchange: AttachmentExchange,
    open: (opened: OpenedAttachment) => void = () => undefined,
) {
    return render(
        <LocalizationProvider>
            <AttachmentExchangeContext value={exchange}>
                <OpenAttachmentContext value={open}>
                    <Attachments session={session} storedEmailId={messageId} attachments={attachments} />
                </OpenAttachmentContext>
            </AttachmentExchangeContext>
        </LocalizationProvider>,
    );
}

describe('Attachments', () => {
    it('fetches nothing until a download is asked for', () => {
        const held = deliveryHeldOpen();
        drawing([invoice, photograph], held.exchange);

        expect(held.asked).toEqual([]);
    });

    it('asks for the file at the position the message described it at, under the size it stated', () => {
        const held = deliveryHeldOpen();
        drawing([invoice, photograph], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download photo.jpg' }));

        expect(held.asked[0]?.request).toEqual({
            method: 'GET',
            path: `https://mail.example.invalid/api/client/messages/${messageId}/attachments/2`,
            headers: { Accept: 'application/octet-stream', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 4_096,
        });
    });

    it('says how much has arrived while the file is still arriving', async () => {
        const held = deliveryHeldOpen();
        drawing([invoice], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        act(() => {
            held.asked[0]?.arrived(1_024);
        });

        expect(await screen.findByText(`${sizeReadAs(1_024)} of ${sizeReadAs(2_048)}`)).toBeDefined();
    });

    it('starts one download however often the control is pressed while that one is arriving', () => {
        const held = deliveryHeldOpen();
        drawing([invoice], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        expect(held.asked.length).toBe(1);
    });

    it('abandons a download in flight when the way out of it is taken', () => {
        const held = deliveryHeldOpen();
        drawing([invoice], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        fireEvent.click(screen.getByRole('button', { name: 'Stop downloading' }));

        expect(held.asked[0]?.abandoned.aborted).toBe(true);
    });

    it('says the file was downloaded once it has been', async () => {
        const held = deliveryHeldOpen();
        drawing([invoice], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));
        held.answer('delivered');

        expect(await screen.findByText('invoice.pdf was downloaded.')).toBeDefined();
    });

    // The viewer is handed the message the file came from as well as the file, because a part's position is its only
    // identity and it means nothing without the message it is a part of.
    it('hands the viewer the file and the message it came from when a chip is pressed', () => {
        const opened: OpenedAttachment[] = [];
        drawing([invoice, photograph], deliveryHeldOpen().exchange, (opening) => opened.push(opening));

        fireEvent.click(screen.getByRole('button', { name: 'Open photo.jpg' }));

        expect(opened).toEqual([{ storedEmailId: messageId, attachment: photograph }]);
    });

    it('offers no way to download everything where the message carries one file', () => {
        drawing([invoice], deliveryHeldOpen().exchange);

        expect(screen.queryByRole('button', { name: 'Download all' })).toBeNull();
    });

    it('downloads every file the message carries, one after the next', async () => {
        const held = deliveryHeldOpen();
        drawing([invoice, photograph], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download all' }));

        expect(held.asked.length).toBe(1);
        expect(held.asked[0]?.fileName).toBe('invoice.pdf');

        held.answer('delivered');
        await screen.findByText('invoice.pdf was downloaded.');

        expect(held.asked.length).toBe(2);
        expect(held.asked[1]?.fileName).toBe('photo.jpg');
    });

    it('asks for no further file once the message it belongs to has been closed', async () => {
        const held = deliveryHeldOpen();
        const { unmount } = drawing([invoice, photograph], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download all' }));
        unmount();

        // The answer the abandoned download settles with, and then the microtasks the loop would continue on: what is
        // being proven is that the file after it is never asked for, so the turns it would be asked in have to pass.
        await act(async () => {
            held.answer('abandoned');
            await Promise.resolve();
        });

        expect(held.asked.length).toBe(1);
    });

    it('reports a refusal against the file it refused and downloads the rest anyway', async () => {
        const held = deliveryHeldOpen();
        drawing([invoice, photograph], held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download all' }));
        held.answer('unavailable');
        await screen.findByText('The deployment did not answer, so the file was not downloaded. Try again.');

        held.answer('delivered');

        expect(await screen.findByText('photo.jpg was downloaded.')).toBeDefined();
        expect(screen.getAllByRole('alert').length).toBe(1);
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
