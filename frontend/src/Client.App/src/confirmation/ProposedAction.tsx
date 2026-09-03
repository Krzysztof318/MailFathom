// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { Confirmation, type Reversal } from './Confirmation';

// Something the model offered to do, drawn so that agreeing is informed. It is the top half of MailFathom's autonomy
// scale rendered: analysis and a local artifact happen, and everything that leaves the deployment is offered here
// instead — which is what makes the scale visible rather than a paragraph in a document.
//
// **It performs nothing.** There is no effect in this file, no timer, and no path from being drawn to the act: what
// happens is what somebody pressed, and where the act is one that reaches a mailbox it is pressed twice, the second
// time in the confirmation below. A suggestion that acted on being shown would be the product's one rule broken, so
// the absence is the component rather than a property of it.
//
// **Four things are shown and none of them is optional**, because a proposal missing one of them is a proposal
// somebody agrees to blindly: what it would do, why it was offered, what would change, and whether anything stands
// between agreeing and it happening. The last is not a detail of the mechanism — it is the difference between a press
// that files four hundred messages and a press that opens a question about filing them.

/** Something the model offered to do, as values rather than as anything on the screen. */
export interface Proposal {
    /** What it would do, said as the act it is rather than as a suggestion about one. */
    readonly action: string;

    /** Why it was offered, in the terms of what was read rather than as a score. */
    readonly reason: string;

    /** What would change if it happened, in the terms of the thing that would change. */
    readonly impact: string;

    /** What could be done about it afterwards, which is what decides how heavy the question in front of it is. */
    readonly reversal: Reversal;

    /**
     * Whether a confirmation stands between agreeing and the act.
     *
     * Every act that reaches a mailbox, a recipient, or another person sets it. It is false only where the act stays
     * inside this client, and it is stated rather than derived so that a proposal says which kind it is even where the
     * screen showing it has no idea what the act reaches.
     */
    readonly confirmationRequired: boolean;
}

export function ProposedAction({
    proposal,
    agreeing,
    onAgreed,
    onDismissed,
}: {
    readonly proposal: Proposal;

    /** What the control that agrees is called, in the words of the act — never `Accept` and never `OK`. */
    readonly agreeing: string;

    readonly onAgreed: () => void;
    readonly onDismissed: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <section className="flex flex-col gap-2 rounded-2xl border border-accent-line bg-accent-soft px-3.5 py-3">
            <p className="flex items-center gap-2 text-sm font-semibold text-accent-deep">
                <Icon name="auto_awesome" className="size-4 shrink-0" />
                {translate('proposal.offered')}
            </p>

            <p className="text-base font-semibold text-text text-pretty">{proposal.action}</p>

            <p className="text-base text-text-soft text-pretty">
                {translate('proposal.reason', { reason: proposal.reason })}
            </p>

            <p className="text-base text-text-soft text-pretty">
                {translate('proposal.impact', { impact: proposal.impact })}
            </p>

            <p className="text-sm text-muted text-pretty">
                {translate(proposal.confirmationRequired ? 'proposal.confirmed' : 'proposal.unconfirmed')}
            </p>

            <div className="flex flex-wrap justify-end gap-2">
                <button
                    type="button"
                    className="rounded-lg border border-line bg-panel px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                    onClick={onDismissed}
                >
                    {translate('proposal.notNow')}
                </button>

                <button
                    type="button"
                    className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                    onClick={() => {
                        if (proposal.confirmationRequired) {
                            asked.current?.showModal();
                        } else {
                            onAgreed();
                        }
                    }}
                >
                    {agreeing}
                </button>
            </div>

            <Confirmation
                asked={asked}
                mark="auto_awesome"
                question={proposal.action}
                consequence={<p className="text-base text-muted text-pretty">{proposal.impact}</p>}
                reversal={proposal.reversal}
                ways={[
                    { said: translate('proposal.notNow'), manner: 'back' },
                    { said: agreeing, manner: 'act', run: onAgreed },
                ]}
            />
        </section>
    );
}
