// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock, SuggestedActionImpact } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { useLocalization } from '../../localization/useLocalization';
import { UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { ProposalCard, type ProposalControl } from '../ProposalCard';
import { useProposalAnswering } from '../proposalAnswering';
import { Citation } from './Citation';
import { actionImpacts, suggestedActions, supportVerdicts } from './blockWording';

// A next step somebody may take, and the block where the autonomy boundary this product draws becomes visible. It
// offers rather than reports, and three things follow.
//
// **Nothing here performs anything on its own.** The block draws the step, why it is suggested, and what taking it would
// change. Read-only, as Discover draws it, that is all; in a conversation the same card offers the step and *Decline*,
// and taking it is a request to the service that decides it rather than an act this client performs.
//
// **What it would change is stated before anybody agrees**, as the impact's own sentence rather than the member's name:
// opening a thread costs nothing, filing a message is reversible by whoever filed it, and a message that has left the
// deployment cannot be recalled by anything here.
//
// **Anything that leaves or changes a mailbox is drawn as needing confirmation, whatever the block claims.** The
// contract lets a producer state that flag itself, and a step that mutates or sends is confirmed regardless — so the
// screen reads the two together rather than trusting one of them. This is the only place that rule is applied to a
// reversible step: `parseSuggestedAction` refuses an unconfirmed *sending* step, which is what the service refuses too,
// and reads everything else through — so a member added to the impact set later cannot arrive as an unconfirmed step by
// default, and a run is not lost over a block this card already draws correctly.
//
// The sentences are `confirmation/ProposedAction.tsx`'s, because this says the same four things that component says and
// a second wording of *why*, *what would change*, and *nothing has happened yet* is how one product comes to describe
// its own autonomy boundary two ways. That component itself is not what is drawn here: it performs, offering a control
// that agrees and the confirmation behind it, while this one only asks the service to take a step it already proposed.

export function SuggestedAction({ block }: { readonly block: AnswerBlock }) {
    const { translate } = useLocalization();
    const answering = useProposalAnswering();

    if (block.type !== 'suggestedAction') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { evidence, suggestion } = block;
    const verdict = supportVerdicts[evidence.support];
    const step = suggestedActions[suggestion.action];
    const confirmationRequired = mustBeConfirmed(suggestion.impact, suggestion.requiresConfirmation);
    const title = translate(step.label);
    const waiting = answering === null || answering.phase === 'pending';

    const controls: readonly ProposalControl[] =
        answering === null
            ? []
            : [
                  {
                      said: translate('suggestedAction.take'),
                      name: translate('suggestedAction.takeName', { title }),
                      primary: true,
                      run: () => {
                          answering.answer('accepted');
                      },
                  },
                  {
                      said: translate('proposal.decline'),
                      name: translate('suggestedAction.declineName', { title }),
                      run: () => {
                          answering.answer('declined');
                      },
                  },
              ];

    return (
        <ProposalCard
            citations={evidence.citations}
            confirmationRequired={confirmationRequired}
            controls={controls}
            kind="suggestedAction"
            label={translate('suggestedAction.label')}
            title={title}
        >
            <div className="flex flex-wrap items-start gap-3">
                <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-accent-soft text-accent-deep">
                    <Icon className="size-4.5" name={step.icon} />
                </span>

                <div className="flex min-w-0 flex-col gap-1.25">
                    <p className="text-sm font-semibold">{title}</p>

                    <p className="text-sm text-text-soft text-pretty">
                        {translate('proposal.reason', { reason: suggestion.reason })}
                    </p>

                    <p className="text-xs text-muted text-pretty">
                        {translate('proposal.impact', { impact: translate(actionImpacts[suggestion.impact]) })}
                    </p>

                    {confirmationRequired && waiting ? (
                        <p className="text-xs text-muted text-pretty">{translate('proposal.confirmed')}</p>
                    ) : null}
                </div>
            </div>

            <p className="flex flex-wrap items-center gap-2">
                <span className={`flex items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                    <Icon className="size-3.25" name={verdict.icon} />
                    {translate(verdict.label)}
                </span>

                {evidence.citations.map((source, at) => (
                    <Citation key={source} position={at + 1} source={source} />
                ))}
            </p>

            {/* Said where the suggestion is rather than once on the screen: what somebody needs to know is that this
                card is a proposal, and a reader meets one card at a time. Once it is answered the phase says what
                happened instead, and this would contradict it. */}
            {waiting ? <p className="text-xs text-faint text-pretty">{translate('proposal.offered')}</p> : null}
        </ProposalCard>
    );
}

/** Whether a step is confirmed before it is taken, which is what the run said and what its impact says. */
function mustBeConfirmed(impact: SuggestedActionImpact, requiresConfirmation: boolean): boolean {
    return requiresConfirmation || impact !== 'ReadsOnly';
}
