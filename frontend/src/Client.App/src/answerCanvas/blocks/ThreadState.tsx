// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AnswerBlock,
    BlockParticipant,
    ConversationStanding,
    ThreadCommitment,
    ThreadStatement,
} from '@mailfathom/client-backend';
import { SenderAvatar } from '../../controls/SenderAvatar';
import type { MessageKey } from '../../localization/en';
import { wordInstant } from '../../localization/instants';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { participantCounts, supportVerdicts } from './blockWording';
import { citationOrder } from './citationOrder';

// Where a conversation stands, as a block of an answer: who is taking part, what they settled, what they left open, and
// who owes what. Three lists rather than one, because somebody scanning for what they still owe should not have to find
// it among what was agreed.
//
// **It is a second rendering of one answer and never a second derivation.** `thread/ThreadState.tsx` draws the mail
// space's own reading beside a conversation, from a different contract and with its own compositions; what this draws is
// the block a Discover run composed. Neither is the source of the other, and a screen that recomputed one from the other
// would be inventing a third.
//
// **The standing is drawn by a component of its own**, exported beside the renderer, because the mail space is expected
// to host exactly this block — the same statements with the same citations — without the card around it. The card is
// what makes something a block of an answer, so it stays here and the body travels.

export function ThreadState({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    if (block.type !== 'threadState') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { evidence, standing } = block;
    const verdict = supportVerdicts[evidence.support];
    const said = standing.agreements.length + standing.openQuestions.length + standing.commitments.length;

    return (
        <AnswerBlockCard
            label={translate('threadStanding.label')}
            meta={
                standing.participants.length === 0
                    ? undefined
                    : translate(participantCounts[new Intl.PluralRules(locale).select(standing.participants.length)], {
                          count: new Intl.NumberFormat(locale).format(standing.participants.length),
                      })
            }
            note={said === 0 ? translate('threadStanding.empty') : undefined}
            state={said === 0 ? 'empty' : 'ready'}
        >
            <span className={`flex w-fit items-center gap-1 rounded-full px-2.25 py-0.5 text-2xs ${verdict.tint}`}>
                {translate(verdict.label)}
            </span>

            <ConversationStandingBody citations={evidence.citations} standing={standing} />
        </AnswerBlockCard>
    );
}

/**
 * Where a conversation stands, without the card a block of an answer stands in.
 *
 * @param standing Who is taking part and the three things the conversation says about itself.
 * @param citations The sources the whole reading rests on, which the numbers on the chips are places in.
 */
export function ConversationStandingBody({
    standing,
    citations,
}: {
    readonly standing: ConversationStanding;
    readonly citations: readonly string[];
}) {
    const { translate } = useLocalization();

    const positionOf = citationOrder(
        citations,
        ...standing.agreements.map((statement) => statement.sources),
        ...standing.openQuestions.map((statement) => statement.sources),
        ...standing.commitments.map((commitment) => commitment.sources),
    );

    return (
        <>
            <Participants participants={standing.participants} />

            <div className="flex flex-col gap-3.5 desktop:grid desktop:grid-cols-3 desktop:items-start">
                <Statements
                    heading="threadStanding.agreed"
                    statements={standing.agreements}
                    positionOf={positionOf}
                    empty="threadStanding.noAgreements"
                />

                <Statements
                    heading="threadStanding.openQuestions"
                    statements={standing.openQuestions}
                    positionOf={positionOf}
                    empty="threadStanding.noOpenQuestions"
                />

                <section className="flex min-w-0 flex-col gap-1.5">
                    <h4 className="text-2xs tracking-wider text-muted uppercase">
                        {translate('threadStanding.commitments')}
                    </h4>

                    {standing.commitments.length === 0 ? (
                        <p className="text-sm text-faint">{translate('threadStanding.noCommitments')}</p>
                    ) : (
                        <ul className="flex flex-col gap-1.5">
                            {standing.commitments.map((commitment, at) => (
                                <li className="text-sm text-text-soft" key={`${commitment.text}-${String(at)}`}>
                                    <Commitment commitment={commitment} positionOf={positionOf} />
                                </li>
                            ))}
                        </ul>
                    )}
                </section>
            </div>
        </>
    );
}

/** Who is taking part, as the one overlapping run of circles the design draws rather than a list of names. */
function Participants({ participants }: { readonly participants: readonly BlockParticipant[] }) {
    const { translate } = useLocalization();

    if (participants.length === 0) {
        return null;
    }

    return (
        <ul aria-label={translate('threadStanding.participants')} className="flex flex-wrap items-center">
            {participants.map((participant, at) => (
                <li
                    // The circle says nothing to a screen reader, so the name is the item's own text and is kept out of
                    // sight: a run of initials read aloud as letters is not who was in the conversation.
                    className="flex items-center"
                    key={`${participant.address ?? participant.displayName}-${String(at)}`}
                >
                    <SenderAvatar
                        address={participant.address}
                        displayName={participant.displayName}
                        place="answerStack"
                    />

                    <span className="sr-only">{participant.displayName}</span>
                </li>
            ))}
        </ul>
    );
}

/** One of the two lists of statements, each of which the conversation may legitimately have none of. */
function Statements({
    heading,
    statements,
    positionOf,
    empty,
}: {
    readonly heading: MessageKey;
    readonly statements: readonly ThreadStatement[];
    readonly positionOf: (source: string) => number;
    readonly empty: MessageKey;
}) {
    const { translate } = useLocalization();

    return (
        <section className="flex min-w-0 flex-col gap-1.5">
            <h4 className="text-2xs tracking-wider text-muted uppercase">{translate(heading)}</h4>

            {statements.length === 0 ? (
                <p className="text-sm text-faint">{translate(empty)}</p>
            ) : (
                <ul className="flex flex-col gap-1.5">
                    {statements.map((statement, at) => (
                        <li className="text-sm text-text-soft text-pretty" key={`${statement.text}-${String(at)}`}>
                            {statement.text}

                            {statement.sources.map((source) => (
                                <span className="ms-1 inline-flex" key={source}>
                                    <Citation position={positionOf(source)} source={source} />
                                </span>
                            ))}
                        </li>
                    ))}
                </ul>
            )}
        </section>
    );
}

/**
 * One commitment: what was undertaken, who undertook it, and when it falls due.
 *
 * Who and when are each optional in the contract and each optional for a reason a client must not paper over — a
 * commitment is often made without naming who keeps it, and naming somebody for it would be asserting what the mail did
 * not say. So the sentence is one catalogue entry per combination rather than fragments joined here.
 */
function Commitment({
    commitment,
    positionOf,
}: {
    readonly commitment: ThreadCommitment;
    readonly positionOf: (source: string) => number;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const due = wordInstant(commitment.dueAt, locale, 'stamp', timeZone);
    const owedBy = commitment.owedBy?.displayName ?? null;

    return (
        <>
            {translate(...owing(commitment.text, owedBy, due))}

            {commitment.sources.map((source) => (
                <span className="ms-1 inline-flex" key={source}>
                    <Citation position={positionOf(source)} source={source} />
                </span>
            ))}
        </>
    );
}

// Which of the four sentences a commitment reads as, decided from what the correspondence actually gave rather than
// assembled from pieces: where a value falls in the sentence is the language's answer and not this screen's.
function owing(
    text: string,
    owedBy: string | null,
    due: string | null,
): [MessageKey, Readonly<Record<string, string>>] {
    if (owedBy !== null && due !== null) {
        return ['threadStanding.commitmentOwedByDue', { what: text, name: owedBy, when: due }];
    }

    if (owedBy !== null) {
        return ['threadStanding.commitmentOwedBy', { what: text, name: owedBy }];
    }

    return due === null
        ? ['threadStanding.commitment', { what: text }]
        : ['threadStanding.commitmentDue', { what: text, when: due }];
}
