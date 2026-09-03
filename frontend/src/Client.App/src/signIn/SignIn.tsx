// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { AdoptedDeployment } from '../deployment/adoptedDeployment';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AdvancedConnection } from './AdvancedConnection';
import { portForPermission, portOf, resolveConnection } from './connection';
import { resolveCredentialEntry, type CredentialEntryRefusal } from './credentialEntry';
import { CredentialNotices, type CredentialNotice } from './CredentialNotices';
import type { CredentialLifetime } from './credentialStore';

// The screen somebody meets before any mail: it collects the credential, and the address beside it wherever nothing has
// already said where the deployment is. Those are one form rather than two screens because a person was handed all four
// values together, and splitting them would make the first half read as configuration.
//
// Which shape is rendered follows from where the address came from, not from which head this is. Three answers, and
// each draws the address differently:
//
// - **Nobody has said.** The field is asked for and is editable, and the `Advanced` disclosure beside it holds the
//   permission an unsecured connection needs and what the entry resolved to.
// - **A deployment configured it.** The field is drawn and is not editable — somebody has to be able to see what they
//   are about to send a password over, and a hidden field says less than a locked one. The disclosure is not drawn at
//   all: every row in it is about an address this person cannot change, and the permission it holds arrived from the
//   same configuration, so it would offer a decision that has already been taken.
// - **The origin served the client, or the person named it on an earlier run.** The address is not on this form. A web
//   bundle is served by its deployment, and a chosen address is stated above this screen beside the way out of it.
//
// What is decided here is only what a person sees; the address rule, the precedence between configuration sources, and
// the credential's own encoding each belong to the module that owns them.

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

    // `unauthenticated` is the answer `credentialRefused` already is — a 401 whose challenge did prove MailFathom
    // wrote it, a 401 that did not being an unreadable answer instead — so it reads as a refused credential and marks
    // both halves of one. `unauthorized` is the one that says something about the grant rather than about the
    // password, and it marks nothing: retyping a password changes nothing about a permission an owner is missing.
    // Neither is reached from here — `signIn` and `reachDeployment` return neither — and both are named because the
    // failure set is closed by its own type rather than because this screen expects either.
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

// One shape for every field on this screen, stated once. The focus treatment is the design project's — the line goes
// to the accent and the tint widens behind it — and it is written with `focus-within` on the box rather than on the
// input, because the box is what the reveal control and the port hint stand inside.
//
// Everything that changes below the compact breakpoint is the same decision made twice: a control a finger has to hit
// is taller than one a pointer has to hit, and a field a phone keyboard would zoom the page into is one set below the
// size that browser treats as readable. So the generous size is the base and the compact one is the variant, which is
// the direction a mobile-first breakpoint reads in.
const fieldBox =
    'flex items-center gap-2 rounded-xl border border-line-strong bg-panel px-3 transition focus-within:border-accent focus-within:ring-3 focus-within:ring-accent-soft';

const fieldInput =
    'min-h-13 min-w-0 flex-1 bg-transparent text-xl text-text outline-none compact:min-h-11 compact:text-md';

const fieldLabel = 'text-sm font-medium text-text-soft';

export function SignIn({
    adopted,
    clearTextPermitted: configuredClearText,
    lifetime,
    notices,
    send,
    onSignedIn,
}: {
    readonly adopted: AdoptedDeployment | null;

    /** The clear-text permission a deployment configured, or `null` where it configured none. */
    readonly clearTextPermitted: boolean | null;

    readonly lifetime: CredentialLifetime;
    readonly notices: readonly CredentialNotice[];
    readonly send: DeploymentTransport;
    readonly onSignedIn: (deployment: DeploymentAddress, authorization: string) => void;
}) {
    const { translate } = useLocalization();
    const deployment = adopted === null ? null : adopted.deployment;
    const [entry, setEntry] = useState('');

    // Seeded from what a deployment configured, and left alone afterwards where it did: nothing this screen holds is
    // written back to the device store, so removing the setting removes it from the screen on the next start.
    const [clearTextPermitted, setClearTextPermitted] = useState(configuredClearText ?? false);
    const [userName, setUserName] = useState('');
    const [password, setPassword] = useState('');
    const [revealed, setRevealed] = useState(false);
    const [presenting, setPresenting] = useState(false);
    const [refusal, setRefusal] = useState<SignInScreenRefusal | null>(null);
    const address = useRef<HTMLInputElement>(null);
    const name = useRef<HTMLInputElement>(null);
    const submit = useRef<HTMLButtonElement>(null);
    const notified = useRef<HTMLDivElement>(null);
    const attempt = useRef<AbortController | null>(null);
    const started = useRef(false);

    // The view changed, so focus is placed rather than left wherever the previous screen had it: on what this screen
    // has to say about why it is back where there is something, and otherwise on the first thing the form is asking to
    // have filled. Moving focus is an imperative browser API, which is what an effect is for.
    useEffect(() => {
        (notified.current ?? address.current ?? name.current)?.focus();
    }, []);

    // An attempt is answered for the deployment it was started against, so one whose deployment was abandoned while it
    // ran is called off rather than allowed to answer: the way out of a chosen address sits above this form and stays
    // live while an attempt runs, and a client that signed somebody in here would write the credential back into the
    // store that was just asked to clear it, at the address they had pointed away from.
    useEffect(() => {
        return () => {
            attempt.current?.abort();
        };
    }, [deployment]);

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

        // Whatever called this attempt off — the person, or the deployment it was against being abandoned underneath
        // it — the form it disabled is what has to come back. Hanging that on the signal rather than on the caller is
        // what makes it hold for a wire that never answers at all, which no later step of this function would reach.
        running.signal.addEventListener(
            'abort',
            () => {
                if (attempt.current === running) {
                    attempt.current = null;
                    setPresenting(false);
                }
            },
            { once: true },
        );

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
    }

    // Which of the three address shapes this screen is in, and what the field then holds. A configured address is the
    // one somebody is shown rather than asked for, so it is read out of what was adopted rather than out of the entry.
    const configured = adopted?.origin === 'configured';
    const shownAddress = configured ? deployment?.baseAddress : deployment === null ? entry : undefined;

    // What the address on the screen resolves to, computed during render rather than held beside the entry: it is a
    // pure function of values this component already has, and a second piece of state kept in step with them is the
    // pair that eventually disagrees. It is `null` wherever there is no address on this form at all.
    //
    // A configured address is read back permitting clear text, for the reason a stored one is: it only became the
    // address this run uses by being resolved, and what is being asked here is what it resolved *to*.
    const connection =
        shownAddress === undefined ? null : resolveConnection(shownAddress, configured || clearTextPermitted);

    // What the connect control names while an attempt runs. It is the authority rather than the whole address because
    // that is what a person recognises, and it is read back the same way the summary is so the two cannot disagree —
    // including where the address is not on this form at all, which is the one case `connection` answers nothing for.
    const reaching = connection ?? (deployment === null ? null : resolveConnection(deployment.baseAddress, true));

    return (
        <section className="flex flex-col gap-6">
            <div className="flex flex-col gap-1">
                <h2 className="text-4xl font-semibold tracking-tight text-text">{translate('signIn.title')}</h2>
                <p className="text-base text-muted">{translate('signIn.explanation')}</p>
            </div>

            <CredentialNotices notices={notices} ref={notified} />

            <form
                className="flex flex-col gap-5"
                onSubmit={(event) => {
                    event.preventDefault();
                    void present();
                }}
            >
                {shownAddress === undefined ? null : (
                    <div className="flex flex-col gap-1.5">
                        <label className={fieldLabel} htmlFor="sign-in-address">
                            {translate('connect.address')}
                        </label>
                        <div className={fieldBox}>
                            <input
                                // The refusal joins the hint rather than replacing it, so somebody reading the field
                                // hears why it was refused and what it wants, in that order, without moving off it.
                                aria-describedby={describedBy('sign-in-address-hint')}
                                aria-invalid={marks('address')}
                                autoComplete="off"
                                className={fieldInput}
                                id="sign-in-address"
                                inputMode="url"
                                placeholder={configured ? undefined : translate('connect.addressExample')}
                                readOnly={configured}
                                ref={address}
                                spellCheck={false}
                                type="text"
                                value={shownAddress}
                                onChange={(event) => {
                                    setEntry(event.target.value);
                                    setRefusal(null);
                                }}
                            />

                            {/* The lock says the same thing the field's own read-only state already announces, for
                                somebody reading rather than listening. Decorative for exactly that reason. */}
                            {configured ? <Icon name="lock" className="size-4 shrink-0 text-faint" /> : null}

                            {/* The port this will actually reach, said beside the field while it is being typed. Out
                                of the accessibility tree because the sentence under the field says the same thing in
                                words, and hearing a bare number after every keystroke is noise. */}
                            {connection === null ? null : (
                                <span aria-hidden="true" className="shrink-0 text-xs whitespace-nowrap text-faint">
                                    {translate('connect.portHint', { port: portOf(connection) })}
                                </span>
                            )}
                        </div>
                        <p className="text-xs text-muted" id="sign-in-address-hint">
                            {configured
                                ? translate('connect.addressConfigured')
                                : translate('connect.addressHint', {
                                      port:
                                          connection === null
                                              ? portForPermission(clearTextPermitted)
                                              : portOf(connection),
                                  })}
                        </p>
                    </div>
                )}

                {/* Neither field carries a `maxLength`, deliberately: it truncates a paste without saying so, and a
                    password silently shortened is refused by the deployment and read back as a wrong password.
                    `resolveCredentialEntry` refuses what is too long by name instead. */}
                <div className="flex flex-col gap-1.5">
                    <label className={fieldLabel} htmlFor="sign-in-user-name">
                        {translate('signIn.userName')}
                    </label>
                    <div className={fieldBox}>
                        <input
                            aria-describedby={describedBy('sign-in-kept')}
                            aria-invalid={marks('userName')}
                            autoComplete="username"
                            className={fieldInput}
                            id="sign-in-user-name"
                            placeholder={translate('signIn.userNameExample')}
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
                </div>

                <div className="flex flex-col gap-1.5">
                    <label className={fieldLabel} htmlFor="sign-in-password">
                        {translate('signIn.password')}
                    </label>
                    <div className={fieldBox}>
                        <input
                            aria-describedby={describedBy('sign-in-kept')}
                            aria-invalid={marks('password')}
                            autoComplete="current-password"
                            className={fieldInput}
                            id="sign-in-password"
                            type={revealed ? 'text' : 'password'}
                            value={password}
                            onChange={(event) => {
                                setPassword(event.target.value);
                                setRefusal(null);
                            }}
                        />

                        {/* A real button rather than a word somebody clicks, so it is reachable from the keyboard and
                            announced as what it does. Its accessible name says the action; the word beside it is what
                            the design shows and is out of the accessibility tree because it would be read twice. */}
                        <button
                            aria-label={translate(
                                revealed ? 'signIn.hidePasswordControl' : 'signIn.revealPasswordControl',
                            )}
                            className="-me-2 flex min-h-12 shrink-0 items-center rounded-lg px-3 text-md text-muted transition hover:bg-hover hover:text-text compact:min-h-8 compact:px-2 compact:text-sm"
                            type="button"
                            onClick={() => {
                                setRevealed(!revealed);
                            }}
                        >
                            <span aria-hidden="true">
                                {translate(revealed ? 'signIn.hidePassword' : 'signIn.revealPassword')}
                            </span>
                        </button>
                    </div>
                </div>

                {/* Only where an address is being typed. A client served by its own deployment is not being pointed
                    anywhere, so there is no connection for a reader to check before handing over a password; and an
                    address a deployment configured is one every row in here would be about and none of them could
                    change, permission included. */}
                {deployment === null ? (
                    <AdvancedConnection
                        connection={connection}
                        clearTextPermitted={clearTextPermitted}
                        clearTextConfigured={configuredClearText !== null}
                        onPermitClearText={(permitted) => {
                            setClearTextPermitted(permitted);
                            setRefusal(null);
                        }}
                    />
                ) : null}

                {shown === null || presenting ? null : (
                    <p
                        className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-text"
                        id="sign-in-refusal"
                        role="alert"
                    >
                        {translate(shown.message)}
                    </p>
                )}

                <div className="flex items-center gap-3">
                    <button
                        className="flex min-h-13 flex-1 items-center justify-center gap-2 rounded-full bg-accent px-4 text-lg font-semibold text-on-accent transition hover:bg-accent-strong disabled:opacity-70 compact:min-h-11.5 compact:text-md"
                        disabled={presenting}
                        ref={submit}
                        type="submit"
                    >
                        {presenting ? <Spinner /> : null}
                        {presenting
                            ? translate('signIn.presenting', { address: reaching?.authority ?? '' })
                            : translate('signIn.submit')}
                    </button>

                    {presenting ? (
                        <SecondaryButton label={translate('signIn.abandon')} shape="form" onActivate={abandon} />
                    ) : null}
                </div>
            </form>

            <p className="text-xs text-muted" id="sign-in-kept">
                {translate(lifetimeMessages[lifetime])}
            </p>

            {/* The wait is drawn on the control that started it, which is where somebody looking at the screen reads
                it. A label changing is not something a screen reader announces, so the same sentence stands here in a
                live region as well — out of sight rather than out of the accessibility tree, because two copies of it
                on the screen would say the same thing twice. */}
            {presenting ? (
                <p className="sr-only" role="status">
                    {translate('signIn.presenting', { address: reaching?.authority ?? '' })}
                </p>
            ) : null}
        </section>
    );
}

// What a submit says while it is waiting. Out of the accessibility tree because the sentence below the form is what
// announces the wait; this is the same statement drawn, for a reader who is looking at the button they just pressed.
function Spinner() {
    return (
        <span
            aria-hidden="true"
            className="size-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
        />
    );
}

/** What a refusal reads as on the shape this screen is actually rendering. */
function shownFor(refusal: SignInScreenRefusal, deployment: DeploymentAddress | null): Refusal {
    return refusal === 'unavailable' && deployment !== null ? silentDeployment : refusals[refusal];
}
