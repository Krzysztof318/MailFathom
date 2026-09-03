// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { FullHtmlSurface } from './FullHtmlSurface';

// The network boundary is the transport and it is the whole of what these tests fake, so the query the surface asks
// under, the parsing that reads the answer, and the failure mapping are all under test rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const description = JSON.stringify({
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
});

function body(markup: unknown, remoteImagesRequested = false): string {
    return JSON.stringify({
        storedEmailId: messageId,
        availability: 'Readable',
        plainText: { text: 'The invoice is attached.', originalCharacterCount: 24, truncation: 'None' },
        document: null,
        selfContainedHtml: markup,
        remoteImagesRequested,
    });
}

const asSent = { text: '<p>The invoice, as it was sent.</p>', originalCharacterCount: 35, truncation: 'None' };

/** A deployment answering both reads the surface makes, recording what each of them asked for. */
function deploymentServing(markup: unknown = asSent): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const answer: ClientResponse = request.path.includes('/body')
                ? { status: 200, body: body(markup, request.path.includes('remoteImages=true')), headers: {} }
                : { status: 200, body: description, headers: {} };

            return Promise.resolve(answer);
        },
    };
}

/** Draws the surface, and answers with the way to hand it a network that came or went afterwards. */
async function drawing(
    transport: MailFathomTransport,
    { onClose = () => undefined, online = true }: { onClose?: () => void; online?: boolean } = {},
): Promise<(nowOnline: boolean) => void> {
    const surface = (hasNetwork: boolean) => (
        <LocalizationProvider>
            <FullHtmlSurface
                session={session}
                transport={transport}
                storedEmailId={messageId}
                online={hasNetwork}
                onClose={onClose}
            />
        </LocalizationProvider>
    );

    const view = render(surface(online));

    await screen.findByRole('region', { name: "The sender's own version of this message" });

    return (nowOnline) => {
        view.rerender(surface(nowOnline));
    };
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

describe('FullHtmlSurface', () => {
    it('asks the deployment for the sender own markup rather than for the reduced tree alone', async () => {
        const { transport, asked } = deploymentServing();

        await drawing(transport);
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(asked.map((request) => request.path).filter((path) => path.includes('/body'))).toEqual([
            `${session.baseAddress}/api/client/messages/${messageId}/body?fullHtml=true`,
        ]);
    });

    it('names the message it is showing, and who sent it and when', async () => {
        const { transport } = deploymentServing();

        await drawing(transport);

        expect(await screen.findByText('Quarterly invoice')).toBeDefined();
        expect(screen.getByText(/^Billing · /)).toBeDefined();
    });

    it('takes focus into its own head, because opening it is a view change', async () => {
        const { transport } = deploymentServing();

        await drawing(transport);

        expect(document.activeElement).toBe(
            screen.getByRole('region', { name: "The sender's own version of this message" }),
        );
    });

    it('says who holds each guarantee, rather than crediting the frame with both', async () => {
        const { transport } = deploymentServing();

        await drawing(transport);
        await screen.findByTitle("The sender's own markup, drawn in isolation");

        expect(screen.getByText(/the frame it is drawn in permits no script at all/)).toBeDefined();
        expect(screen.getByText(/removed before this message was sent to the client/)).toBeDefined();
    });

    it('says so in words where the deployment served no markup for this message', async () => {
        const { transport } = deploymentServing(null);

        await drawing(transport);

        expect(await screen.findByText(/no formatted version of this message/)).toBeDefined();
        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });

    it('says so in words where the markup arrived empty, rather than drawing a frame with nothing in it', async () => {
        const { transport } = deploymentServing({ text: '', originalCharacterCount: 0, truncation: 'None' });

        await drawing(transport);

        expect(await screen.findByText(/no formatted version of this message/)).toBeDefined();
        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });

    it('names the bound that cut the markup to nothing, rather than reporting it as never written', async () => {
        const { transport } = deploymentServing({
            text: '',
            originalCharacterCount: 90_000,
            truncation: 'ReadCharacterBudget',
        });

        await drawing(transport);

        expect(await screen.findByText(/longer than one read returns/)).toBeDefined();
        expect(screen.queryByText(/no formatted version of this message/)).toBeNull();
    });

    it('reports the picture bound as its own loss, which the character bounds cannot name', async () => {
        const { transport } = deploymentServing({ ...asSent, truncation: 'InlineImageOctetLimit' });

        await drawing(transport);

        expect(await screen.findByText(/more pictures of its own than one view holds/)).toBeDefined();
    });

    it('re-reads the one message with the ask when the reader wants the sender pictures', async () => {
        const { transport, asked } = deploymentServing();

        await drawing(transport);
        await screen.findByTitle("The sender's own markup, drawn in isolation");
        press('Load pictures from the sender');

        await screen.findByText(/so their servers can tell it was opened/);

        expect(asked.map((request) => request.path).filter((path) => path.includes('/body'))).toEqual([
            `${session.baseAddress}/api/client/messages/${messageId}/body?fullHtml=true`,
            `${session.baseAddress}/api/client/messages/${messageId}/body?remoteImages=true&fullHtml=true`,
        ]);
    });

    it('leaves the head alone when the pictures are asked for, rather than re-reading and blanking it', async () => {
        const { transport, asked } = deploymentServing();

        await drawing(transport);
        await screen.findByTitle("The sender's own markup, drawn in isolation");
        press('Load pictures from the sender');

        await screen.findByText(/so their servers can tell it was opened/);

        expect(asked.map((request) => request.path).filter((path) => !path.includes('/body'))).toEqual([
            `${session.baseAddress}/api/client/messages/${messageId}`,
        ]);
        expect(screen.getByText('Quarterly invoice')).toBeDefined();
    });

    it('leaves the surface through the control that closes it', async () => {
        const closed = vi.fn();
        const { transport } = deploymentServing();

        await drawing(transport, { onClose: closed });
        press('Close this view');

        expect(closed).toHaveBeenCalledOnce();
    });

    it('says what failed rather than showing an empty frame', async () => {
        const refusing: MailFathomTransport = () => Promise.reject(new Error('the deployment is not there'));

        await drawing(refusing);

        expect(await screen.findByText(/could not be read: unavailable/)).toBeDefined();
    });

    it('says the message itself could not be read rather than drawing a frame under a head that never arrives', async () => {
        // Only the read behind the head refuses. Without one failure state for the two reads this is the quiet case:
        // the frame draws the markup and the head goes on saying it is reading, so the surface looks like it worked.
        const halfServing: MailFathomTransport = (request) =>
            request.path.includes('/body')
                ? Promise.resolve({ status: 200, body: body(asSent), headers: {} })
                : Promise.reject(new Error('the deployment is not there'));

        await drawing(halfServing);

        expect(await screen.findByText(/could not be read: unavailable/)).toBeDefined();
        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('reads both the head and the markup again when the reader tries again', async () => {
        let refusals = 2;

        const recovering: MailFathomTransport = (request) => {
            if (refusals > 0) {
                refusals -= 1;

                return Promise.reject(new Error('the deployment is not there'));
            }

            const answer: ClientResponse = request.path.includes('/body')
                ? { status: 200, body: body(asSent), headers: {} }
                : { status: 200, body: description, headers: {} };

            return Promise.resolve(answer);
        };

        await drawing(recovering);
        await screen.findByText(/could not be read: unavailable/);
        press('Try again');

        expect(await screen.findByTitle("The sender's own markup, drawn in isolation")).toBeDefined();
        expect(screen.getByText('Quarterly invoice')).toBeDefined();
    });

    it('keeps a markup it has already drawn when the network goes, because nothing about it stopped being true', async () => {
        const { transport } = deploymentServing();

        const network = await drawing(transport);
        await screen.findByTitle("The sender's own markup, drawn in isolation");
        network(false);

        expect(screen.getByTitle("The sender's own markup, drawn in isolation")).toBeDefined();
        expect(screen.queryByText(/This machine is offline/)).toBeNull();
    });

    it('stops saying it is reading the message once that read has definitively failed', async () => {
        const halfServing: MailFathomTransport = (request) =>
            request.path.includes('/body')
                ? Promise.resolve({ status: 200, body: body(asSent), headers: {} })
                : Promise.reject(new Error('the deployment is not there'));

        await drawing(halfServing);

        expect(await screen.findByText(/could not be read: unavailable/)).toBeDefined();
        expect(screen.queryByText(/Reading the sender/)).toBeNull();
    });

    it('says the machine has no network rather than reporting the deployment as unavailable', async () => {
        const { transport, asked } = deploymentServing();

        await drawing(transport, { online: false });

        expect(await screen.findByText(/This machine is offline/)).toBeDefined();
        expect(screen.queryByText(/could not be read/)).toBeNull();
        expect(asked).toEqual([]);
    });
});
