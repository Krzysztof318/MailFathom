// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readMailAccounts,
    type ClientResult,
    type DeploymentAddress,
    type MailAccountDirectory,
} from '@mailfathom/client-backend';
import { SecondaryButton } from './controls/SecondaryButton';
import { forgetDeployment, storeDeployment, type AdoptedDeployment } from './deployment/adoptedDeployment';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { useLocalization } from './localization/useLocalization';
import { useSpace } from './routing/useSpace';
import { ConnectionSummary } from './shell/ConnectionSummary';
import { IntentField } from './shell/IntentField';
import { LanguageChoice, ThemeChoice } from './shell/Preferences';
import { Space } from './shell/Space';
import { SpaceNavigation } from './shell/SpaceNavigation';
import { CredentialNotices, type CredentialNotice } from './signIn/CredentialNotices';
import type { CredentialStore } from './signIn/credentialStore';
import { SignIn } from './signIn/SignIn';
import { emptyWorkspace } from './workspace/useWorkspace';
import { useWorkspace } from './workspace/useWorkspace';

// The frame Discover, Mail, and Cases are held in, and the only thing in the client that survives moving between them.
// It is one tree laid out two ways by the width it is given — a rail beside a workspace, or bottom navigation under a
// stack of screens — and nothing in it asks which head or which platform it is running on.
//
// In front of it stands what every run answers first: which deployment this client belongs to, and who is asking it.
// That is a screen rather than a state of the frame, because three spaces with nothing behind them are a frame around
// nothing — and the two halves of the answer are one screen because a person was handed all of it together.
//
// What the accounts are read for here is the line above the space and the mailboxes the scope offers. Each account's
// own freshness and the grants that decide what a space may show are the next stage's.

export function App({
    deployment,
    signedInWith,
    credentials,
    send,
}: {
    readonly deployment: AdoptedDeployment | null;
    readonly signedInWith: string | null;
    readonly credentials: CredentialStore;
    readonly send: DeploymentTransport;
}) {
    const space = useSpace();
    const { revise } = useWorkspace();
    const [adopted, setAdopted] = useState(deployment);
    const [authorization, setAuthorization] = useState(signedInWith);
    const [accounts, setAccounts] = useState<ClientResult<MailAccountDirectory> | null>(null);
    const [attempt, setAttempt] = useState(0);
    const [notices, setNotices] = useState<readonly CredentialNotice[]>([]);
    const baseAddress = adopted === null ? null : adopted.deployment.baseAddress;
    const workspace = useRef<HTMLDivElement>(null);
    const focusedFor = useRef(authorization);

    // The view changed, so focus goes to the start of what replaced it rather than staying on a control that is no
    // longer there. Only in this direction: the sign-in screen places focus itself, on the field it is asking to have
    // filled, and a parent effect runs after a child's and would take it back off. A cold start against a credential
    // that was kept is not a view change, so opening already signed in moves nothing.
    //
    // What separates the two is the credential this effect last acted on rather than a flag saying the first render
    // has happened. React invokes an effect twice on mount under `StrictMode`, which `main.tsx` mounts the application
    // in, and a flag the first invocation cleared is already cleared when the second one reads it — so the guard would
    // pull focus onto the workspace on exactly the ordinary open it exists to leave alone. Both invocations see the
    // same credential, so a comparison against it survives being run twice.
    useEffect(() => {
        if (authorization === focusedFor.current) {
            return;
        }

        focusedFor.current = authorization;

        if (authorization !== null) {
            workspace.current?.focus();
        }
    }, [authorization]);

    // The session is built from the address and the credential rather than held beside them, which is what makes a
    // credential unable to outlive the deployment it was presented to: pointing the client somewhere else, or signing
    // out, runs this again with nothing to present, and there is nowhere for the previous one's session to survive.
    useEffect(() => {
        if (baseAddress === null || authorization === null) {
            return;
        }

        const attempted = new AbortController();
        let listening = true;

        void readMailAccounts({ baseAddress, authorization }, send(attempted.signal)).then((answer) => {
            if (!listening) {
                return;
            }

            // A credential the deployment has stopped accepting is acted on once rather than left to produce the same
            // refusal on every later read, which is why this is the one failure the frame does not render. What was
            // kept goes with it: a stored password the service refuses is a password nothing will make work again.
            if (answer.outcome === 'failed' && answer.failure.reason === 'unauthenticated') {
                setNotices(['credentialNoLongerAccepted']);
                setAuthorization(null);
                setAccounts(null);
                revise(emptyWorkspace);

                void credentials.forget({ baseAddress }).then((removed) => {
                    if (!removed) {
                        setNotices((shown) => [...shown, 'passwordNotRemoved']);
                    }
                });

                return;
            }

            setAccounts(answer);
        });

        return () => {
            listening = false;
            attempted.abort();
        };
    }, [baseAddress, authorization, attempt, credentials, revise, send]);

    // Reading again is a new attempt rather than a second copy of the read above: the effect owns the cancellation, so
    // the retry only says that the answer it holds is stale.
    function reread(): void {
        setAccounts(null);
        setAttempt((previous) => previous + 1);
    }

    function signedIn(reached: DeploymentAddress, presented: string): void {
        if (adopted === null) {
            storeDeployment(reached);
            setAdopted({ deployment: reached, chosen: true });
        }

        setNotices([]);

        // The screen has already said how long the password will be kept, so a store that refused the write says so
        // rather than leaving somebody to discover it by being asked for the password again at the next start. This
        // one is read inside the frame: signing in worked, and what failed is only the keeping.
        void credentials.keep(reached, presented).then((stored) => {
            if (!stored) {
                setNotices(['passwordNotKept']);
            }
        });
        setAccounts(null);
        setAuthorization(presented);
    }

    // Everything this session held goes with the credential, including what the person carried between the spaces:
    // the question in the intent field and the mailbox it was scoped to are theirs rather than the machine's, and a
    // client that kept them would show the next person what the last one was asking about.
    //
    // A store that would not delete is reported rather than swallowed. The screen has already said that signing out is
    // what removes the password, so a refused deletion leaves it on the machine for the next start to read back while
    // the person believes they signed out.
    function signOut(): void {
        setNotices([]);
        setAccounts(null);
        setAuthorization(null);
        revise(emptyWorkspace);

        if (adopted !== null) {
            void credentials.forget(adopted.deployment).then((removed) => {
                if (!removed) {
                    setNotices(['passwordNotRemoved']);
                }
            });
        }
    }

    function pointSomewhereElse(): void {
        signOut();
        forgetDeployment();
        setAdopted(null);
    }

    if (baseAddress === null || authorization === null) {
        return (
            <SignInScreen
                deployment={adopted === null ? null : adopted.deployment}
                chosen={adopted?.chosen === true}
                lifetime={credentials.lifetime}
                notices={notices}
                send={send}
                onSignedIn={signedIn}
                onPointSomewhereElse={pointSomewhereElse}
            />
        );
    }

    return (
        <div className="flex h-dvh flex-col bg-rail pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left workspace:flex-row">
            <div ref={workspace} tabIndex={-1} className="flex min-h-0 min-w-0 flex-1 flex-col bg-page">
                <header className="flex flex-wrap items-center gap-3 border-b border-line-soft bg-panel px-4 py-2 workspace:px-8">
                    <ConnectionSummary accounts={accounts} reread={reread} />

                    {adopted?.chosen === true ? (
                        <ChosenDeployment address={baseAddress} onChange={pointSomewhereElse} />
                    ) : null}

                    <div className="ms-auto flex items-center gap-2">
                        <p className="font-mono text-xs text-faint">{__MAILFATHOM_VERSION__}</p>
                        <ThemeChoice />
                        <LanguageChoice />
                        <SignOut onSignOut={signOut} />
                    </div>
                </header>

                {/* Inside the frame as well as on the sign-in screen, because a credential that could not be kept is
                    learned about at the moment somebody successfully signed in — which is the one of these sentences
                    whose reader is already past that screen. */}
                {notices.length === 0 ? null : (
                    <div className="border-b border-line-soft bg-panel px-4 py-2 workspace:px-8">
                        <CredentialNotices notices={notices} />
                    </div>
                )}

                <IntentField accounts={accounts?.outcome === 'read' ? accounts.value.accounts : []} />

                <Space space={space} />
            </div>

            {/* Navigation is last in the document because the keyboard follows the document rather than the layout,
                and the narrow composition puts it at the bottom of the screen: written the other way round, a reader
                tabbing into a narrow window would reach the bottom bar before the header above it. The wide
                composition then carries the one mismatch CSS cannot remove — a rail drawn on the left out of a node
                that comes last — because no single document order matches both shapes, and content before navigation
                is the direction a skip link exists to manufacture rather than the one it works around. */}
            <SpaceNavigation current={space} />
        </div>
    );
}

// The screen in front of the frame, which carries the theme and the language controls itself: they belong to somebody
// who has not signed in yet exactly as much as to somebody who has, and the frame that usually holds them is not on the
// screen at this point.
function SignInScreen({
    deployment,
    chosen,
    lifetime,
    notices,
    send,
    onSignedIn,
    onPointSomewhereElse,
}: {
    readonly deployment: DeploymentAddress | null;
    readonly chosen: boolean;
    readonly lifetime: CredentialStore['lifetime'];
    readonly notices: readonly CredentialNotice[];
    readonly send: DeploymentTransport;
    readonly onSignedIn: (reached: DeploymentAddress, authorization: string) => void;
    readonly onPointSomewhereElse: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <main className="min-h-dvh bg-page px-4 py-8 pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left">
            <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
                <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line-soft pb-4">
                    <h1 className="text-2xl font-semibold tracking-tight">{translate('shell.title')}</h1>

                    <div className="flex items-center gap-2">
                        <p className="font-mono text-xs text-faint">{__MAILFATHOM_VERSION__}</p>
                        <ThemeChoice />
                        <LanguageChoice />
                    </div>
                </header>

                {/* The way out of an address somebody named themselves, offered here rather than only inside the
                    frame: a deployment that stopped accepting the credential, or one whose password is gone, leaves a
                    person on this screen with no address field to correct — and a chosen address is read back out of
                    storage on every later start, so reloading returns to the same one. */}
                {chosen && deployment !== null ? (
                    <ChosenDeployment address={deployment.baseAddress} onChange={onPointSomewhereElse} />
                ) : null}

                <SignIn
                    deployment={deployment}
                    lifetime={lifetime}
                    notices={notices}
                    send={send}
                    onSignedIn={onSignedIn}
                />
            </div>
        </main>
    );
}

// Offered only where somebody named the deployment themselves. An origin that served the client is not something
// changing an address could move, so a client served by its own deployment is not asked to be pointed anywhere.
function ChosenDeployment({ address, onChange }: { readonly address: string; readonly onChange: () => void }) {
    const { translate } = useLocalization();

    return (
        <p className="flex items-center gap-2 text-sm text-muted">
            {translate('deployment.reachedAt', { address })}
            <SecondaryButton label={translate('deployment.change')} onActivate={onChange} />
        </p>
    );
}

// Beside the two preferences rather than among them: signing out is what removes the credential this machine kept, so
// it belongs in the one place present at both widths rather than behind something a reader has to find.
function SignOut({ onSignOut }: { readonly onSignOut: () => void }) {
    const { translate } = useLocalization();

    return <SecondaryButton label={translate('shell.signOut')} onActivate={onSignOut} />;
}
