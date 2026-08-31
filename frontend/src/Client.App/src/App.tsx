// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    readMailAccounts,
    type ClientResult,
    type MailAccount,
    type MailAccountDirectory,
    type MailSynchronizationState,
} from '@mailfathom/client-backend';
import { stubSession, stubTransport } from './stubMailFathom';

// The one screen this workspace exists to prove. It draws nothing the client will keep: what it demonstrates is that
// the application package reads through `Client.Backend`, that the boundary holds, that the build produces a bundle,
// and that the Tailwind theme tokens reach the markup.

const synchronizationLabels: Readonly<Record<MailSynchronizationState, string>> = {
    NeverSynchronized: 'never synchronized',
    Synchronized: 'synchronized',
    Failing: 'failing',
    Unreachable: 'unreachable',
};

export function App() {
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
                    <h1 className="text-2xl font-semibold text-white">MailFathom</h1>
                    <p className="font-mono text-sm text-fathom-500">{__MAILFATHOM_VERSION__}</p>
                </header>

                {result === null ? <p>Reading accounts…</p> : <Accounts result={result} />}
            </div>
        </main>
    );
}

function Accounts({ result }: { readonly result: ClientResult<MailAccountDirectory> }) {
    if (result.outcome === 'failed') {
        return <p className="text-fathom-star">The accounts could not be read: {result.failure.reason}.</p>;
    }

    return (
        <section className="flex flex-col gap-3">
            <p className="text-sm">
                {result.value.synchronizationEnabled
                    ? 'This deployment refreshes the local copy of these accounts.'
                    : 'This deployment is not refreshing the local copy of these accounts.'}
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
    return (
        <li className="flex items-center justify-between rounded-md bg-fathom-800/40 px-4 py-3">
            <span className="font-medium text-white">{account.displayName}</span>
            <span className="text-sm">
                {synchronizationLabels[account.synchronizationState]}
                {account.behind ? ', behind' : ''}
            </span>
        </li>
    );
}
