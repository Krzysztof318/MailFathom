// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useMemo } from 'react';
import type { AgentMessageScope } from '@mailfathom/client-backend';
import { DrawnBlock } from '../answerCanvas/AnswerCanvas';
import { answerBlockRenderers } from '../answerCanvas/answerBlocks';
import { AnswerSourcesContext } from '../answerCanvas/answerSources';
import { Containment } from '../containment/Containment';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { Answer } from './conversationThread';

const scopes: Readonly<Record<AgentMessageScope['kind'], MessageKey>> = {
    mailbox: 'agent.scope.mailbox',
    thread: 'agent.scope.thread',
    calendarEvent: 'agent.scope.calendarEvent',
    discoveryRun: 'agent.scope.discoveryRun',
};

/**
 * The line above everything the agent wrote, which says it was the agent and what it was working across.
 */
export function AnswerScope({ scope }: { readonly scope: MessageKey }) {
    const { translate } = useLocalization();

    return (
        <p className="flex items-center gap-1.75 text-2xs text-muted">
            <span className="tracking-wider text-accent-deep">{translate('ai.badge')}</span>
            <span>{translate('agent.scopeLine', { scope: translate(scope) })}</span>
        </p>
    );
}

/**
 * One answer, as far as it has been composed: the blocks it has published, each drawn by the renderer the catalogue
 * registers for its type, and a working line where it has published nothing yet.
 *
 * It carries no card of its own. The thread is flat and the blocks are the only cards in it, so the separation between
 * one answer and the next is carried by them.
 *
 * @param settledThrough What was already there when the conversation was opened, which draws without animating in.
 */
export function AgentAnswer({ answer, settledThrough }: { readonly answer: Answer; readonly settledThrough: number }) {
    const { translate } = useLocalization();

    // Memoized against the sources alone, because every renderer reads the context and a fresh object each render would
    // redraw the whole answer whenever anything above it rendered.
    const declared = useMemo(() => ({ sources: answer.sources, follow: null }), [answer.sources]);

    return (
        <article
            aria-label={translate('agent.answer')}
            className={`flex w-full max-w-agent-answer flex-col gap-2.75 ${
                answer.sequence > settledThrough ? 'animate-arrival' : ''
            }`}
        >
            <AnswerScope scope={answer.scope === null ? 'agent.scope.mailbox' : scopes[answer.scope.kind]} />

            {answer.blocks.length === 0 && answer.ending === null ? (
                <p className="flex items-center gap-2 text-md text-muted">
                    <Icon name="autorenew" className="size-4 text-accent-deep" />
                    {translate('agent.working')}
                </p>
            ) : null}

            {answer.blocks.length > 0 ? (
                <AnswerSourcesContext value={declared}>
                    <ol aria-label={translate('answerCanvas.blocks')} className="flex flex-col gap-2.75">
                        {answer.blocks.map((answered) => (
                            <li
                                key={answered.sequence}
                                className={answered.sequence > settledThrough ? 'animate-block-arrival' : undefined}
                            >
                                <Containment drawing={String(answered.sequence)} region="answer_block">
                                    <DrawnBlock block={answered} renderers={answerBlockRenderers} />
                                </Containment>
                            </li>
                        ))}
                    </ol>
                </AnswerSourcesContext>
            ) : null}

            {answer.ending === 'failed' ? (
                <p role="status" className="flex items-start gap-1.75 text-md text-warning-text">
                    <Icon name="error" className="mt-0.5 size-4 shrink-0" />
                    {translate('agent.failed')}
                </p>
            ) : null}
        </article>
    );
}
