// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { useLocalization } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { citationCounts, confidenceBands, supportVerdicts } from './blockWording';

// The synthesized answer: prose, what it is worth beyond the sources it names, and what those sources actually do for
// it. It is the block the whole contract is shaped around, because it is the one most able to say something the
// correspondence does not — so everything drawn around the words is there to stop the words being read as more
// settled than they are.
//
// **The verdict is drawn where the answer is, not under it.** A fact nothing backs, a fact read from a copy known to
// be behind, and two sources that disagree are each a state of this answer rather than a note about it, so each is a
// chip beside the confidence and a sentence beneath the prose — and a conflict draws both of its sides, each with the
// sources that say it, because an answer that resolved two figures into one has answered a question nobody asked.
//
// **The citations sit at the end of the prose rather than beside the fact each supports**, and that is a limit of the
// contract rather than a reading of the design. The plan carries one text and a flat list of the sources the whole
// block rests on, with nothing saying which sentence rests on which — so placing a chip mid-paragraph would be this
// client inventing a provenance the run never stated, which is worse than a chip a reader has to look to the end of
// the paragraph for. Closing that needs the block to carry its text in segments; until it does, what is drawn is what
// the run said.

export function SynthesizedAnswer({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    if (block.type !== 'answer') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { answer, evidence } = block;
    const band = confidenceBands[answer.confidence];
    const verdict = supportVerdicts[evidence.support];

    return (
        <AnswerBlockCard
            label={translate('answer.label')}
            meta={translate(citationCounts[new Intl.PluralRules(locale).select(evidence.citations.length)], {
                count: new Intl.NumberFormat(locale).format(evidence.citations.length),
            })}
            state="ready"
        >
            <div className="flex flex-wrap items-center gap-2">
                <span className={`rounded-full px-2.25 py-0.5 text-2xs ${band.tint}`}>{translate(band.label)}</span>

                <span className={`flex items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                    <Icon className="size-3.25" name={verdict.icon} />
                    {translate(verdict.label)}
                </span>
            </div>

            <p className="text-base leading-relaxed text-pretty">
                {answer.text}

                {evidence.citations.map((source, at) => (
                    <span className="ms-1 inline-flex" key={source}>
                        <Citation position={at + 1} source={source} />
                    </span>
                ))}
            </p>

            <p className="text-sm text-muted text-pretty">{translate(verdict.note)}</p>

            {evidence.conflictingClaims.length === 0 ? null : (
                <ul aria-label={translate('answer.sides')} className="flex flex-col gap-2">
                    {evidence.conflictingClaims.map((claim) => (
                        <li
                            className="flex flex-col gap-1 rounded-e-lg border-s-2 border-s-warning bg-sunken px-3 py-2"
                            key={claim.statement}
                        >
                            <p className="text-sm text-pretty">{claim.statement}</p>

                            <p className="flex flex-wrap items-center gap-1">
                                {claim.sources.map((source) => (
                                    <Citation
                                        key={source}
                                        position={evidence.citations.indexOf(source) + 1}
                                        source={source}
                                    />
                                ))}
                            </p>
                        </li>
                    ))}
                </ul>
            )}
        </AnswerBlockCard>
    );
}
