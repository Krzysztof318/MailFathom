// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    reachDeployment,
    resolveDeploymentEntry,
    type ClientFailureReason,
    type DeploymentAddress,
    type DeploymentEntryRefusal,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { DeploymentTransport } from './sendToDeployment';

// The screen somebody meets when nothing has told the client where its deployment is. It collects an address and
// proves something MailFathom-shaped answers at it; the credential presented against it afterwards is another screen's.
//
// Nothing about the address is decided here. What may be typed, what the scheme is, and when plain HTTP is refused all
// belong to `Client.Backend`, because they are properties of the wire the credential will travel on rather than of the
// form that collects them. What is this component's own is what a person sees while that is being decided.

/** Why the client is not pointed at a deployment yet: the entry was refused, or the deployment did not answer as one. */
type ConnectRefusal = DeploymentEntryRefusal | ClientFailureReason;

const refusalMessages: Readonly<Record<ConnectRefusal, MessageKey>> = {
    blank: 'connect.blank',
    malformed: 'connect.malformed',
    clearTextRefused: 'connect.clearTextRefused',
    unavailable: 'connect.unavailable',
    unreadable: 'connect.unreadable',

    // The session route is published under no permission, so a deployment answering either of these has refused for a
    // reason a person entering an address cannot act on. They are named rather than left out because the set is closed
    // by its own type, and a reason added to it should stop compiling here until this screen says what it reads as.
    unauthenticated: 'connect.refused',
    unauthorized: 'connect.refused',
};

export function ConnectDeployment({
    send,
    onReached,
}: {
    readonly send: DeploymentTransport;
    readonly onReached: (deployment: DeploymentAddress) => void;
}) {
    const { translate } = useLocalization();
    const [entry, setEntry] = useState('');
    const [clearTextPermitted, setClearTextPermitted] = useState(false);
    const [reaching, setReaching] = useState(false);
    const [refusal, setRefusal] = useState<ConnectRefusal | null>(null);
    const address = useRef<HTMLInputElement>(null);
    const attempt = useRef<AbortController | null>(null);

    // The view changed, so focus is placed rather than left wherever the previous screen had it. Moving focus is an
    // imperative browser API, which is what an effect is for.
    useEffect(() => {
        address.current?.focus();
    }, []);

    async function connect(): Promise<void> {
        const resolved = resolveDeploymentEntry(entry, clearTextPermitted);
        if (resolved.outcome === 'refused') {
            setRefusal(resolved.refusal);

            return;
        }

        const attempted = new AbortController();
        attempt.current = attempted;

        setRefusal(null);
        setReaching(true);

        const greeting = await reachDeployment(resolved.deployment, send(attempted.signal));

        // An abandoned attempt has no answer, whatever the wire eventually said: the screen is already back where the
        // person left it, and reporting a failure here would be this attempt overwriting what they did next.
        if (attempted.signal.aborted) {
            return;
        }

        attempt.current = null;
        setReaching(false);

        if (greeting.outcome === 'failed') {
            setRefusal(greeting.failure.reason);

            return;
        }

        onReached(resolved.deployment);
    }

    // A deployment that accepts the connection and never answers would otherwise hold the screen on `connect.reaching`
    // with the only control on it disabled, which is a state nobody can leave. Abandoning frees the connection as well
    // as the screen, which is why it aborts the request rather than only ignoring what it says.
    function abandon(): void {
        attempt.current?.abort();
        attempt.current = null;
        setReaching(false);
    }

    return (
        <section className="flex flex-col gap-6">
            <div className="flex flex-col gap-2">
                <h2 className="text-xl font-semibold text-white">{translate('connect.title')}</h2>
                <p className="text-sm">{translate('connect.explanation')}</p>
            </div>

            <form
                className="flex flex-col gap-5"
                onSubmit={(event) => {
                    event.preventDefault();
                    void connect();
                }}
            >
                <div className="flex flex-col gap-2">
                    <label className="text-sm font-medium text-white" htmlFor="deployment-address">
                        {translate('connect.address')}
                    </label>
                    <input
                        // The refusal joins the hint rather than replacing it, so somebody reading the field hears why
                        // it was refused and what it wants, in that order, without moving off it.
                        aria-describedby={
                            refusal === null ? 'deployment-address-hint' : 'deployment-refusal deployment-address-hint'
                        }
                        aria-invalid={refusal !== null}
                        autoComplete="off"
                        className="rounded-md border border-fathom-800 bg-fathom-800/40 px-3 py-2 text-white"
                        id="deployment-address"
                        inputMode="url"
                        ref={address}
                        spellCheck={false}
                        type="text"
                        value={entry}
                        onChange={(event) => {
                            setEntry(event.target.value);
                            setRefusal(null);
                        }}
                    />
                    <p className="text-sm text-fathom-500" id="deployment-address-hint">
                        {translate('connect.addressHint')}
                    </p>
                </div>

                <div className="flex flex-col gap-2">
                    <label className="flex items-center gap-2 text-sm font-medium text-white">
                        <input
                            aria-describedby="deployment-clear-text-explanation"
                            checked={clearTextPermitted}
                            type="checkbox"
                            onChange={(event) => {
                                setClearTextPermitted(event.target.checked);
                                setRefusal(null);
                            }}
                        />
                        {translate('connect.clearText')}
                    </label>
                    <p className="text-sm text-fathom-star" id="deployment-clear-text-explanation">
                        {translate('connect.clearTextExplanation')}
                    </p>
                </div>

                <div className="flex items-center gap-3">
                    <button
                        className="rounded-md bg-fathom-600 px-4 py-2 font-medium text-white disabled:opacity-60"
                        disabled={reaching}
                        type="submit"
                    >
                        {translate('connect.submit')}
                    </button>

                    {reaching ? (
                        <button
                            className="rounded-md bg-fathom-800/40 px-4 py-2 font-medium text-fathom-200"
                            type="button"
                            onClick={abandon}
                        >
                            {translate('connect.abandon')}
                        </button>
                    ) : null}
                </div>
            </form>

            {reaching ? <p role="status">{translate('connect.reaching')}</p> : null}

            {refusal === null || reaching ? null : (
                <p className="text-fathom-star" id="deployment-refusal" role="alert">
                    {translate(refusalMessages[refusal])}
                </p>
            )}
        </section>
    );
}
