// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession } from './session';
import type { MailFathomTransport } from './transport';

/** The route the owner's accounts are served at, relative to the client prefix. */
export const mailAccountsRoute = '/accounts';

/** Whether the deployment's last finished attempt at an account succeeded, failed, found no mail server, or never ran. */
export type MailSynchronizationState = 'NeverSynchronized' | 'Synchronized' | 'Failing' | 'Unreachable';

/** One of the signed-in owner's accounts, and how current the local copy of it is. */
export interface MailAccount {
    readonly id: string;
    readonly displayName: string;
    readonly synchronizationState: MailSynchronizationState;
    readonly lastSynchronizedAt: string | null;
    readonly behind: boolean;
}

/** The owner's accounts, beside the deployment-wide switch that says whether any of them is being refreshed at all. */
export interface MailAccountDirectory {
    readonly synchronizationEnabled: boolean;
    readonly accounts: readonly MailAccount[];
}

// The most accounts a directory answer may carry before it is refused unread. One owner holds a handful in practice,
// so the ceiling is far above anything real and exists for the case the answer is not: the array is walked and
// validated element by element, and a bound applied after that walk is not a bound.
const maximumAccountsInDirectory = 256;

const synchronizationStates: readonly MailSynchronizationState[] = [
    'NeverSynchronized',
    'Synchronized',
    'Failing',
    'Unreachable',
];

/** Reads the signed-in owner's accounts, answering an expected failure as a value rather than by throwing. */
export async function readMailAccounts(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<MailAccountDirectory>> {
    const response = await transport({
        method: 'GET',
        path: routeFor(session, mailAccountsRoute),
        headers: headersFor(session),
    });

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const directory = parseDirectory(response.body);

    return directory === null ? failed('unreadable', response.status) : read(directory);
}

function parseDirectory(body: string): MailAccountDirectory | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const synchronizationEnabled = record['synchronizationEnabled'];
    const entries = record['accounts'];
    if (typeof synchronizationEnabled !== 'boolean' || !Array.isArray(entries)) {
        return null;
    }

    if (entries.length > maximumAccountsInDirectory) {
        return null;
    }

    const accounts: MailAccount[] = [];
    for (const entry of entries) {
        const account = parseAccount(entry);
        if (account === null) {
            return null;
        }

        accounts.push(account);
    }

    return { synchronizationEnabled, accounts };
}

function parseAccount(value: unknown): MailAccount | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const displayName = record['displayName'];
    const synchronizationState = record['synchronizationState'];
    const lastSynchronizedAt = record['lastSynchronizedAt'] ?? null;
    const behind = record['behind'];

    if (typeof id !== 'string' || typeof displayName !== 'string' || typeof behind !== 'boolean') {
        return null;
    }

    if (!isSynchronizationState(synchronizationState)) {
        return null;
    }

    if (lastSynchronizedAt !== null && typeof lastSynchronizedAt !== 'string') {
        return null;
    }

    return {
        id,
        displayName,
        synchronizationState,
        lastSynchronizedAt,
        behind,
    };
}

function asRecord(value: unknown): Readonly<Record<string, unknown>> | null {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : null;
}

function isSynchronizationState(value: unknown): value is MailSynchronizationState {
    return typeof value === 'string' && synchronizationStates.includes(value as MailSynchronizationState);
}
