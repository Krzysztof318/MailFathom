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
// and deleting is the one that stands a question in front of it, because mail in the trash is on a clock the client
// does not own. Filing is here beside it because picking a folder *is* the act rather than a confirmation of one —
// there is nowhere else for a choice to be made.
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

    return (
        <>
            <Confirmation
                asked={deleting}
                mark="delete"
                question={translate(deleteQuestions[new Intl.PluralRules(locale).select(messages.length)], {
                    count: new Intl.NumberFormat(locale).format(messages.length),
                })}
                consequence={<p className="text-base text-muted text-pretty">{translate('act.deleteConsequence')}</p>}
                reversal={{ kind: 'undoable', forSeconds: toastLifetime / 1000 }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('act.deleteConfirm'),
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
                destinations={acts.destinationsOf(messages)}
                onChosen={(destination) => {
                    acts.perform('move', messages, destination);
                    onActed?.();
                }}
            />
        </>
    );
}
