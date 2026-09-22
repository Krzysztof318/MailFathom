// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

/**
 * What the agent suggests asking next, under the answer that suggested it. Each is a question and nothing else: pressing
 * one asks it in this conversation exactly as typing it would, and the row stands aside while that question is being
 * sent, so one press asks once.
 *
 * @param followUps What the last answer suggested, which the service wrote and the client never makes up.
 * @param onAsk Asks one of them, answering once the question has been sent or refused.
 */
export function ProposedNext({
    followUps,
    onAsk,
}: {
    readonly followUps: readonly string[];
    readonly onAsk: (question: string) => Promise<unknown>;
}) {
    const { translate } = useLocalization();
    const label = useId();
    const [asking, setAsking] = useState(false);

    async function ask(question: string): Promise<void> {
        setAsking(true);
        await onAsk(question);
        setAsking(false);
    }

    if (followUps.length === 0) {
        return null;
    }

    return (
        <div className="flex animate-arrival flex-col gap-2 self-start pt-0.5">
            <p id={label} className="text-2xs tracking-wider text-muted uppercase">
                {translate('agent.proposedNext')}
            </p>
            <ul aria-labelledby={label} className="flex flex-wrap gap-2">
                {followUps.map((followUp) => (
                    <li key={followUp}>
                        <button
                            type="button"
                            disabled={asking}
                            aria-label={translate('agent.askFollowUp', { question: followUp })}
                            className="inline-flex items-center gap-1.75 rounded-lg border border-line-strong bg-panel py-1.75 ps-2.5 pe-3 text-start text-base leading-tight text-text-soft transition hover:border-accent hover:text-accent-deep disabled:opacity-60"
                            onClick={() => {
                                void ask(followUp);
                            }}
                        >
                            <Icon name="arrow_forward" className="size-3.75 text-accent-deep" />
                            {followUp}
                        </button>
                    </li>
                ))}
            </ul>
        </div>
    );
}
