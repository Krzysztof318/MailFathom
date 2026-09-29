// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientFailureReason, ClientResult, MailReplyDrafting } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { ToastKind } from '../toasts/useToasts';

// What asking the deployment for a draft came to, read once for the two places that ask: the composer's own drafting
// block, and the field under a correspondence. Both say a drafting that wrote nothing in the same words, which is why the
// reading is here rather than in either of them.

/**
 * What each of the five failures means for somebody trying to write mail, said as what they do next.
 *
 * The composer's reading failure and the deployment's own are worded from one table because four of the five are the
 * same answer to either. `missing` reaches only the read: the message route is the one that reads a `404` as something
 * the deployment no longer holds, so the sentence is about the message being answered rather than about a draft.
 */
export const failureSaid: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'compose.failedUnauthenticated',
    unauthorized: 'compose.failedUnauthorized',
    unavailable: 'compose.failedUnavailable',
    unreadable: 'compose.failedUnreadable',
    missing: 'compose.failedMissing',
};

/** A draft the deployment wrote, or what to tell somebody about one it did not. */
export type DraftingRead =
    | { readonly drafted: true; readonly body: string }
    | { readonly drafted: false; readonly kind: ToastKind; readonly said: MessageKey };

/**
 * Reads what a drafting came to.
 *
 * A request that failed is an error; a deployment that answered and wrote nothing is a warning, because nothing went
 * wrong — it declined, or the allowance for drafting is spent, and each of those is its own sentence.
 */
export function draftingRead(answer: ClientResult<MailReplyDrafting>): DraftingRead {
    if (answer.outcome === 'failed') {
        return { drafted: false, kind: 'error', said: failureSaid[answer.failure.reason] };
    }

    if (answer.value.outcome !== 'drafted') {
        return {
            drafted: false,
            kind: 'warning',
            said: answer.value.outcome === 'allowanceSpent' ? 'compose.draftAllowanceSpent' : 'compose.notDrafted',
        };
    }

    return { drafted: true, body: answer.value.body };
}
