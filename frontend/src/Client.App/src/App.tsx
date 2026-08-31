// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailAccounts,
    type ClientFailureReason,
    type ClientResult,
    type DeploymentAddress,
    type MailAccount,
    type MailAccountDirectory,
    type MailSynchronizationState,
} from '@mailfathom/client-backend';
import { forgetDeployment, storeDeployment, type AdoptedDeployment } from './deployment/adoptedDeployment';
import { ConnectDeployment } from './deployment/ConnectDeployment';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import type { MessageKey } from './localization/en';
import { isOfferedLocale, localeNames, locales, type Locale } from './localization/locale';
import { useLocalization } from './localization/useLocalization';
import { stubAuthorization, stubTransport } from './stubMailFathom';

// The one screen this workspace exists to prove, in front of which stands the question every run has to answer first:
// which deployment this client belongs to. That answer arrives as a value rather than as a code path — `main.tsx`
// resolves it once at the edge, and nothing below asks which head it is running on.

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

export function App({
    deployment,
    send,
}: {
    readonly deployment: AdoptedDeployment | null;
    readonly send: DeploymentTransport;
}) {
    const { translate } = useLocalization();
    const [adopted, setAdopted] = useState(deployment);
    const [result, setResult] = useState<ClientResult<MailAccountDirectory> | null>(null);
    const baseAddress = adopted === null ? null : adopted.deployment.baseAddress;
    const mail = useRef<HTMLDivElement>(null);
    const focusedFor = useRef(adopted);

    // The view changed, so focus goes to the start of what replaced it rather than staying on a control that is no
    // longer there. Only in this direction: the connect screen places focus itself, on the field it is asking to have
    // filled, and a parent effect runs after a child's and would take it back off. A cold start is not a view change,
    // so opening against a deployment already adopted moves nothing.
    //
    // What separates the two is the deployment this effect last acted on rather than a flag saying the first render
    // has happened. React invokes an effect twice on mount under `StrictMode`, which `main.tsx` mounts the application
    // in, and a flag the first invocation cleared is already cleared when the second one reads it — so the guard would
    // pull focus onto the mail on exactly the ordinary open it exists to leave alone. Both invocations see the same
    // adopted deployment, so a comparison against it survives being run twice.
    useEffect(() => {
        if (adopted === focusedFor.current) {
            return;
        }

        focusedFor.current = adopted;

        if (adopted !== null) {
            mail.current?.focus();
        }
    }, [adopted]);

    // The session is built from the address rather than held beside it, which is what makes a credential unable to
    // outlive the deployment it was presented to: pointing the client somewhere else runs this again against the new
    // address, and there is nowhere for the previous one's session to survive.
    useEffect(() => {
        if (baseAddress === null) {
            return;
        }

        let listening = true;

        void readMailAccounts({ baseAddress, authorization: stubAuthorization }, stubTransport).then((answer) => {
            if (listening) {
                setResult(answer);
            }
        });

        return () => {
            listening = false;
        };
    }, [baseAddress]);

    function reached(reachedDeployment: DeploymentAddress): void {
        storeDeployment(reachedDeployment);
        setResult(null);
        setAdopted({ deployment: reachedDeployment, chosen: true });
    }

    function pointSomewhereElse(): void {
        forgetDeployment();
        setResult(null);
        setAdopted(null);
    }

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

                {adopted === null ? (
                    <ConnectDeployment send={send} onReached={reached} />
                ) : (
                    <div className="flex flex-col gap-6" ref={mail} tabIndex={-1}>
                        {adopted.chosen ? (
                            <ChosenDeployment address={adopted.deployment.baseAddress} onChange={pointSomewhereElse} />
                        ) : null}

                        {result === null ? <p>{translate('accounts.reading')}</p> : <Accounts result={result} />}
                    </div>
                )}
            </div>
        </main>
    );
}

// Offered only where somebody named the deployment themselves. An origin that served the client is not something
// changing an address could move, so a client served by its own deployment is not asked to be pointed anywhere.
function ChosenDeployment({ address, onChange }: { readonly address: string; readonly onChange: () => void }) {
    const { translate } = useLocalization();

    return (
        <section className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
            <p>{translate('deployment.reachedAt', { address })}</p>
            <button className="rounded-md bg-fathom-800/40 px-3 py-1 text-fathom-200" type="button" onClick={onChange}>
                {translate('deployment.change')}
            </button>
        </section>
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
