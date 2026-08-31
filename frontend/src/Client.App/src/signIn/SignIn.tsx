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
import { SecondaryButton } from '../controls/SecondaryButton';
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

/**
 * What the screen has to say before anybody types, which is about the previous session rather than about this one.
 *
 * The screen takes a list rather than one of these, because two of them are true together: a credential the deployment
 * stopped accepting is cleared, and the store may refuse to delete what was kept — which leaves the person signed out
 * for one reason and still carrying a password for another.
 */
export type SignInNotice = 'credentialNoLongerAccepted' | 'passwordNotRemoved';

const noticeMessages: Readonly<Record<SignInNotice, MessageKey>> = {
    credentialNoLongerAccepted: 'signIn.noLongerAccepted',
    passwordNotRemoved: 'signIn.notRemoved',
};

/** Everything this screen can be stopped by, whether it was decided here, by the deployment, or by the wire. */
type SignInScreenRefusal = DeploymentEntryRefusal | CredentialEntryRefusal | SignInRefusal | ClientFailureReason;

/** Which controls a refusal is about, so the fields that have to change are the ones marked as needing it. */
type RefusedControl = 'address' | 'userName' | 'password';

interface Refusal {
    readonly message: MessageKey;

    /**
     * The controls this refusal is about, and only those.
     *
     * A refusal about the user name alone announces the password valid, because telling somebody that a field they
     * filled in correctly is wrong is worse than saying nothing about it. A refusal about neither — the deployment's
     * own configuration, or the wire — marks nothing at all.
     */
    readonly controls: readonly RefusedControl[];
}

// One lookup, exhaustive by its own type, so a refusal added to any of the four sets fails to compile here until this
// screen says what it reads as and which controls it belongs to.
const refusals: Readonly<Record<SignInScreenRefusal, Refusal>> = {
    blank: { message: 'connect.blank', controls: ['address'] },
    malformed: { message: 'connect.malformed', controls: ['address'] },
    clearTextRefused: { message: 'connect.clearTextRefused', controls: ['address'] },

    incomplete: { message: 'signIn.incomplete', controls: ['userName', 'password'] },
    userNameHasColon: { message: 'signIn.userNameHasColon', controls: ['userName'] },
    tooLong: { message: 'signIn.tooLong', controls: ['userName', 'password'] },

    credentialRefused: { message: 'signIn.credentialRefused', controls: ['userName', 'password'] },
    basicNotOffered: { message: 'signIn.basicNotOffered', controls: [] },

    // `unauthenticated` is the answer `credentialRefused` already is — a 401 whose challenge did not prove MailFathom
    // wrote it — so it reads as a refused credential and marks both halves of one. `unauthorized` is the one that says
    // something about the grant rather than about the password, and it marks nothing: retyping a password changes
    // nothing about a permission an owner is missing. Both are named because the set is closed by its own type rather
    // than because this screen expects either.
    unauthenticated: { message: 'signIn.credentialRefused', controls: ['userName', 'password'] },
    unauthorized: { message: 'signIn.grantMissing', controls: [] },
    unavailable: { message: 'connect.unavailable', controls: [] },
    unreadable: { message: 'connect.unreadable', controls: [] },
};

// What "nothing answered" reads as where there is no address on the screen. `connect.unavailable` asks somebody to
// check an address and check that the deployment is running, and neither half is theirs to act on when the origin that
// served this page is the deployment — so the same outcome takes the sentence that fits the shape it is rendered in.
const silentDeployment: Refusal = { message: 'signIn.deploymentSilent', controls: [] };

const lifetimeMessages: Readonly<Record<CredentialLifetime, MessageKey>> = {
    untilSignedOut: 'signIn.keptUntilSignedOut',
    untilTheTabCloses: 'signIn.keptUntilTheTabCloses',
    untilTheClientCloses: 'signIn.keptUntilTheClientCloses',
};

const fieldStyle = 'rounded-md border border-line bg-panel px-3 py-2 text-text';

export function SignIn({
    deployment,
    lifetime,
    notices,
    send,
    onSignedIn,
}: {
    readonly deployment: DeploymentAddress | null;
    readonly lifetime: CredentialLifetime;
    readonly notices: readonly SignInNotice[];
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
    const submit = useRef<HTMLButtonElement>(null);
    const attempt = useRef<AbortController | null>(null);
    const started = useRef(false);

    // The view changed, so focus is placed rather than left wherever the previous screen had it, on the first thing
    // this form is asking to have filled. Moving focus is an imperative browser API, which is what an effect is for.
    useEffect(() => {
        (address.current ?? name.current)?.focus();
    }, []);

    // An attempt disables the control that started it, and the browser drops focus to the document when it does — so
    // an attempt that ends leaves the refusal announced with focus nowhere and somebody reading by keyboard tabbing in
    // from the top of the page. Focus goes back to that control, and it is placed here rather than where the attempt
    // ended because a disabled element cannot take focus: this runs after the render that re-enabled it.
    //
    // The ref is what separates an attempt that ended from the first render, and it survives `StrictMode` invoking
    // this twice because both invocations read the same `presenting`.
    useEffect(() => {
        if (presenting) {
            started.current = true;

            return;
        }

        if (started.current) {
            submit.current?.focus();
        }
    }, [presenting]);

    const shown = refusal === null ? null : shownFor(refusal, deployment);
    const describedBy = (hint: string): string => (shown === null ? hint : `sign-in-refusal ${hint}`);
    const marks = (control: RefusedControl): boolean => shown?.controls.includes(control) === true;

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

        const running = new AbortController();
        attempt.current = running;

        setRefusal(null);
        setPresenting(true);

        // An abandoned attempt has no answer, whatever the wire eventually said: the screen is already back where the
        // person left it, and reporting a refusal here would be this attempt overwriting what they did next.
        //
        // Focus returns to the control that started the attempt, because starting one disabled that control and the
        // browser dropped focus to the document when it did. Without this the refusal is announced with focus nowhere,
        // and somebody reading by keyboard tabs in from the top of the page to reach the field it named.
        const stopped = (reason: SignInScreenRefusal): void => {
            if (running.signal.aborted) {
                return;
            }

            attempt.current = null;
            setPresenting(false);
            setRefusal(reason);
        };

        // An address typed on this screen is asked what it is before it is handed a password, which is the whole
        // reason there are two requests here. An address this screen was handed is not, whichever way it arrived: an
        // origin that served the client is the deployment by definition, and an address somebody chose was asked this
        // question on the run they chose it — which is why the stored one is not asked again on every later start.
        if (deployment === null) {
            const greeting = await reachDeployment(reached.deployment, send(running.signal));

            if (running.signal.aborted) {
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
            send(running.signal),
        );

        if (running.signal.aborted) {
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
    // as the screen, which is why it aborts the request rather than only ignoring what it says. Focus goes back to the
    // control this one stood beside, placed by the effect above, since this one is about to leave the document.
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

            {notices.map((notice) => (
                <p key={notice} className="text-warning" role="status">
                    {translate(noticeMessages[notice])}
                </p>
            ))}

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
                                aria-invalid={marks('address')}
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

                {/* Neither field carries a `maxLength`, deliberately: it truncates a paste without saying so, and a
                    password silently shortened is refused by the deployment and read back as a wrong password.
                    `resolveCredentialEntry` refuses what is too long by name instead. */}
                <div className="flex flex-col gap-2">
                    <label className="text-sm font-medium text-text" htmlFor="sign-in-user-name">
                        {translate('signIn.userName')}
                    </label>
                    <input
                        aria-describedby={describedBy('sign-in-kept')}
                        aria-invalid={marks('userName')}
                        autoComplete="username"
                        className={fieldStyle}
                        id="sign-in-user-name"
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
                        aria-invalid={marks('password')}
                        autoComplete="current-password"
                        className={fieldStyle}
                        id="sign-in-password"
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
                        ref={submit}
                        type="submit"
                    >
                        {translate('signIn.submit')}
                    </button>

                    {presenting ? (
                        <SecondaryButton label={translate('signIn.abandon')} shape="form" onActivate={abandon} />
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

/** What a refusal reads as on the shape this screen is actually rendering. */
function shownFor(refusal: SignInScreenRefusal, deployment: DeploymentAddress | null): Refusal {
    return refusal === 'unavailable' && deployment !== null ? silentDeployment : refusals[refusal];
}
