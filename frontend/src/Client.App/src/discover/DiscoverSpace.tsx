// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState, type ReactNode } from 'react';
import type {
    ClientFailureReason,
    ClientSession,
    MailAccount,
    MailFathomTransport,
    RunFollowingSchedule,
} from '@mailfathom/client-backend';
import { AnswerCanvas } from '../answerCanvas/AnswerCanvas';
import { EvidenceInspector } from '../answerCanvas/EvidenceInspector';
import { RunStatus } from '../answerCanvas/RunStatus';
import { useFollowedRun } from '../answerCanvas/useFollowedRun';
import { useRunStopping } from '../answerCanvas/useRunStopping';
import { Control } from '../controls/Control';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useAgentHandOver } from '../routing/agentHandOver';
import { askScopeInForce, withAsked, type AskedQuestion } from '../workspace/askScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { DiscoverIdle } from './DiscoverIdle';
import { useStartedRun } from './useStartedRun';

// The client's own opening screen, and the one place a question asked anywhere in the application is answered. The
// parts it is composed of were each built before it — the canvas, the block renderers, the run's chrome, the evidence
// inspector and the follower — so what this owns is the screen they stand on: which question is being answered, the run
// answering it, and the way back to asking another.
//
// **The field stands under the head rather than at the foot.** The design project draws it there on this screen and at
// the foot of a correspondence, so the frame hands the field to the space in front and this is where Discover puts it.
//
// **A question is answered by following a run rather than by waiting for one.** Starting one answers with an
// identifier, and everything the run publishes is read from its own record — so a block draws as it arrives, a run that
// finished before anybody looked draws in full, and a deployment serving no signal channel costs the follower's
// interval rather than the answer.
//
// **An answer is carried on in the Agent space.** *Continue with the agent* stands in the head while a run exists and
// *Hand to the agent* under an answer that finished, and both hand the run over — so the conversation they open asks
// its next question about this answer rather than about the whole mailbox.
//
// **What the design draws and this does not, it does not draw inert.** *Save as Case* waits for a Case (#2087); the
// idle state's morning
// briefing and its *needs you today* list are derived before anybody asks, which #1174 defers as a question about
// unattended spend that nothing has settled. Each is left out rather than drawn as a control leading nowhere.

/** How the follower waits before reading a silent run again, which is the one thing it cannot do for itself. */
const whileTheRunIsSilent: RunFollowingSchedule = {
    wait: (milliseconds) =>
        new Promise<void>((resolve) => {
            setTimeout(resolve, milliseconds);
        }),
};

// Why a question was not accepted at all, which is a different thing from a run that was started and could not be read:
// nothing was spent, nothing is in flight, and what somebody does next is ask again rather than wait. Exhaustive by its
// own type, so a reason added to the failure model does not compile until this screen says what it would tell a reader.
const notAsked: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'discover.notAsked.unauthenticated',
    unauthorized: 'discover.notAsked.unauthorized',
    unavailable: 'discover.notAsked.unavailable',
    unreadable: 'discover.notAsked.unreadable',
    missing: 'discover.notAsked.unavailable',
};

/**
 * Discover: one question, the run answering it, and the evidence beside the answer.
 *
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How every read and the question itself go out.
 * @param accounts The mailboxes this user holds, which is what a scope is resolved against.
 * @param intent The question field the frame composes for the space in front.
 * @param status What the deployment is doing, which every space shows somewhere.
 * @param onOpenMessage Opens the message a citation names, which is the Mail space's to draw and the frame's to perform.
 * @param schedule How the follower waits, which a test replaces so nothing polls in it.
 */
export function DiscoverSpace({
    session,
    transport,
    accounts,
    intent,
    status,
    onOpenMessage,
    schedule = whileTheRunIsSilent,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly accounts: readonly MailAccount[];
    readonly intent: ReactNode;
    readonly status: ReactNode;
    readonly onOpenMessage: (storedEmailId: string) => void;
    readonly schedule?: RunFollowingSchedule;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();

    // The question somebody put away, held by the identity of the entry the field wrote: asking again writes another
    // one, so the screen comes back without this having to be cleared.
    const [putAway, setPutAway] = useState<AskedQuestion | null>(null);
    const [checking, setChecking] = useState<string | null>(null);

    const newest = workspace.askedBefore[0] ?? null;
    const asked = newest === putAway ? null : newest;

    const started = useStartedRun(session, transport, asked);
    const answer = useFollowedRun(session, transport, started.run, schedule);
    const stopping = useRunStopping(session, transport, started.run);

    const source = checking === null ? null : (answer.sources.get(checking) ?? null);

    const handToAgent = useAgentHandOver();
    const run = started.run;
    const handRunOver =
        handToAgent === null || run === null || asked === null
            ? null
            : () => {
                  handToAgent({ scope: { kind: 'discoveryRun', subject: run }, title: asked.question });
              };

    // Offered under an answer once it is whole, which is where the design puts it: a run still composing, or one that
    // could not be read, has nothing yet worth carrying anywhere.
    const answered = !answer.running && answer.failure === null && answer.blocks.length > 0;

    function ask(question: string): void {
        revise({
            question,
            askedBefore: withAsked(workspace.askedBefore, question, askScopeInForce(workspace, accounts)),
        });
    }

    function askSomethingElse(): void {
        setPutAway(newest);
        setChecking(null);
        revise({ question: '' });
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            <header className="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-line px-4 py-3 workspace:px-5.5">
                <h1 className="text-md font-semibold">{translate('space.discover')}</h1>

                {status}

                {asked === null ? null : (
                    <span className="ms-auto flex items-center gap-2.25">
                        {handRunOver === null ? null : (
                            <Control
                                label={translate('discover.continueWithAgent')}
                                icon="auto_awesome"
                                shape="primary"
                                onPress={handRunOver}
                            />
                        )}

                        <SecondaryButton label={translate('discover.newQuestion')} onActivate={askSomethingElse} />
                    </span>
                )}
            </header>

            {intent}

            <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 workspace:px-5.5">
                {asked === null ? (
                    <DiscoverIdle onAsk={ask} />
                ) : started.failure !== null ? (
                    /* The way out is the field directly above this, which is where the question was typed and where
                       asking again is one press — so the sentence says what happened and offers no control of its own. */
                    <p className="max-w-2xl text-md text-pretty" role="status">
                        {translate(notAsked[started.failure])}
                    </p>
                ) : (
                    <div className="flex flex-col gap-4">
                        <RunStatus
                            answer={answer}
                            stopping={stopping.stopping}
                            onStop={started.run === null ? undefined : stopping.stop}
                        />

                        <AnswerCanvas
                            blocks={answer.blocks}
                            sources={answer.sources}
                            onFollowSource={setChecking}
                            running={answer.running}
                            planSchemaVersion={answer.planSchemaVersion}
                            failure={answer.failure}
                            evidence={
                                <EvidenceInspector
                                    source={source}
                                    session={session}
                                    transport={transport}
                                    onDismiss={() => {
                                        setChecking(null);
                                    }}
                                    onOpenMessage={onOpenMessage}
                                />
                            }
                        />

                        {handRunOver !== null && answered ? (
                            <div className="flex flex-wrap items-center gap-3 rounded-lg border border-accent-line bg-accent-soft px-4 py-3.25">
                                <span className="rounded-xs bg-accent px-1.75 py-0.75 text-2xs tracking-wide text-on-accent">
                                    {translate('ai.badge')}
                                </span>

                                <p className="text-md text-pretty text-text-soft">
                                    {translate('discover.handToAgentPrompt')}
                                </p>

                                <Control
                                    label={translate('discover.handToAgent')}
                                    shape="primary"
                                    className="ms-auto"
                                    onPress={handRunOver}
                                />
                            </div>
                        ) : null}
                    </div>
                )}
            </div>
        </div>
    );
}
