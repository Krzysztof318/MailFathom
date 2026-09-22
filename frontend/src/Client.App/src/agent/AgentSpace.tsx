// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
    askAgent,
    deleteAgentConversation,
    steerAgentRun,
    stopAgentRun,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type RunFollowingSchedule,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useWideWorkspace } from '../shell/useWideWorkspace';
import { AgentComposer } from './AgentComposer';
import { ConversationHistory } from './ConversationHistory';
import { ConversationTabs } from './ConversationTabs';
import { ConversationThread } from './ConversationThread';
import { answerInFlight, threadOf } from './threadTurns';
import { newIdentifier } from './newIdentifier';
import { useConversationHistory } from './useConversationHistory';
import { useFollowedConversation } from './useFollowedConversation';

// The Agent: several conversations held open as tabs, the history beside them, and the thread in front with the field
// the reader tells the agent what to do in. Nothing here acts on its own — every block the agent composes is drawn as
// what it proposes, and sending, steering, stopping and deleting are each the reader's press and each a route of its
// own, so none of them waits for the signal connection and the screen draws from an ordinary read.

/** How the follower waits before reading a silent conversation again, which is the one thing it cannot do for itself. */
const whileTheConversationIsSilent: RunFollowingSchedule = {
    wait: (milliseconds) =>
        new Promise<void>((resolve) => {
            setTimeout(resolve, milliseconds);
        }),
};

const notSent: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'agent.notSent.unauthenticated',
    unauthorized: 'agent.notSent.unauthorized',
    unavailable: 'agent.notSent.unavailable',
    unreadable: 'agent.notSent.unreadable',
    missing: 'agent.notSent.missing',
};

const notDeleted: Readonly<Record<Exclude<ClientFailureReason, 'missing'>, MessageKey>> = {
    unauthenticated: 'agent.notDeleted.unauthenticated',
    unauthorized: 'agent.notDeleted.unauthorized',
    unavailable: 'agent.notDeleted.unavailable',
    unreadable: 'agent.notDeleted.unreadable',
};

function refusedDeletion(reason: ClientFailureReason): reason is Exclude<ClientFailureReason, 'missing'> {
    return reason !== 'missing';
}

const deletingCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.deleteMany.other',
    one: 'agent.deleteMany.one',
    two: 'agent.deleteMany.other',
    few: 'agent.deleteMany.few',
    many: 'agent.deleteMany.many',
    other: 'agent.deleteMany.other',
};

/**
 * @param status What the deployment is doing, which every space shows somewhere and this one shows in its header.
 * @param schedule How the follower waits, which a test replaces so nothing polls in it.
 */
export function AgentSpace({
    session,
    transport,
    status,
    schedule = whileTheConversationIsSilent,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly status: ReactNode;
    readonly schedule?: RunFollowingSchedule;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();
    const desktop = useDesktopComposition();

    const [current, setCurrent] = useState<string | null>(null);
    const [open, setOpen] = useState<readonly string[]>([]);
    const [revision, setRevision] = useState(0);
    const [selected, setSelected] = useState<readonly string[]>([]);
    const [deleting, setDeleting] = useState<readonly string[]>([]);
    const [unsent, setUnsent] = useState<ClientFailureReason | null>(null);
    const [undeleted, setUndeleted] = useState<Exclude<ClientFailureReason, 'missing'> | null>(null);
    const [historyShown, setHistoryShown] = useState(true);
    const [drawerOpen, setDrawerOpen] = useState(false);
    const [following, setFollowing] = useState(0);
    const [composerFocusAsked, setComposerFocusAsked] = useState(0);

    const asked = useRef<HTMLDialogElement>(null);
    const drawer = useRef<HTMLDialogElement>(null);

    // What the screen shows, read by a write that resolves after the reader may have moved on — so what it adopts and
    // what it pins to the bottom is decided against the conversation in front then, not when it was sent.
    const shown = useRef<string | null>(null);

    useEffect(() => {
        shown.current = current;
    }, [current]);

    // A conversation the deployment no longer holds is put down where it was held, and what is left is the empty state.
    const letGo = useCallback((conversation: string): void => {
        setOpen((before) => before.filter((held) => held !== conversation));
        setCurrent((now) => (now === conversation ? null : now));
        setRevision((before) => before + 1);
    }, []);

    const history = useConversationHistory(session, transport, revision);
    const followed = useFollowedConversation(session, transport, current, revision, schedule, letGo);

    // Folded once per arrival rather than per render, because the thread re-measures whenever the turns change.
    const turns = useMemo(() => threadOf(followed.entries), [followed.entries]);
    const inFlight = answerInFlight(turns);

    const titleOf = (conversation: string): string | null =>
        history.conversations.find((listed) => listed.id === conversation)?.title ?? null;

    useScreenLayer(drawerOpen, () => {
        drawer.current?.close();
    });

    // A window widened past the phone has a history column, so a drawer left open over it is closed rather than drawn
    // twice. It synchronizes with the platform's own dialog, which is why it is an effect.
    useEffect(() => {
        if (wide) {
            drawer.current?.close();
        }
    }, [wide]);

    function openConversation(conversation: string): void {
        setCurrent(conversation);
        setOpen((before) => (before.includes(conversation) ? before : [...before, conversation]));
        setUnsent(null);
        setFollowing((before) => before + 1);
        drawer.current?.close();
    }

    function startNew(): void {
        setCurrent(null);
        setUnsent(null);
        drawer.current?.close();
    }

    function closeTab(conversation: string): void {
        const remaining = open.filter((held) => held !== conversation);

        setOpen(remaining);

        // The strip goes with its second-last tab, and focus with it, so the field takes it.
        if (remaining.length < 2) {
            setComposerFocusAsked((before) => before + 1);
        }

        if (current === conversation) {
            setCurrent(remaining.at(-1) ?? null);
        }
    }

    async function send(text: string): Promise<string | null> {
        if (session === null) {
            return null;
        }

        const conversation = current ?? newIdentifier();
        const message = newIdentifier();

        const posted =
            current === null || inFlight === null
                ? await askAgent(session, transport, conversation, message, text)
                : await steerAgentRun(session, transport, conversation, inFlight.run, message, text);

        if (posted.outcome === 'failed') {
            setUnsent(posted.failure.reason);

            return null;
        }

        const inFront = shown.current === conversation || (current === null && shown.current === null);

        // A conversation opened while this one was being posted stays in front: a new one joins the tabs behind it,
        // and only the conversation still being looked at is pinned to its bottom again.
        if (current === null) {
            setOpen((before) => (before.includes(conversation) ? before : [...before, conversation]));
        }

        if (inFront) {
            setUnsent(null);
            setCurrent(conversation);
            setFollowing((before) => before + 1);
        }

        setRevision((before) => before + 1);

        return conversation;
    }

    function cancel(): void {
        if (session === null || current === null || inFlight === null) {
            return;
        }

        // What the stop wrote reaches the thread through the follower, which the conversation's own signal or its
        // interval reads — so nothing is drawn here on the stop's say-so, and a stop that failed leaves Cancel standing.
        void stopAgentRun(session, transport, current, inFlight.run).then(() => {
            setRevision((before) => before + 1);
        });
    }

    function askDeletion(conversations: readonly string[]): void {
        setDeleting(conversations);
        asked.current?.showModal();
    }

    async function deleteConversations(conversations: readonly string[]): Promise<void> {
        if (session === null) {
            return;
        }

        const answers = await Promise.all(
            conversations.map((conversation) => deleteAgentConversation(session, transport, conversation)),
        );

        // One already gone is what deleting it asked for, so only the other failures are said.
        const refused = answers.flatMap((answered) =>
            answered.outcome === 'failed' && refusedDeletion(answered.failure.reason) ? [answered.failure.reason] : [],
        );
        const gone = conversations.filter((_, at) => {
            const answered = answers[at];

            return answered !== undefined && (answered.outcome === 'read' || answered.failure.reason === 'missing');
        });

        setUndeleted(refused[0] ?? null);

        setSelected((before) => before.filter((conversation) => !gone.includes(conversation)));
        setOpen((before) => before.filter((conversation) => !gone.includes(conversation)));

        setCurrent((now) => (now !== null && gone.includes(now) ? null : now));

        setRevision((before) => before + 1);
    }

    function closeDrawer(): void {
        drawer.current?.close();
    }

    const historyPanel = (inDrawer: boolean) => (
        <ConversationHistory
            history={history}
            open={current}
            selected={selected}
            onOpen={openConversation}
            onSelected={setSelected}
            onNew={startNew}
            onAskDeletion={askDeletion}
            onReadAgain={() => {
                setRevision((before) => before + 1);
            }}
            onClose={inDrawer ? closeDrawer : null}
        />
    );

    const conversationTitle = current === null ? null : titleOf(current);

    return (
        <div className="flex min-h-0 flex-1 flex-col bg-sunken">
            {desktop && open.length > 1 ? (
                <ConversationTabs
                    open={open.map((id) => ({ id, title: titleOf(id) }))}
                    current={current}
                    onChoose={openConversation}
                    onClose={closeTab}
                    onNew={startNew}
                />
            ) : null}

            <header className="flex shrink-0 flex-wrap items-center gap-2.5 border-b border-line bg-panel px-3.5 py-2.75 workspace:h-14 workspace:flex-nowrap workspace:gap-4 workspace:px-6.5 workspace:py-0">
                <Control
                    label={translate('agent.history')}
                    icon="menu"
                    shape={wide ? 'outlined' : 'outlinedSymbol'}
                    {...(wide ? { pressed: historyShown } : {})}
                    onPress={() => {
                        if (wide) {
                            setHistoryShown((before) => !before);
                        } else {
                            drawer.current?.showModal();
                            setDrawerOpen(true);
                        }
                    }}
                />

                <Control
                    label={translate('agent.newConversation')}
                    icon="add"
                    shape={wide ? 'primary' : 'primarySymbol'}
                    onPress={startNew}
                />

                <h1 className="min-w-0 truncate text-xl font-semibold">
                    {conversationTitle ?? translate('space.agent')}
                </h1>

                {wide ? <p className="ms-auto shrink-0 text-base text-muted">{translate('agent.reach')}</p> : null}

                {status}
            </header>

            <div className="relative flex min-h-0 flex-1">
                {wide && historyShown ? (
                    <nav
                        aria-label={translate('agent.history')}
                        className="flex w-agent-history shrink-0 flex-col border-e border-line bg-rail"
                    >
                        {historyPanel(false)}
                    </nav>
                ) : null}

                {wide ? null : (
                    <dialog
                        ref={drawer}
                        aria-label={translate('agent.history')}
                        onClose={() => {
                            setDrawerOpen(false);
                        }}
                        className="fixed inset-y-0 left-0 m-0 h-full max-h-none w-drawer max-w-full border-0 border-e border-line bg-rail p-0 pt-safe-top pb-safe-bottom pl-safe-left text-text shadow-overlay backdrop:bg-scrim"
                    >
                        <div className="flex h-full flex-col">{historyPanel(true)}</div>
                    </dialog>
                )}

                <div className="flex min-h-0 min-w-0 flex-1 flex-col">
                    <ConversationThread
                        turns={turns}
                        reading={followed.reading}
                        failure={followed.failure}
                        status={inFlight === null ? undefined : inFlight.status}
                        settledThrough={followed.settledThrough}
                        following={following}
                    />

                    {undeleted === null ? null : (
                        <p role="alert" className="px-3.5 py-2 text-sm text-warning-text workspace:px-6.5">
                            {translate(notDeleted[undeleted])}
                        </p>
                    )}

                    <AgentComposer
                        running={inFlight !== null}
                        offersStarters={current === null}
                        notSent={unsent === null ? null : translate(notSent[unsent])}
                        conversation={current}
                        focusAsked={composerFocusAsked}
                        onSend={send}
                        onCancel={cancel}
                    />
                </div>
            </div>

            <Confirmation
                asked={asked}
                mark="delete"
                question={translate(deleting.length === 1 ? 'agent.deleteOne' : 'agent.deleteManyQuestion')}
                consequence={
                    deleting.length === 1
                        ? translate('agent.deleteOneConsequence', {
                              title: titleOf(deleting[0] ?? '') ?? translate('agent.newConversation'),
                          })
                        : translate(deletingCounted[new Intl.PluralRules(locale).select(deleting.length)], {
                              count: new Intl.NumberFormat(locale).format(deleting.length),
                          })
                }
                reversal={{ kind: 'permanent', said: translate('agent.deletePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('agent.delete'),
                        manner: 'destroy',
                        run: () => {
                            void deleteConversations(deleting);
                        },
                    },
                ]}
            />
        </div>
    );
}
