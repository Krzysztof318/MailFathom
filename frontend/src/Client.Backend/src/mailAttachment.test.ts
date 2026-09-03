// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    attachmentRefusalForStatus,
    mailAttachmentRequest,
    mailAttachmentRoute,
    readMailAttachment,
} from './mailAttachment';
import type { ClientSession } from './session';
import type { ClientRequest } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

describe('mailAttachmentRoute', () => {
    it('names the file by the position the message described it at', () => {
        expect(mailAttachmentRoute(messageId, 2)).toBe(`/messages/${messageId}/attachments/2`);
    });

    it('encodes the identifier it is given rather than writing it into the path as it stands', () => {
        expect(mailAttachmentRoute('a/../b', 0)).toBe('/messages/a%2F..%2Fb/attachments/0');
    });
});

describe('mailAttachmentRequest', () => {
    it('asks for octets rather than for JSON, and presents the credential the session holds', () => {
        expect(mailAttachmentRequest(session, messageId, 1, 2_048)).toEqual({
            method: 'GET',
            path: `https://mail.example.invalid/api/client/messages/${messageId}/attachments/1`,
            headers: { Accept: 'application/octet-stream', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 2_048,
        });
    });

    it('reads the answer under the size the message said the file holds, so a larger one is refusable', () => {
        expect(mailAttachmentRequest(session, messageId, 0, 17).longestAnswer).toBe(17);
    });
});

// What the download records is asserted in `telemetry.test.ts`, which is where a span can be read back from; what is
// asserted here is that the adapter is handed the request this module composes and that its answer travels back
// untouched, this package having no vocabulary for a download somebody stopped.
describe('readMailAttachment', () => {
    it('hands the composed download to the adapter that puts it on the wire', async () => {
        const asked: ClientRequest[] = [];

        await readMailAttachment(
            session,
            messageId,
            1,
            2_048,
            (request) => {
                asked.push(request);

                return Promise.resolve('delivered');
            },
            () => null,
        );

        expect(asked).toEqual([mailAttachmentRequest(session, messageId, 1, 2_048)]);
    });

    it('answers exactly what the adapter answered', async () => {
        const answer = await readMailAttachment(
            session,
            messageId,
            0,
            17,
            () => Promise.resolve('abandoned'),
            () => null,
        );

        expect(answer).toBe('abandoned');
    });
});

describe('attachmentRefusalForStatus', () => {
    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
        [503, 'unavailable'],
    ])('reads a status of %i as %s', (status, refusal) => {
        expect(attachmentRefusalForStatus(status)).toBe(refusal);
    });
});
