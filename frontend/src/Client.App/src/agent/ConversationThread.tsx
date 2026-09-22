// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientFailureReason } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AgentAnswer, AnswerScope } from './AgentAnswer';
import { ProposedNext } from './ProposedNext';
import { proposedNext, type ThreadTurn } from './threadTurns';
import { usePinnedToBottom } from './usePinnedToBottom';

const readFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'agent.readFailed.unauthenticated',
    unauthorized: 'agent.readFailed.unauthorized',
    unavailable: 'agent.readFailed.unavailable',
    unreadable: 'agent.readFailed.unreadable',
    missing: 'agent.readFailed.missing',
};

/**
 * The conversation in front: the reader's own questions on the right, everything the agent wrote on the left, and one
 * status line under the answer still being composed, which stands until nothing is left to load.
 *
 * @param status What the answer in flight said it was doing last, `undefined` where nothing is in flight, and `null`
 * where one is and has said nothing yet.
 * @param settledThrough What was already there when the conversation was opened, which draws without animating in.
 * @param following Changed whenever the reader asks something or opens a conversation, which pins the thread to its
 * bottom again.
 * @param onAsk Asks a question the last answer suggested, exactly as sending it from the field would.
 */
export function ConversationThread({
    turns,
    reading,
    failure,
    status,
    settledThrough,
    following,
    onAsk,
}: {
    readonly turns: readonly ThreadTurn[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
    readonly status: string | null | undefined;
    readonly settledThrough: number;
    readonly following: number;
    readonly onAsk: (question: string) => Promise<unknown>;
}) {
    const { translate } = useLocalization();
    const { attachScroller, onScroll, onGesture } = usePinnedToBottom(turns, following);
    const next = proposedNext(turns);

    return (
        <div
            ref={attachScroller}
            className="min-h-0 flex-1 overflow-y-auto"
            onScroll={onScroll}
            onWheel={onGesture}
            onTouchMove={onGesture}
        >
            <div
                role="log"
                aria-label={translate('agent.thread')}
                className="mx-auto flex w-full max-w-agent-thread flex-col gap-3.5 px-3.5 py-4 workspace:gap-4 workspace:px-6.5 workspace:py-5.5"
            >
                {turns.length === 0 && !reading && failure === null ? (
                    <div className="flex flex-col gap-2.75">
                        <AnswerScope scope="agent.scope.mailbox" />
                        <p className="max-w-agent-answer text-lg text-text-soft text-pretty">
                            {translate('agent.greeting')}
                        </p>
                    </div>
                ) : null}

                {turns.map((turn) =>
                    turn.kind === 'question' ? (
                        <p
                            key={turn.sequence}
                            className={`max-w-agent-question-narrow self-end rounded-lg bg-accent-soft px-3.5 py-2.75 text-lg text-text text-pretty whitespace-pre-wrap workspace:max-w-agent-question ${
                                turn.sequence > settledThrough ? 'animate-arrival' : ''
                            }`}
                        >
                            {turn.text}
                        </p>
                    ) : turn.kind === 'note' ? (
                        <div
                            key={turn.sequence}
                            className={`flex max-w-agent-answer flex-col gap-2.75 ${
                                turn.sequence > settledThrough ? 'animate-arrival' : ''
                            }`}
                        >
                            <AnswerScope scope={turn.afterStop ? 'agent.scope.stopped' : 'agent.scope.mailbox'} />
                            <p className="text-lg text-text-soft text-pretty">{turn.text}</p>
                        </div>
                    ) : (
                        <AgentAnswer key={turn.sequence} answer={turn} settledThrough={settledThrough} />
                    ),
                )}

                {reading ? (
                    <p role="status" className="text-md text-muted">
                        {translate('agent.reading')}
                    </p>
                ) : null}

                {failure === null ? null : (
                    <p role="alert" className="max-w-agent-answer text-md text-warning-text text-pretty">
                        {translate(readFailures[failure])}
                    </p>
                )}

                {status === undefined ? null : (
                    <p className="flex animate-arrival items-center gap-2.25 self-start">
                        <Icon name="progress_activity" className="size-4.25 animate-spin text-accent-deep" />
                        <span role="status" aria-live="polite" className="min-w-0 text-md text-muted">
                            {status ?? translate('agent.statusFallback')}
                        </span>
                    </p>
                )}

                {next === null ? null : (
                    <ProposedNext followUps={next.questions} arriving={next.sequence > settledThrough} onAsk={onAsk} />
                )}
            </div>
        </div>
    );
}
