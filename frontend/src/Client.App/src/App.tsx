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
import { forgetDeployment, storeDeployment, type AdoptedDeployment } from './deployment/adoptedDeployment';
import { ConnectDeployment } from './deployment/ConnectDeployment';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { useLocalization } from './localization/useLocalization';
import { useSpace } from './routing/useSpace';
import { ConnectionSummary } from './shell/ConnectionSummary';
import { IntentField } from './shell/IntentField';
import { LanguageChoice, ThemeChoice } from './shell/Preferences';
import { Space } from './shell/Space';
import { SpaceNavigation } from './shell/SpaceNavigation';
import { stubAuthorization, stubTransport } from './stubMailFathom';

// The frame Discover, Mail, and Cases are held in, and the only thing in the client that survives moving between them.
// It is one tree laid out two ways by the width it is given — a rail beside a workspace, or bottom navigation under a
// stack of screens — and nothing in it asks which head or which platform it is running on.
//
// In front of it stands the question every run answers first: which deployment this client belongs to. That is a
// screen rather than a state of the frame, because three spaces with nothing behind them are a frame around nothing.
//
// What the accounts are read for here is the line above the space and the mailboxes the scope offers. Each account's
// own freshness, the session behind it, and the grants that decide what a space may show are the next stage's.

export function App({
    deployment,
    send,
}: {
    readonly deployment: AdoptedDeployment | null;
    readonly send: DeploymentTransport;
}) {
    const space = useSpace();
    const [adopted, setAdopted] = useState(deployment);
    const [accounts, setAccounts] = useState<ClientResult<MailAccountDirectory> | null>(null);
    const [attempt, setAttempt] = useState(0);
    const baseAddress = adopted === null ? null : adopted.deployment.baseAddress;
    const workspace = useRef<HTMLDivElement>(null);
    const focusedFor = useRef(adopted);

    // The view changed, so focus goes to the start of what replaced it rather than staying on a control that is no
    // longer there. Only in this direction: the connect screen places focus itself, on the field it is asking to have
    // filled, and a parent effect runs after a child's and would take it back off. A cold start is not a view change,
    // so opening against a deployment already adopted moves nothing.
    //
    // What separates the two is the deployment this effect last acted on rather than a flag saying the first render
    // has happened. React invokes an effect twice on mount under `StrictMode`, which `main.tsx` mounts the application
    // in, and a flag the first invocation cleared is already cleared when the second one reads it — so the guard would
    // pull focus onto the workspace on exactly the ordinary open it exists to leave alone. Both invocations see the
    // same adopted deployment, so a comparison against it survives being run twice.
    useEffect(() => {
        if (adopted === focusedFor.current) {
            return;
        }

        focusedFor.current = adopted;

        if (adopted !== null) {
            workspace.current?.focus();
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
                setAccounts(answer);
            }
        });

        return () => {
            listening = false;
        };
    }, [baseAddress, attempt]);

    // Reading again is a new attempt rather than a second copy of the read above: the effect owns the cancellation, so
    // the retry only says that the answer it holds is stale.
    function reread(): void {
        setAccounts(null);
        setAttempt((previous) => previous + 1);
    }

    function reached(reachedDeployment: DeploymentAddress): void {
        storeDeployment(reachedDeployment);
        setAccounts(null);
        setAdopted({ deployment: reachedDeployment, chosen: true });
    }

    function pointSomewhereElse(): void {
        forgetDeployment();
        setAccounts(null);
        setAdopted(null);
    }

    if (adopted === null) {
        return <ConnectScreen send={send} onReached={reached} />;
    }

    return (
        <div className="flex h-dvh flex-col bg-rail pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left workspace:flex-row">
            <div ref={workspace} tabIndex={-1} className="flex min-h-0 min-w-0 flex-1 flex-col bg-page">
                <header className="flex flex-wrap items-center gap-3 border-b border-line-soft bg-panel px-4 py-2 workspace:px-8">
                    <ConnectionSummary accounts={accounts} reread={reread} />

                    {adopted.chosen ? (
                        <ChosenDeployment address={adopted.deployment.baseAddress} onChange={pointSomewhereElse} />
                    ) : null}

                    <div className="ms-auto flex items-center gap-2">
                        <p className="font-mono text-xs text-faint">{__MAILFATHOM_VERSION__}</p>
                        <ThemeChoice />
                        <LanguageChoice />
                    </div>
                </header>

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
// who has not reached a deployment yet exactly as much as to somebody who has, and the frame that usually holds them
// is not on the screen at this point.
function ConnectScreen({
    send,
    onReached,
}: {
    readonly send: DeploymentTransport;
    readonly onReached: (reached: DeploymentAddress) => void;
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

                <ConnectDeployment send={send} onReached={onReached} />
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
            <button
                type="button"
                onClick={onChange}
                className="rounded-md border border-line px-2 py-0.5 text-sm text-text-soft transition hover:bg-hover"
            >
                {translate('deployment.change')}
            </button>
        </p>
    );
}
