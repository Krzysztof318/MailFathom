// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    discardMailDraft,
    mailDraftAttachmentUploadRequest,
    mostAttachmentsInDraft,
    mostRecipientsInDraft,
    reviseMailDraft,
    sendMailDraft,
    stageMailDraftAttachment,
    unstageMailDraftAttachment,
    writeMailDraft,
    type MailDraftComposition,
    type MailSendRefusal,
} from './mailDrafts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const draftId = '6a7d2f10-4c9b-4a1d-8f31-0c5e9a2b7d44';
const attachmentId = 'b1f0c3d2-88ae-4f52-9c17-2d6b4e0a1f93';

const composition: MailDraftComposition = {
    account: 'work',
    subject: 'Renewal terms',
    plainTextBody: 'The figure in the third column is the one to check.',
    to: ['anna@example.invalid'],
    cc: [],
    bcc: [],
};

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

function draftBody(fields: Readonly<Record<string, unknown>> = {}): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            draftId,
            account: 'work',
            subject: 'Renewal terms',
            recipients: [{ role: 'To', address: 'anna@example.invalid', displayName: 'Anna', provenance: 'Author' }],
            attachments: [],
            serverCopy: 'Filed',
            revision: 1,
            sizeOctets: 812,
            composedAt: '2026-09-03T08:00:00+00:00',
            revisedAt: '2026-09-03T08:00:00+00:00',
            ...fields,
        }),
    };
}

function refusing(status: number, errorCode: number): Answer {
    return { status, body: JSON.stringify({ title: 'Refused', errorCode }) };
}

describe('writeMailDraft', () => {
    it('states a message of its own with the account and the subject the author named', async () => {
        const { transport, requests } = recording(draftBody());

        await writeMailDraft(session, transport, composition);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/drafts');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            plainTextBody: 'The figure in the third column is the one to check.',
            to: ['anna@example.invalid'],
            cc: [],
            bcc: [],
            account: 'work',
            subject: 'Renewal terms',
        });
    });

    it('states an answer by the message it answers, naming neither an account nor a subject', async () => {
        const { transport, requests } = recording(draftBody());

        await writeMailDraft(session, transport, {
            answeredEmailId: 'c0ffee00-0000-4000-8000-000000000001',
            answers: 'everyone',
            plainTextBody: 'Agreed.',
            to: ['anna@example.invalid'],
            cc: ['piotr@example.invalid'],
            bcc: [],
        });

        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            plainTextBody: 'Agreed.',
            to: ['anna@example.invalid'],
            cc: ['piotr@example.invalid'],
            bcc: [],
            answeredEmailId: 'c0ffee00-0000-4000-8000-000000000001',
            answers: 'everyone',
        });
    });

    it('answers the draft the deployment now holds', async () => {
        const answer = await writeMailDraft(session, answering(draftBody()), composition);

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                draftId,
                account: 'work',
                subject: 'Renewal terms',
                recipients: [{ role: 'To', address: 'anna@example.invalid', displayName: 'Anna' }],
                attachments: [],
                revision: 1,
                sizeOctets: 812,
            },
        });
    });

    it('reads a recipient the sender wrote no name beside as one with no name on it', async () => {
        const answer = await writeMailDraft(
            session,
            answering(draftBody({ recipients: [{ role: 'Bcc', address: 'anna@example.invalid', displayName: null }] })),
            composition,
        );

        expect(answer.outcome === 'read' ? answer.value.recipients[0] : null).toStrictEqual({
            role: 'Bcc',
            address: 'anna@example.invalid',
            displayName: null,
        });
    });

    it('answers the files staged against the draft, described and carrying none of what they hold', async () => {
        const answer = await writeMailDraft(
            session,
            answering(
                draftBody({
                    attachments: [
                        {
                            attachmentId,
                            fileName: 'Renewal.pdf',
                            mediaType: 'application/pdf',
                            sizeOctets: 248_000,
                            stagedAt: '2026-09-03T08:01:00+00:00',
                        },
                    ],
                }),
            ),
            composition,
        );

        expect(answer.outcome === 'read' ? answer.value.attachments : null).toStrictEqual([
            { attachmentId, fileName: 'Renewal.pdf', mediaType: 'application/pdf', sizeOctets: 248_000 },
        ]);
    });

    it.each([
        { shape: 'a body that is not JSON', body: 'not json' },
        {
            shape: 'a staged file with no size on it',
            body: JSON.stringify({
                draftId,
                account: 'work',
                subject: '',
                revision: 1,
                sizeOctets: 1,
                recipients: [],
                attachments: [{ attachmentId, fileName: 'a.pdf', mediaType: 'application/pdf' }],
            }),
        },
        { shape: 'a record with no draft identifier', body: JSON.stringify({ account: 'work' }) },
        {
            shape: 'a recipient written in a header this surface does not publish',
            body: JSON.stringify({
                draftId,
                account: 'work',
                subject: '',
                revision: 1,
                sizeOctets: 1,
                attachments: [],
                recipients: [{ role: 'Sender', address: 'anna@example.invalid', displayName: null }],
            }),
        },
        {
            shape: 'more recipients than a draft is read with',
            body: JSON.stringify({
                draftId,
                account: 'work',
                subject: '',
                revision: 1,
                sizeOctets: 1,
                attachments: [],
                recipients: Array.from({ length: mostRecipientsInDraft + 1 }, () => ({
                    role: 'To',
                    address: 'anna@example.invalid',
                    displayName: null,
                })),
            }),
        },
        {
            shape: 'more staged files than a draft is read with',
            body: JSON.stringify({
                draftId,
                account: 'work',
                subject: '',
                revision: 1,
                sizeOctets: 1,
                recipients: [],
                attachments: Array.from({ length: mostAttachmentsInDraft + 1 }, () => ({
                    attachmentId,
                    fileName: 'a.pdf',
                    mediaType: 'application/pdf',
                    sizeOctets: 1,
                })),
            }),
        },
    ])('refuses $shape as unreadable', async ({ body }) => {
        const answer = await writeMailDraft(session, answering({ status: 200, body }), composition);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a credential the deployment refused as one to sign in with again', async () => {
        const answer = await writeMailDraft(session, answering({ status: 401, body: '' }), composition);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('reads a deployment that never answered as one to try again', async () => {
        const answer = await writeMailDraft(session, () => Promise.reject(new Error('no route to host')), composition);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('reviseMailDraft', () => {
    it('replaces the draft it names', async () => {
        const { transport, requests } = recording(draftBody({ revision: 2 }));

        const answer = await reviseMailDraft(session, transport, draftId, composition);

        expect(requests[0]?.method).toBe('PUT');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/drafts/${draftId}`);
        expect(answer.outcome === 'read' ? answer.value.revision : null).toBe(2);
    });
});

describe('discardMailDraft', () => {
    it('gives the draft up at its own route', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ draftId, outcome: 'Removed' }),
        });

        const answer = await discardMailDraft(session, transport, draftId);

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/drafts/${draftId}`);
        expect(answer.outcome).toBe('read');
    });

    it('reads a draft this owner no longer holds as a deployment that would not do it', async () => {
        const answer = await discardMailDraft(session, answering({ status: 404, body: '' }), draftId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 404 } });
    });
});

describe('unstageMailDraftAttachment', () => {
    it('takes one staged file off, reading the answer that carries no content as done', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        const answer = await unstageMailDraftAttachment(session, transport, draftId, attachmentId);

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/drafts/${draftId}/attachments/${attachmentId}`,
        );
        expect(answer.outcome).toBe('read');
    });
});

describe('mailDraftAttachmentUploadRequest', () => {
    it('carries the file’s name as a query value and what it declares itself to be as the request’s own type', () => {
        const request = mailDraftAttachmentUploadRequest(session, draftId, 'Q3 report (final).pdf', 'application/pdf');

        expect(request.method).toBe('POST');
        expect(request.path).toBe(
            `https://mail.example.invalid/api/client/drafts/${draftId}` +
                '/attachments?fileName=Q3%20report%20(final).pdf',
        );
        expect(request.headers['Content-Type']).toBe('application/pdf');
        expect(request.headers['Authorization']).toBe('Basic dGVzdA==');
    });
});

describe('stageMailDraftAttachment', () => {
    it('hands the composed request to the adapter and answers the file the deployment took in', async () => {
        const composed: ClientRequest[] = [];

        const answer = await stageMailDraftAttachment(session, draftId, 'notes.txt', 'text/plain', (request) => {
            composed.push(request);

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({
                    attachmentId,
                    fileName: 'notes.txt',
                    mediaType: 'text/plain',
                    sizeOctets: 44,
                    stagedAt: '2026-09-03T08:02:00+00:00',
                }),
            });
        });

        expect(composed[0]?.headers['Content-Type']).toBe('text/plain');
        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { attachmentId, fileName: 'notes.txt', mediaType: 'text/plain', sizeOctets: 44 },
        });
    });

    it('reads a file the deployment refused as larger than it takes as a deployment that would not do it', async () => {
        const answer = await stageMailDraftAttachment(session, draftId, 'big.iso', 'application/octet-stream', () =>
            Promise.resolve({ status: 413, headers: {}, body: '' }),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 413 } });
    });

    it('reads an upload nothing answered as one to try again', async () => {
        const answer = await stageMailDraftAttachment(session, draftId, 'notes.txt', 'text/plain', () =>
            Promise.resolve(null),
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('sendMailDraft', () => {
    it('queues the message the draft holds and answers the send it became', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ draftId, outgoingEmail: 'e9d1a2b3-0000-4000-8000-00000000abcd', stage: 'Queued' }),
        });

        const answer = await sendMailDraft(session, transport, draftId);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/drafts/${draftId}/send`);
        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { queued: true, outgoingEmailId: 'e9d1a2b3-0000-4000-8000-00000000abcd' },
        });
    });

    it.each<{ status: number; errorCode: number; refusal: MailSendRefusal }>([
        { status: 409, errorCode: 56_003, refusal: 'sendingNotEnabled' },
        { status: 409, errorCode: 53_006, refusal: 'recipientRefused' },
        { status: 409, errorCode: 53_009, refusal: 'recipientRefused' },
        { status: 409, errorCode: 57_002, refusal: 'ceilingReached' },
        { status: 409, errorCode: 59_001, refusal: 'contentRefused' },
        { status: 409, errorCode: 59_002, refusal: 'notFullyScanned' },
        { status: 503, errorCode: 81_001, refusal: 'screeningUnavailable' },
    ])('reads the deployment refusing with $errorCode as $refusal', async ({ status, errorCode, refusal }) => {
        const answer = await sendMailDraft(session, answering(refusing(status, errorCode)), draftId);

        expect(answer).toStrictEqual({ outcome: 'read', value: { queued: false, refusal } });
    });

    it('reads a refusal carrying a code this client does not know as one refused for another reason', async () => {
        const answer = await sendMailDraft(session, answering(refusing(409, 12_345)), draftId);

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { queued: false, refusal: 'refusedForAnotherReason' },
        });
    });

    it('reads a status that is neither a queueing nor a refusal as a failure of the request', async () => {
        const answer = await sendMailDraft(session, answering({ status: 403, body: '' }), draftId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('refuses a queueing that named no send as unreadable', async () => {
        const answer = await sendMailDraft(
            session,
            answering({ status: 200, body: JSON.stringify({ draftId }) }),
            draftId,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
