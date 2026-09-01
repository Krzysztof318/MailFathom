// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mailMessageRoute, readMailMessage, type MailMessage } from './mailMessage';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

/** The whole of what the message route answers with, which every test below narrows or breaks one field of. */
function described(overrides: Readonly<Record<string, unknown>> = {}): string {
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
            participants: [
                { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
                { role: 'To', address: 'reader@example.invalid', displayName: null },
            ],
            messageId: 'abc@example.invalid',
            inReplyTo: null,
            references: [],
        },
        body: { availability: 'Readable', plainText: true, html: true },
        sender: {
            authorAuthentication: 'Authenticated',
            deploymentTrust: 'Trusted',
            authenticatedDomain: 'mail.example.invalid',
        },
        attachments: [
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
            },
        ],
        carried: {
            attachmentCount: 1,
            totalSizeOctets: 2_048,
            inlineResourceCount: 3,
            encrypted: false,
            unverifiedSignature: true,
            unexpandedTnefPart: false,
        },
        unread: true,
        flagged: false,
        answered: false,
        ...overrides,
    });
}

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

async function readingOf(body: string): Promise<MailMessage | null> {
    const result = await readMailMessage(session, answering({ status: 200, body }), messageId);

    return result.outcome === 'read' ? result.value : null;
}

describe('mailMessageRoute', () => {
    it('encodes the identifier it is given rather than writing it into the path as it stands', () => {
        expect(mailMessageRoute('a/../b')).toBe('/messages/a%2F..%2Fb');
    });
});

describe('readMailMessage', () => {
    it('asks for the message route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: described() });

        await readMailMessage(session, transport, messageId);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: `https://mail.example.invalid/api/client/messages/${messageId}`,
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
                longestAnswer: 4 * 1024 * 1024,
            },
        ]);
    });

    it('reads the message a well-formed answer describes', async () => {
        const message = await readingOf(described());

        expect(message?.headers.subject).toBe('Quarterly invoice');
        expect(message?.headers.participants).toEqual([
            { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
            { role: 'To', address: 'reader@example.invalid', displayName: null },
        ]);
        expect(message?.attachments).toEqual([
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
            },
        ]);
    });

    it('reads the domain that authenticated beside the two outcomes it is published with', async () => {
        const message = await readingOf(described());

        expect(message?.sender).toEqual({
            authorAuthentication: 'Authenticated',
            deploymentTrust: 'Trusted',
            authenticatedDomain: 'mail.example.invalid',
        });
    });

    it('reads a message nothing authenticated as one naming no domain', async () => {
        const message = await readingOf(
            described({
                sender: {
                    authorAuthentication: 'NotEstablished',
                    deploymentTrust: 'Unknown',
                    authenticatedDomain: null,
                },
            }),
        );

        expect(message?.sender.authenticatedDomain).toBeNull();
    });

    it('reads a message whose parts nothing has ever read as one carrying no counts', async () => {
        const message = await readingOf(described({ carried: null }));

        expect(message?.carried).toBeNull();
    });

    it.each([401, 403, 404, 500, 503])(
        'reports a status of %i as a failure rather than reading the answer',
        async (status) => {
            const result = await readMailMessage(session, answering({ status, body: '' }), messageId);

            expect(result.outcome).toBe('failed');
        },
    );

    it('reports a deployment that answered nothing at all as unavailable', async () => {
        const result = await readMailMessage(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            messageId,
        );

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        ['a body that is not JSON at all', 'not json'],
        ['a body that is an array rather than a message', '[]'],
        ['a message with no headers block', described({ headers: undefined })],
        ['a subject that is not text', described({ headers: { subject: 7 } })],
        ['a participant in a role this build does not publish', headersNaming({ role: 'Approver' })],
        ['a participant with no address', headersNaming({ role: 'To', displayName: 'Nobody' })],
        [
            'an authentication outcome this build does not publish',
            described({
                sender: { authorAuthentication: 'Probably', deploymentTrust: 'Unknown', authenticatedDomain: null },
            }),
        ],
        [
            'a trust level this build does not publish',
            described({
                sender: { authorAuthentication: 'Failed', deploymentTrust: 'Suspect', authenticatedDomain: null },
            }),
        ],
        ['an attachment at a negative position', attachmentOf({ position: -1 })],
        ['an attachment with a fractional size', attachmentOf({ sizeOctets: 1.5 })],
        ['an attachment declaring no media type', attachmentOf({ mediaType: null })],
        ['a carried block with a count that is not a number', described({ carried: { attachmentCount: 'two' } })],
        ['more attachments than a message may carry', tooManyAttachments()],
    ])('refuses %s rather than reading part of it', async (_shape, body) => {
        const result = await readMailMessage(session, answering({ status: 200, body }), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

/** A message whose only participant is the one named, which is how a malformed address is put in front of the parser. */
function headersNaming(participant: Readonly<Record<string, unknown>>): string {
    return described({
        headers: {
            subject: 'Quarterly invoice',
            sentAt: null,
            receivedAt: null,
            participants: [participant],
            messageId: null,
            inReplyTo: null,
            references: [],
        },
    });
}

/** A message whose only attachment is the one named, with the rest of a well-formed description around it. */
function attachmentOf(attachment: Readonly<Record<string, unknown>>): string {
    return described({
        attachments: [
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
                ...attachment,
            },
        ],
    });
}

// The bound is checked while the list is walked rather than after it, so the case worth proving is an answer larger than
// anything the service composes rather than one merely longer than a reader would open.
function tooManyAttachments(): string {
    return described({
        attachments: Array.from({ length: 1_025 }, (_unused, position) => ({
            position,
            fileName: 'invoice.pdf',
            wasFileNameNormalized: false,
            mediaType: 'application/pdf',
            sizeOctets: 2_048,
        })),
    });
}
