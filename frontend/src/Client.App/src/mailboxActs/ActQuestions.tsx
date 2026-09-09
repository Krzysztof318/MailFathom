// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { RefObject } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { toastLifetime } from '../toasts/useToasts';
import { MoveChoice } from './MoveChoice';
import { useMailboxActs, type ActedMessage } from './useMailboxActs';

// The two acts that stand behind a question, drawn once for every surface that offers them. A strip of controls asks
// them and so does a row's own menu, and the question has to read identically from both: *are you sure* wearing two
// different sets of words is how a reader learns to agree without reading.
//
// **Only what cannot be taken back is asked about.** That is the design project's rule rather than a preference:
// archiving, flagging, marking unread and filing happen on the press and report in a toast that offers the way back,
// and deleting is the one that stands a question in front of it. Filing is here beside it because picking a folder *is*
// the act rather than a confirmation of one — there is nowhere else for a choice to be made.
//
// **Deleting asks one of two questions, and which one is read off where the mail already is.** A message somewhere else
// is filed in the trash, which stands for a while and can be undone from the toast; a message already in the trash is
// destroyed on the mail server, and that question names what it costs and offers nothing afterwards.
//
// Whether either is open is the dialog element's own state, which is why the caller hands over the references: two
// ways to ask are still one question, and a second copy of *is it open* is how the two come to disagree.

// How many messages the question about deleting counts, in the forms a language has for the noun.
const deleteQuestions: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.deleteQuestion.other',
    one: 'act.deleteQuestion.one',
    two: 'act.deleteQuestion.other',
    few: 'act.deleteQuestion.few',
    many: 'act.deleteQuestion.many',
    other: 'act.deleteQuestion.other',
};

// The same question for a message already in the trash, where the act destroys it rather than filing it. A second set
// of forms rather than one sentence with a word swapped, because *permanently* falls in a different place in the two
// languages and a sentence assembled from pieces can be translated in only one word order.
const purgeQuestions: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.purgeQuestion.other',
    one: 'act.purgeQuestion.one',
    two: 'act.purgeQuestion.other',
    few: 'act.purgeQuestion.few',
    many: 'act.purgeQuestion.many',
    other: 'act.purgeQuestion.other',
};

// What that question says the act costs, counted for the same reason the question is: the design names the number
// again in the sentence, and a language that inflects the noun cannot take it from the heading.
const purgeConsequences: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.purgeConsequence.other',
    one: 'act.purgeConsequence.one',
    two: 'act.purgeConsequence.other',
    few: 'act.purgeConsequence.few',
    many: 'act.purgeConsequence.many',
    other: 'act.purgeConsequence.other',
};

export function ActQuestions({
    messages,
    deleting,
    filing,
    onActed,
}: {
    /** The messages both questions are about, in the order the list draws them. */
    readonly messages: readonly ActedMessage[];

    readonly deleting: RefObject<HTMLDialogElement | null>;
    readonly filing: RefObject<HTMLDialogElement | null>;

    /** What the surface does once an act has been asked for, which is where a selection is let go. */
    readonly onActed?: (() => void) | undefined;
}) {
    const { locale, translate } = useLocalization();
    const acts = useMailboxActs();

    // One control with one symbol, asking two different questions: what *delete* does is read off where the mail
    // already is, so a message in the trash is asked about as the destruction it would be rather than as the move it
    // cannot be. Asked here rather than answered by a second control, which is the design project's own arrangement.
    const destroys = acts.deletesPermanently(messages);
    const counted = { count: new Intl.NumberFormat(locale).format(messages.length) };
    const form = new Intl.PluralRules(locale).select(messages.length);

    return (
        <>
            <Confirmation
                asked={deleting}
                mark="delete"
                question={translate((destroys ? purgeQuestions : deleteQuestions)[form], counted)}
                consequence={
                    <p className="text-base text-muted text-pretty">
                        {destroys ? translate(purgeConsequences[form], counted) : translate('act.deleteConsequence')}
                    </p>
                }
                reversal={
                    destroys
                        ? { kind: 'permanent', said: translate('act.purgeReversal') }
                        : { kind: 'undoable', forSeconds: toastLifetime / 1000 }
                }
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate(destroys ? 'act.purgeConfirm' : 'act.deleteConfirm'),
                        manner: 'destroy',
                        run: () => {
                            acts.perform('delete', messages);
                            onActed?.();
                        },
                    },
                ]}
            />

            <MoveChoice
                asked={filing}
                groups={acts.destinationsOf(messages)}
                onChosen={(destination) => {
                    acts.perform('move', messages, destination);
                    onActed?.();
                }}
            />
        </>
    );
}
