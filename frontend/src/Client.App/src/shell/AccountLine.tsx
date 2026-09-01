// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccount, MailSynchronizationState } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { ageOf } from './synchronizationAge';

// One account, and how current the deployment's copy of it is. It is its own component because it is the row of a list
// and because the three facts on it answer different questions: what the last finished attempt did, whether mail was
// left behind by it, and when anything was last taken in.

const stateLabels: Readonly<Record<MailSynchronizationState, MessageKey>> = {
    Synchronized: 'account.synchronized',
    Failing: 'account.failing',
    Unreachable: 'account.unreachable',
    NeverSynchronized: 'account.neverSynchronized',
};

// An account nothing is fixing is read before one that is merely catching up, for the reason the summary above the
// list reads them in that order: "behind" would let a mailbox nobody is refreshing wait unnoticed.
function stateOf(account: MailAccount): MessageKey {
    return account.synchronizationState === 'Synchronized' && account.behind
        ? 'account.behind'
        : stateLabels[account.synchronizationState];
}

export function AccountLine({ account, readAt }: { readonly account: MailAccount; readonly readAt: Date }) {
    const { locale, translate } = useLocalization();
    const age = ageOf(account.lastSynchronizedAt, readAt, locale);

    return (
        <li className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <span className="font-medium text-text">{account.displayName}</span>
            <span className="text-muted">{translate(stateOf(account))}</span>
            <span className="text-faint">
                {age === null ? translate('account.neverRefreshed') : translate('account.lastRefreshed', { age })}
            </span>
        </li>
    );
}
