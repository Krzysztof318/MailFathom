// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type {
    ClientFailureReason,
    ClientResult,
    MailAccountDirectory,
    MailSynchronizationState,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The line above every space saying what the client is looking at and how current it is. It summarizes rather than
// enumerates: what each account was last synchronized at, and what a grant allows, is read by the space that shows it.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

type Tone = 'healthy' | 'attention' | 'quiet';

const toneColours: Readonly<Record<Tone, string>> = {
    healthy: 'bg-healthy',
    attention: 'bg-warning',
    quiet: 'bg-faint',
};

// The two states in which an account is not going to catch up on its own. They are read before the lag below, because
// an account nothing is fixing says something a lagging one does not, and "behind" would let it wait unnoticed.
const brokenStates: readonly MailSynchronizationState[] = ['Failing', 'Unreachable'];

interface Freshness {
    readonly message: MessageKey;
    readonly tone: Tone;
}

function freshnessOf(directory: MailAccountDirectory): Freshness {
    if (directory.accounts.length === 0) {
        return { message: 'connection.noAccounts', tone: 'quiet' };
    }

    if (!directory.synchronizationEnabled) {
        return { message: 'accounts.notRefreshing', tone: 'quiet' };
    }

    if (directory.accounts.some((account) => brokenStates.includes(account.synchronizationState))) {
        return { message: 'connection.failing', tone: 'attention' };
    }

    const current = directory.accounts.every(
        (account) => !account.behind && account.synchronizationState === 'Synchronized',
    );

    return current
        ? { message: 'connection.current', tone: 'healthy' }
        : { message: 'connection.behind', tone: 'attention' };
}

export function ConnectionSummary({
    accounts,
    reread,
}: {
    readonly accounts: ClientResult<MailAccountDirectory> | null;
    readonly reread: () => void;
}) {
    const { translate } = useLocalization();

    if (accounts === null) {
        return <Line tone="quiet">{translate('accounts.reading')}</Line>;
    }

    if (accounts.outcome === 'failed') {
        return (
            <Line tone="attention">
                {translate('accounts.failed', { reason: translate(failureLabels[accounts.failure.reason]) })}
                <button
                    type="button"
                    onClick={reread}
                    className="rounded-md border border-line px-2 py-0.5 text-sm text-text-soft transition hover:bg-hover"
                >
                    {translate('connection.retry')}
                </button>
            </Line>
        );
    }

    const freshness = freshnessOf(accounts.value);

    return <Line tone={freshness.tone}>{translate(freshness.message)}</Line>;
}

function Line({ tone, children }: { readonly tone: Tone; readonly children: ReactNode }) {
    return (
        <p className="flex items-center gap-2 text-sm text-muted">
            <span aria-hidden="true" className={`size-2 shrink-0 rounded-full ${toneColours[tone]}`} />
            {children}
        </p>
    );
}
