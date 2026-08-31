// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    reachDeployment,
    resolveDeploymentEntry,
    signIn,
    type ClientFailureReason,
    type DeploymentAddress,
    type DeploymentEntryRefusal,
    type DeploymentEntryResult,
    type SignInRefusal,
} from '@mailfathom/client-backend';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { resolveCredentialEntry, type CredentialEntryRefusal } from './credentialEntry';
import type { CredentialLifetime } from './credentialStore';

// The screen somebody meets before any mail: it collects the credential, and the address beside it wherever nothing has
// already said where the deployment is. Those are one form rather than two screens because a person was handed all four
// values together, and splitting them would make the first half read as configuration.
//
// Which of the two shapes is rendered follows from whether an address arrived, not from which head this is — a web
// bundle is served by its deployment and so arrives with one, and a shell loaded from a scheme of its own does not.
// What is decided here is only what a person sees; the address rule and the credential's own encoding each belong to
// the module that owns them.

/** Everything this screen can be stopped by, whether it was decided here, by the deployment, or by the wire. */
type SignInScreenRefusal = DeploymentEntryRefusal | CredentialEntryRefusal | SignInRefusal | ClientFailureReason;

/** Which control a refusal is about, so the field that has to change is the one marked as needing it. */
type RefusedControl = 'address' | 'userName' | 'nothing';

// One lookup, exhaustive by its own type, so a refusal added to any of the four sets fails to compile here until this
// screen says what it reads as and which control it belongs to.
const refusals: Readonly<Record<SignInScreenRefusal, { message: MessageKey; control: RefusedControl }>> = {
    blank: { message: 'connect.blank', control: 'address' },
    malformed: { message: 'connect.malformed', control: 'address' },
    clearTextRefused: { message: 'connect.clearTextRefused', control: 'address' },

    incomplete: { message: 'signIn.incomplete', control: 'userName' },
    userNameHasColon: { message: 'signIn.userNameHasColon', control: 'userName' },

    credentialRefused: { message: 'signIn.credentialRefused', control: 'userName' },
    basicNotOffered: { message: 'signIn.basicNotOffered', control: 'nothing' },

    // A deployment that answered a credential with either of these has said something about the grant rather than about
    // the password, so neither marks a control: retyping a password changes nothing about a permission an owner is
    // missing. They are named because the set is closed by its own type rather than because this screen expects one.
    unauthenticated: { message: 'signIn.credentialRefused', control: 'userName' },
    unauthorized: { message: 'signIn.grantMissing', control: 'nothing' },
    unavailable: { message: 'connect.unavailable', control: 'nothing' },
    unreadable: { message: 'connect.unreadable', control: 'nothing' },
};

const lifetimeMessages: Readonly<Record<CredentialLifetime, MessageKey>> = {
    untilSignedOut: 'signIn.keptUntilSignedOut',
    untilTheTabCloses: 'signIn.keptUntilTheTabCloses',
    untilTheClientCloses: 'signIn.keptUntilTheClientCloses',
};

const fieldStyle = 'rounded-md border border-line bg-panel px-3 py-2 text-text';

// The most of either half a person may type. A credential is a name and a password rather than a document, and the
// value composed from them travels on every request this client makes.
const longestCredentialPart = 256;

export function SignIn({
    deployment,
    lifetime,
    credentialNoLongerAccepted,
    send,
    onSignedIn,
}: {
    readonly deployment: DeploymentAddress | null;
    readonly lifetime: CredentialLifetime;
    readonly credentialNoLongerAccepted: boolean;
    readonly send: DeploymentTransport;
    readonly onSignedIn: (deployment: DeploymentAddress, authorization: string) => void;
}) {
    const { translate } = useLocalization();
    const [entry, setEntry] = useState('');
    const [clearTextPermitted, setClearTextPermitted] = useState(false);
    const [userName, setUserName] = useState('');
    const [password, setPassword] = useState('');
    const [presenting, setPresenting] = useState(false);
    const [refusal, setRefusal] = useState<SignInScreenRefusal | null>(null);
    const address = useRef<HTMLInputElement>(null);
    const name = useRef<HTMLInputElement>(null);
    const attempt = useRef<AbortController | null>(null);

    // The view changed, so focus is placed rather than left wherever the previous screen had it, on the first thing
    // this form is asking to have filled. Moving focus is an imperative browser API, which is what an effect is for.
    useEffect(() => {
        (address.current ?? name.current)?.focus();
    }, []);

    const shown = refusal === null ? null : refusals[refusal];
    const describedBy = (hint: string): string => (shown === null ? hint : `sign-in-refusal ${hint}`);

    async function present(): Promise<void> {
        const reached: DeploymentEntryResult =
            deployment === null
                ? resolveDeploymentEntry(entry, clearTextPermitted)
                : { outcome: 'resolved', deployment };

        if (reached.outcome === 'refused') {
            setRefusal(reached.refusal);

            return;
        }

        const credential = resolveCredentialEntry(userName, password);

        if (credential.outcome === 'refused') {
            setRefusal(credential.refusal);

            return;
        }

        const attempted = new AbortController();
        attempt.current = attempted;

        setRefusal(null);
        setPresenting(true);

        // An abandoned attempt has no answer, whatever the wire eventually said: the screen is already back where the
        // person left it, and reporting a refusal here would be this attempt overwriting what they did next.
        const stopped = (reason: SignInScreenRefusal): void => {
            if (attempted.signal.aborted) {
                return;
            }

            attempt.current = null;
            setPresenting(false);
            setRefusal(reason);
        };

        // An address typed on this screen is asked what it is before it is handed a password, which is the whole
        // reason there are two requests here. An address that arrived from the edge is not: the origin that served
        // this client is the deployment, and there is nothing about it left to establish.
        if (deployment === null) {
            const greeting = await reachDeployment(reached.deployment, send(attempted.signal));

            if (attempted.signal.aborted) {
                return;
            }

            if (greeting.outcome === 'failed') {
                stopped(greeting.failure.reason);

                return;
            }

            if (!greeting.value.acceptsPassword) {
                stopped('basicNotOffered');

                return;
            }
        }

        const answer = await signIn(
            { baseAddress: reached.deployment.baseAddress, authorization: credential.authorization },
            send(attempted.signal),
        );

        if (attempted.signal.aborted) {
            return;
        }

        if (answer.outcome === 'failed') {
            stopped(answer.failure.reason);

            return;
        }

        if (!answer.value.signedIn) {
            stopped(answer.value.refusal);

            return;
        }

        attempt.current = null;
        setPresenting(false);
        onSignedIn(reached.deployment, credential.authorization);
    }

    // A deployment that accepts the connection and never answers would otherwise hold the screen on `signIn.presenting`
    // with the only control on it disabled, which is a state nobody can leave. Abandoning frees the connection as well
    // as the screen, which is why it aborts the request rather than only ignoring what it says.
    function abandon(): void {
        attempt.current?.abort();
        attempt.current = null;
        setPresenting(false);
    }

    return (
        <section className="flex flex-col gap-6">
            <div className="flex flex-col gap-2">
                <h2 className="text-xl font-semibold text-text">{translate('signIn.title')}</h2>
                <p className="text-sm">{translate('signIn.explanation')}</p>
            </div>

            {credentialNoLongerAccepted ? (
                <p className="text-warning" role="status">
                    {translate('signIn.noLongerAccepted')}
                </p>
            ) : null}

            <form
                className="flex flex-col gap-5"
                onSubmit={(event) => {
                    event.preventDefault();
                    void present();
                }}
            >
                {deployment === null ? (
                    <>
                        <div className="flex flex-col gap-2">
                            <label className="text-sm font-medium text-text" htmlFor="sign-in-address">
                                {translate('connect.address')}
                            </label>
                            <input
                                // The refusal joins the hint rather than replacing it, so somebody reading the field
                                // hears why it was refused and what it wants, in that order, without moving off it.
                                aria-describedby={describedBy('sign-in-address-hint')}
                                aria-invalid={shown?.control === 'address'}
                                autoComplete="off"
                                className={fieldStyle}
                                id="sign-in-address"
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
                            <p className="text-sm text-muted" id="sign-in-address-hint">
                                {translate('connect.addressHint')}
                            </p>
                        </div>

                        <div className="flex flex-col gap-2">
                            <label className="flex items-center gap-2 text-sm font-medium text-text">
                                <input
                                    aria-describedby="sign-in-clear-text-explanation"
                                    checked={clearTextPermitted}
                                    type="checkbox"
                                    onChange={(event) => {
                                        setClearTextPermitted(event.target.checked);
                                        setRefusal(null);
                                    }}
                                />
                                {translate('connect.clearText')}
                            </label>
                            <p className="text-sm text-warning" id="sign-in-clear-text-explanation">
                                {translate('connect.clearTextExplanation')}
                            </p>
                        </div>
                    </>
                ) : null}

                <div className="flex flex-col gap-2">
                    <label className="text-sm font-medium text-text" htmlFor="sign-in-user-name">
                        {translate('signIn.userName')}
                    </label>
                    <input
                        aria-describedby={describedBy('sign-in-kept')}
                        aria-invalid={shown?.control === 'userName'}
                        autoComplete="username"
                        className={fieldStyle}
                        id="sign-in-user-name"
                        maxLength={longestCredentialPart}
                        ref={name}
                        spellCheck={false}
                        type="text"
                        value={userName}
                        onChange={(event) => {
                            setUserName(event.target.value);
                            setRefusal(null);
                        }}
                    />
                </div>

                <div className="flex flex-col gap-2">
                    <label className="text-sm font-medium text-text" htmlFor="sign-in-password">
                        {translate('signIn.password')}
                    </label>
                    <input
                        aria-describedby={describedBy('sign-in-kept')}
                        aria-invalid={shown?.control === 'userName'}
                        autoComplete="current-password"
                        className={fieldStyle}
                        id="sign-in-password"
                        maxLength={longestCredentialPart}
                        type="password"
                        value={password}
                        onChange={(event) => {
                            setPassword(event.target.value);
                            setRefusal(null);
                        }}
                    />
                </div>

                <div className="flex items-center gap-3">
                    <button
                        className="rounded-md bg-accent px-4 py-2 font-medium text-on-accent transition disabled:opacity-60"
                        disabled={presenting}
                        type="submit"
                    >
                        {translate('signIn.submit')}
                    </button>

                    {presenting ? (
                        <button
                            className="rounded-md border border-line bg-panel px-4 py-2 font-medium text-text-soft transition hover:bg-hover"
                            type="button"
                            onClick={abandon}
                        >
                            {translate('signIn.abandon')}
                        </button>
                    ) : null}
                </div>
            </form>

            <p className="text-sm text-muted" id="sign-in-kept">
                {translate(lifetimeMessages[lifetime])}
            </p>

            {presenting ? <p role="status">{translate('signIn.presenting')}</p> : null}

            {shown === null || presenting ? null : (
                <p className="text-warning" id="sign-in-refusal" role="alert">
                    {translate(shown.message)}
                </p>
            )}
        </section>
    );
}
