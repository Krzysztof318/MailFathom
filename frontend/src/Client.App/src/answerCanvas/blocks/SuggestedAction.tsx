// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock, SuggestedActionImpact } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { useLocalization } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { actionImpacts, suggestedActions, supportVerdicts } from './blockWording';

// A next step somebody may take, and the block where the autonomy boundary this product draws becomes visible. It
// offers rather than reports, and three things follow.
//
// **Nothing here performs anything.** The block draws the step, why it is suggested, and what taking it would change;
// the control that takes it belongs to the surface that governs that step and carries its own permission. A suggestion
// rendered as a button that just does it is the one way this block can be badly wrong.
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
// that agrees and the confirmation behind it, and a block of a Discover answer has no act to offer — the conversation
// surface is where the same suggestion gains controls, which is what the design project draws as its other half.

export function SuggestedAction({ block }: { readonly block: AnswerBlock }) {
    const { translate } = useLocalization();

    if (block.type !== 'suggestedAction') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { evidence, suggestion } = block;
    const verdict = supportVerdicts[evidence.support];
    const step = suggestedActions[suggestion.action];
    const confirmationRequired = mustBeConfirmed(suggestion.impact, suggestion.requiresConfirmation);

    return (
        <AnswerBlockCard label={translate('suggestedAction.label')} state="ready">
            <div className="flex flex-wrap items-start gap-3">
                <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-accent-soft text-accent-deep">
                    <Icon className="size-4.5" name={step.icon} />
                </span>

                <div className="flex min-w-0 flex-col gap-1.25">
                    <p className="text-sm font-semibold">{translate(step.label)}</p>

                    <p className="text-sm text-text-soft text-pretty">
                        {translate('proposal.reason', { reason: suggestion.reason })}
                    </p>

                    <p className="text-xs text-muted text-pretty">
                        {translate('proposal.impact', { impact: translate(actionImpacts[suggestion.impact]) })}
                    </p>

                    {confirmationRequired ? (
                        <p className="text-xs text-muted text-pretty">{translate('proposal.confirmed')}</p>
                    ) : null}
                </div>

                {confirmationRequired ? (
                    <span className="flex shrink-0 items-center gap-1 rounded-full bg-warning-soft px-2.25 py-0.5 text-2xs whitespace-nowrap text-warning-text workspace:ms-auto">
                        <Icon className="size-3.25" name="gpp_maybe" />
                        {translate('suggestedAction.needsConfirmation')}
                    </span>
                ) : null}
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
                card is a proposal, and a reader meets one card at a time. */}
            <p className="text-xs text-faint text-pretty">{translate('proposal.offered')}</p>
        </AnswerBlockCard>
    );
}

/** Whether a step is confirmed before it is taken, which is what the run said and what its impact says. */
function mustBeConfirmed(impact: SuggestedActionImpact, requiresConfirmation: boolean): boolean {
    return requiresConfirmation || impact !== 'ReadsOnly';
}
