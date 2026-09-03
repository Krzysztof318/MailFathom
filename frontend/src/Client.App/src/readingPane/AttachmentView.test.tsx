// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailAttachment } from '@mailfathom/client-backend';
import {
    AttachmentExchangeContext,
    type AttachmentDeliveryOutcome,
    type AttachmentExchange,
    type AttachmentRead,
    type ShownAs,
} from '../deployment/attachmentExchange';
import { LocalizationProvider } from '../localization/Localization';
import type { OpenedAttachment } from '../workspace/openAttachment';
import { AttachmentView } from './AttachmentView';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const photograph: MailAttachment = {
    position: 2,
    fileName: 'harbour.png',
    wasFileNameNormalized: false,
    mediaType: 'image/png',
    sizeOctets: 4_096,
};

const note: MailAttachment = {
    position: 0,
    fileName: 'note.txt',
    wasFileNameNormalized: false,
    mediaType: 'text/plain; charset=utf-8',
    sizeOctets: 32,
};

const contract: MailAttachment = {
    position: 1,
    fileName: 'contract.pdf',
    wasFileNameNormalized: false,
    mediaType: 'application/pdf',
    sizeOctets: 200_000,
};

/** What a read was asked for, so a test asserts on the request and the form rather than on a call being made. */
interface Asked {
    readonly request: ClientRequest;
    readonly shown: ShownAs;
}

/** An exchange that records what it was asked to read and answers whatever the test lines up for it. */
function reading(...answers: readonly AttachmentRead[]): { exchange: AttachmentExchange; asked: Asked[] } {
    const asked: Asked[] = [];
    const waiting = [...answers];

    return {
        asked,
        exchange: {
            deliver: () => Promise.resolve('delivered'),
            read: (request, shown) => {
                asked.push({ request, shown });
                const answer = waiting.shift();

                return answer === undefined ? new Promise<AttachmentRead>(() => undefined) : Promise.resolve(answer);
            },
        },
    };
}

/** A delivery the test drives: it reports what it was asked and settles only when the test says so. */
function delivering(): {
    exchange: AttachmentExchange;
    arrived: (octets: number) => void;
    finish: (outcome: AttachmentDeliveryOutcome) => void;
} {
    let reporting: (octets: number) => void = () => undefined;
    let settling: (outcome: AttachmentDeliveryOutcome) => void = () => undefined;

    return {
        arrived: (octets) => {
            act(() => {
                reporting(octets);
            });
        },
        finish: (outcome) => {
            act(() => {
                settling(outcome);
            });
        },
        exchange: {
            deliver: (_request, _fileName, arrived) => {
                reporting = arrived;

                return new Promise<AttachmentDeliveryOutcome>((resolve) => {
                    settling = resolve;
                });
            },
            read: () => new Promise<AttachmentRead>(() => undefined),
        },
    };
}

function drawing(
    attachment: MailAttachment,
    exchange: AttachmentExchange,
    online = true,
    onClose: () => void = () => undefined,
): { show: (withNetwork: boolean) => void } {
    const opened: OpenedAttachment = { storedEmailId: messageId, attachment };

    const surface = (withNetwork: boolean) => (
        <LocalizationProvider>
            <AttachmentExchangeContext value={exchange}>
                <AttachmentView session={session} opened={opened} online={withNetwork} onClose={onClose} />
            </AttachmentExchangeContext>
        </LocalizationProvider>
    );

    const { rerender } = render(surface(online));

    return {
        show: (withNetwork) => {
            rerender(surface(withNetwork));
        },
    };
}

describe('AttachmentView', () => {
    it('names the file, its kind and its size before anything has arrived', () => {
        drawing(photograph, reading().exchange);

        expect(screen.getByRole('heading', { name: 'harbour.png' })).toBeDefined();
        expect(screen.getByText('image')).toBeDefined();
        expect(screen.getByText(sizeReadAs(4_096))).toBeDefined();
    });

    it('says it is reading while the file is still on its way', () => {
        drawing(photograph, reading().exchange);

        expect(screen.getByText('Reading harbour.png…')).toBeDefined();
    });

    it('asks for the file at the position the message described it at, under the size it stated', () => {
        const held = reading();
        drawing(photograph, held.exchange);

        expect(held.asked[0]?.request).toEqual({
            method: 'GET',
            path: `https://mail.example.invalid/api/client/messages/${messageId}/attachments/2`,
            headers: { Accept: 'application/octet-stream', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 4_096,
        });
        expect(held.asked[0]?.shown).toEqual({ as: 'picture' });
    });

    it('draws a picture at the address the read answered, named by the file rather than described', async () => {
        drawing(
            photograph,
            reading({ outcome: 'shown', content: 'data:application/octet-stream;base64,AQID' }).exchange,
        );

        const drawn = await screen.findByRole('img', { name: 'harbour.png' });

        expect(drawn.getAttribute('src')).toBe('data:application/octet-stream;base64,AQID');
    });

    it('draws text as the words the file holds', async () => {
        drawing(note, reading({ outcome: 'shown', content: 'the shipment leaves on Tuesday' }).exchange);

        expect(await screen.findByText('the shipment leaves on Tuesday')).toBeDefined();
    });

    it('asks for text under the character set the message declared', () => {
        const held = reading();
        drawing({ ...note, mediaType: 'text/plain; charset=iso-8859-2' }, held.exchange);

        expect(held.asked[0]?.shown).toEqual({ as: 'text', charset: 'iso-8859-2' });
    });

    it('says a file that holds nothing holds nothing, rather than drawing an empty surface', async () => {
        drawing(note, reading({ outcome: 'shown', content: '' }).exchange);

        expect(await screen.findByText('This file holds nothing.')).toBeDefined();
    });

    it('says a kind it does not show cannot be shown, and fetches nothing to find that out', () => {
        const held = reading();
        drawing(contract, held.exchange);

        expect(
            screen.getByText(
                'This client does not show files of this kind. Download it to open it in something that does.',
            ),
        ).toBeDefined();
        expect(held.asked).toEqual([]);
    });

    it('says a file larger than it draws is too large, and fetches nothing to find that out', () => {
        const held = reading();
        drawing({ ...photograph, sizeOctets: 64 * 1024 * 1024 }, held.exchange);

        expect(
            screen.getByText('This file is too large to show here. Download it to open it in something that does.'),
        ).toBeDefined();
        expect(held.asked).toEqual([]);
    });

    it('offers the download beside whatever it says, including for a file it will not show', () => {
        drawing(contract, reading().exchange);

        expect(screen.getByRole('button', { name: 'Download contract.pdf' })).toBeDefined();
    });

    // The download the head offers is the same act the row under a message offers, and it is the one thing on this
    // surface that keeps state of its own: what is being proven is that pressing it starts that download and that what
    // becomes of it is said here rather than in the message the file was opened from.
    it('downloads the file from the control in its head, and says what became of it', async () => {
        const held = delivering();
        drawing(photograph, held.exchange);

        fireEvent.click(screen.getByRole('button', { name: 'Download harbour.png' }));
        held.arrived(1_024);

        expect(screen.getByRole('progressbar', { name: 'How much of the file has arrived' })).toBeDefined();

        held.finish('delivered');

        expect(await screen.findByText('harbour.png was downloaded.')).toBeDefined();
    });

    it('says so and reads nothing while the machine has no network', () => {
        const held = reading();
        drawing(photograph, held.exchange, false);

        expect(
            screen.getByText(
                'This machine is offline, so this file cannot be opened. It opens on its own once the network comes back.',
            ),
        ).toBeDefined();
        expect(held.asked).toEqual([]);
    });

    // The network coming back starts the read again, and what is on the screen while that read is in flight has to be
    // the wait rather than the answer from before it: a surface that looks finished is one somebody acts on twice.
    it('says it is reading again when the network comes back rather than drawing the answer from before', async () => {
        const held = reading({ outcome: 'shown', content: 'data:application/octet-stream;base64,AQID' });
        const surface = drawing(photograph, held.exchange);
        await screen.findByRole('img', { name: 'harbour.png' });

        surface.show(false);
        surface.show(true);

        expect(screen.getByText('Reading harbour.png…')).toBeDefined();
        expect(screen.queryByRole('img', { name: 'harbour.png' })).toBeNull();
        expect(held.asked.length).toBe(2);
    });

    it('closes on the control that says so, which is the way back to the message it was opened from', () => {
        const closed: true[] = [];
        drawing(photograph, reading().exchange, true, () => closed.push(true));

        fireEvent.click(screen.getByRole('button', { name: 'Close harbour.png' }));

        expect(closed).toEqual([true]);
    });

    it.each([
        [
            'unauthenticated',
            'This deployment no longer accepts the credential, so the file could not be shown. Sign in again.',
        ],
        ['unauthorized', 'This credential may not read mail on this deployment, so the file could not be shown.'],
        ['unavailable', 'The deployment did not answer, so the file could not be shown. Try again.'],
        [
            'largerThanDescribed',
            'What arrived is not what this message said the file holds, so nothing is drawn from it. Download it, and report this as a defect.',
        ],
        [
            'unreadable',
            'What arrived is not what this message said the file holds, so nothing is drawn from it. Download it, and report this as a defect.',
        ],
    ] as const)('says what a read refused with %s could not do, and what to do about it', async (refusal, said) => {
        drawing(photograph, reading({ outcome: 'refused', refusal }).exchange);

        expect(await screen.findByText(said)).toBeDefined();
    });

    it('offers a second attempt at a deployment that did not answer, and reads again when it is taken', async () => {
        const held = reading(
            { outcome: 'refused', refusal: 'unavailable' },
            { outcome: 'shown', content: 'data:application/octet-stream;base64,AQID' },
        );
        drawing(photograph, held.exchange);

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(await screen.findByRole('img', { name: 'harbour.png' })).toBeDefined();
        expect(held.asked.length).toBe(2);
    });

    // The second attempt is a wait like the first one, so what stands on the screen while it runs is the wait rather
    // than the sentence somebody pressed past.
    it('says it is reading while the second attempt is in flight rather than keeping the refusal up', async () => {
        const held = reading({ outcome: 'refused', refusal: 'unavailable' });
        drawing(photograph, held.exchange);

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(screen.getByText('Reading harbour.png…')).toBeDefined();
        expect(screen.queryByRole('alert')).toBeNull();
        expect(held.asked.length).toBe(2);
    });

    it('offers no second attempt at a refusal that would repeat identically', async () => {
        drawing(photograph, reading({ outcome: 'refused', refusal: 'unauthorized' }).exchange);

        await screen.findByText(
            'This credential may not read mail on this deployment, so the file could not be shown.',
        );

        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('names a file the sender named nothing rather than leaving the surface unnamed', () => {
        drawing({ ...photograph, fileName: null }, reading().exchange);

        expect(screen.getByRole('heading', { name: 'Unnamed file' })).toBeDefined();
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
