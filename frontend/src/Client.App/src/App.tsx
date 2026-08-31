// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    readMailAccounts,
    type ClientFailureReason,
    type ClientResult,
    type MailAccount,
    type MailAccountDirectory,
    type MailSynchronizationState,
} from '@mailfathom/client-backend';
import type { MessageKey } from './localization/en';
import { isOfferedLocale, localeNames, locales, type Locale } from './localization/locale';
import { useLocalization } from './localization/useLocalization';
import { stubSession, stubTransport } from './stubMailFathom';

// The one screen this workspace exists to prove. It draws nothing the client will keep: what it demonstrates is that
// the application package reads through `Client.Backend`, that the boundary holds, that the build produces a bundle,
// that the Tailwind theme tokens reach the markup, and that every word on it comes out of a catalogue.

// The two closed sets `Client.Backend` answers with, mapped to what a person reads. Each is a lookup declared once and
// exhaustive by its own type, so a value added to either set fails to compile here until this screen says what it is
// called — which is the whole reason the mapping is a table rather than a key assembled from the value at the call site.
const synchronizationLabels: Readonly<Record<MailSynchronizationState, MessageKey>> = {
    NeverSynchronized: 'synchronization.neverSynchronized',
    Synchronized: 'synchronization.synchronized',
    Failing: 'synchronization.failing',
    Unreachable: 'synchronization.unreachable',
};

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

export function App() {
    const { translate } = useLocalization();
    const [result, setResult] = useState<ClientResult<MailAccountDirectory> | null>(null);

    useEffect(() => {
        let listening = true;

        void readMailAccounts(stubSession, stubTransport).then((answer) => {
            if (listening) {
                setResult(answer);
            }
        });

        return () => {
            listening = false;
        };
    }, []);

    return (
        <main className="min-h-screen bg-fathom-950 px-6 py-10 font-sans text-fathom-200">
            <div className="mx-auto flex max-w-2xl flex-col gap-6">
                <header className="flex items-baseline justify-between border-b border-fathom-800 pb-4">
                    <h1 className="text-2xl font-semibold text-white">{translate('shell.title')}</h1>
                    <div className="flex items-baseline gap-4">
                        <LanguageChoice />
                        <p className="font-mono text-sm text-fathom-500">{__MAILFATHOM_VERSION__}</p>
                    </div>
                </header>

                {result === null ? <p>{translate('accounts.reading')}</p> : <Accounts result={result} />}
            </div>
        </main>
    );
}

function LanguageChoice() {
    const { locale, setLocale, translate } = useLocalization();

    return (
        <select
            aria-label={translate('shell.language')}
            className="rounded-md bg-fathom-800/40 px-2 py-1 text-sm text-fathom-200"
            value={locale}
            onChange={(event) => {
                if (isOfferedLocale(event.target.value)) {
                    setLocale(event.target.value);
                }
            }}
        >
            {locales.map((offered) => (
                <option key={offered} value={offered}>
                    {localeNames[offered]}
                </option>
            ))}
        </select>
    );
}

function Accounts({ result }: { readonly result: ClientResult<MailAccountDirectory> }) {
    const { translate } = useLocalization();

    if (result.outcome === 'failed') {
        return (
            <p className="text-fathom-star">
                {translate('accounts.failed', { reason: translate(failureLabels[result.failure.reason]) })}
            </p>
        );
    }

    return (
        <section className="flex flex-col gap-3">
            <p className="text-sm">
                {translate(result.value.synchronizationEnabled ? 'accounts.refreshing' : 'accounts.notRefreshing')}
            </p>

            <ul className="flex flex-col gap-2">
                {result.value.accounts.map((account) => (
                    <AccountRow key={account.id} account={account} />
                ))}
            </ul>
        </section>
    );
}

function AccountRow({ account }: { readonly account: MailAccount }) {
    const { locale, translate } = useLocalization();
    const state = translate(synchronizationLabels[account.synchronizationState]);

    return (
        <li className="flex items-center justify-between rounded-md bg-fathom-800/40 px-4 py-3">
            <span className="font-medium text-white">{account.displayName}</span>
            <span className="text-sm">
                {account.behind ? translate('account.stateBehind', { state }) : state}
                {account.lastSynchronizedAt === null ? null : (
                    <span className="ml-2 text-fathom-500">
                        {translate('account.lastSynchronized', {
                            when: formatInstant(locale, account.lastSynchronizedAt),
                        })}
                    </span>
                )}
            </span>
        </li>
    );
}

// The one place this screen turns a value into words the platform already knows how to write. `Intl` reads the active
// locale, so a Polish reader gets a Polish month and a Polish ordering without a second copy of any of that living in
// the catalogues — which is the rule for numbers and relative times too, whichever screen first shows one.
function formatInstant(locale: Locale, instant: string): string {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(instant));
}
