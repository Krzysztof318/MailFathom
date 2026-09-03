// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { withdrawOutgoingMail, type MailSendWithdrawal } from './mailOutbox';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const outgoingEmailId = 'e9d1a2b3-0000-4000-8000-00000000abcd';

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function deciding(outcome: string): Answer {
    return { status: 200, body: JSON.stringify({ outgoingEmail: outgoingEmailId, outcome }) };
}

describe('withdrawOutgoingMail', () => {
    it('names the one send it withdraws on the client surface’s cancellation route', async () => {
        const requests: ClientRequest[] = [];

        await withdrawOutgoingMail(
            session,
            (request) => {
                requests.push(request);

                return Promise.resolve({ ...deciding('Accepted'), headers: {} });
            },
            outgoingEmailId,
        );

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/outbox/cancellation');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({ outgoingEmail: outgoingEmailId });
    });

    it.each<{ answered: string; withdrawal: MailSendWithdrawal }>([
        { answered: 'Accepted', withdrawal: 'withdrawn' },
        { answered: 'AttemptUnderWay', withdrawal: 'alreadyBeingSent' },
        { answered: 'StageDoesNotAllowIt', withdrawal: 'pastRecall' },
        { answered: 'RecordUnknown', withdrawal: 'noSuchSend' },
    ])('reads $answered as $withdrawal', async ({ answered, withdrawal }) => {
        const answer = await withdrawOutgoingMail(session, answering(deciding(answered)), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'read', value: withdrawal });
    });

    it('refuses an outcome that belongs to offering a send again as unreadable', async () => {
        const answer = await withdrawOutgoingMail(session, answering(deciding('RefusalNotRestated')), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a body that is not JSON as unreadable', async () => {
        const answer = await withdrawOutgoingMail(
            session,
            answering({ status: 200, body: 'not json' }),
            outgoingEmailId,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a credential without the sending grant as one the deployment will not do this for', async () => {
        const answer = await withdrawOutgoingMail(session, answering({ status: 403, body: '' }), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reads a deployment that never answered as one to try again', async () => {
        const answer = await withdrawOutgoingMail(
            session,
            () => Promise.reject(new Error('no route to host')),
            outgoingEmailId,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
