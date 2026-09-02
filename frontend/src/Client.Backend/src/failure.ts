// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/**
 * Why a read did not answer.
 *
 * The four are separated because a screen does something different with each: a refused credential is signed in again,
 * a missing grant is not, an unreachable deployment is retried, and a body this package could not read is a defect.
 */
export type ClientFailureReason = 'unauthenticated' | 'unauthorized' | 'unavailable' | 'unreadable';

/** A read that did not answer, and enough to say so on a screen without showing what the service returned. */
export interface ClientFailure {
    readonly reason: ClientFailureReason;
    readonly status: number | null;
}

/** What every operation on the client answers with: expected failure is a value here rather than an exception. */
export type ClientResult<TValue> =
    | { readonly outcome: 'read'; readonly value: TValue }
    | { readonly outcome: 'failed'; readonly failure: ClientFailure };

export function read<TValue>(value: TValue): ClientResult<TValue> {
    return { outcome: 'read', value };
}

export function failed<TValue>(reason: ClientFailureReason, status: number | null): ClientResult<TValue> {
    return { outcome: 'failed', failure: { reason, status } };
}

/** The failure an HTTP status stands for, for a status this package did not expect to succeed. */
export function failureReasonForStatus(status: number): ClientFailureReason {
    switch (status) {
        case 401:
            return 'unauthenticated';
        case 403:
            return 'unauthorized';
        default:
            return 'unavailable';
    }
}
