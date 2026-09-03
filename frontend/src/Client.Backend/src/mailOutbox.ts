// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// Taking one send back while it is still queued, which is the closest thing to unsending that is honest and is why a
// send is confirmed rather than delayed. Nothing else of the outbox is here: what a client draws today is the send it
// has just asked for, and a listing of what an owner is sending is a screen of its own to publish an operation for.
//
// The route is `mailfathom.mail.send` like every other one there, withdrawing a send being part of sending rather than
// a power beside it.

/** The route one queued send is withdrawn at, relative to the client prefix. */
export const mailOutboxCancellationRoute = '/outbox/cancellation';

/**
 * What became of the send a withdrawal named.
 *
 * Four outcomes rather than a refusal, because a person acting on a screen a moment old is exactly who asks: a message
 * whose transmission has begun cannot be taken back, and saying so is the answer rather than an error.
 */
export type MailSendWithdrawal = 'withdrawn' | 'alreadyBeingSent' | 'pastRecall' | 'noSuchSend';

// The outcomes the surface names, beside what each of them is here. `RefusalNotRestated` belongs to offering a send
// again rather than to withdrawing one, so a withdrawal that answered it is an answer this client does not act on.
const withdrawals: Readonly<Record<string, MailSendWithdrawal | undefined>> = {
    Accepted: 'withdrawn',
    AttemptUnderWay: 'alreadyBeingSent',
    StageDoesNotAllowIt: 'pastRecall',
    RecordUnknown: 'noSuchSend',
};

// Two short fields. Generous against that and far below anything worth buffering.
const longestDecisionAnswer = 16_384;

/**
 * Withdraws one send that has not begun transmitting.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param outgoingEmailId The send, as the queueing answered it.
 * @returns What became of it, or an expected failure as a value.
 */
export function withdrawOutgoingMail(
    session: ClientSession,
    transport: MailFathomTransport,
    outgoingEmailId: string,
): Promise<ClientResult<MailSendWithdrawal>> {
    return spanned(`POST ${mailOutboxCancellationRoute}`, async () =>
        withdrawalIn(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, mailOutboxCancellationRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ outgoingEmail: outgoingEmailId }),
                longestAnswer: longestDecisionAnswer,
            }),
        ),
    );
}

function withdrawalIn(response: ClientResponse | null): ClientResult<MailSendWithdrawal> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    let outcome: unknown;

    try {
        outcome = asRecord(JSON.parse(response.body))?.['outcome'];
    } catch {
        return failed('unreadable', response.status);
    }

    const withdrawal = typeof outcome === 'string' ? withdrawals[outcome] : undefined;

    return withdrawal === undefined ? failed('unreadable', response.status) : read(withdrawal);
}
