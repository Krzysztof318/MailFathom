// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { toastLifetime } from '../toasts/useToasts';
import type { ActRefusal } from './mailboxDestinations';
import { MoveChoice } from './MoveChoice';
import { useMailboxActs, type ActedMessage, type MailboxAct, type MailboxActs } from './useMailboxActs';

// The five things a person does to a mailbox, drawn once for the two strips that offer them: the toolbar, over the
// message that is open, and the selection bar, over everything picked out. One component rather than two rows of
// controls that resemble each other, because the acts are the same acts — a second arrangement of them is how the
// toolbar and the bar come to file mail differently.
//
// **Only what cannot be taken back is asked about.** That is the design project's rule rather than a preference:
// archiving, flagging, marking unread and filing happen on the press and report in a toast that offers the way back,
// and deleting is the one that stands a question in front of it, because mail in the trash is on a clock the client
// does not own. Asking about every act would teach a reader to agree without reading, which is worse than not asking.
//
// **A control that cannot act says so before it is pressed.** An account with no archive folder, a selection spanning
// two accounts, a credential without the grant — each is a sentence on the control rather than a refusal that arrives
// after somebody pressed it and watched nothing happen.

/** What each act is called and what it is drawn as, which is the design project's own symbol for it. */
const actsDrawn: readonly { readonly act: MailboxAct; readonly icon: IconName; readonly label: MessageKey }[] = [
    { act: 'archive', icon: 'archive', label: 'mail.archive' },
    { act: 'delete', icon: 'delete', label: 'mail.delete' },
    { act: 'flag', icon: 'flag', label: 'mail.flag' },
    { act: 'markUnread', icon: 'mark_email_unread', label: 'mail.markUnread' },
    { act: 'move', icon: 'drive_file_move', label: 'mail.move' },
];

/** Why a control cannot act, exhaustive by its own type so a reason added later has to be given words. */
const refusalSaid: Readonly<Record<ActRefusal, MessageKey>> = {
    notOffered: 'act.notOffered',
    nothingToActOn: 'act.nothingToActOn',
    noArchiveFolder: 'act.noArchiveFolder',
    noTrashFolder: 'act.noTrashFolder',
    severalAccounts: 'act.severalAccounts',
    noOtherFolder: 'act.noOtherFolder',
    foldersUnknown: 'act.foldersUnknown',
};

// How many messages the question about deleting counts, in the forms a language has for the noun.
const deleteQuestions: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.deleteQuestion.other',
    one: 'act.deleteQuestion.one',
    two: 'act.deleteQuestion.other',
    few: 'act.deleteQuestion.few',
    many: 'act.deleteQuestion.many',
    other: 'act.deleteQuestion.other',
};

/** Whether this act is already being carried out for every message the control is about. */
function underway(acts: MailboxActs, act: MailboxAct, messages: readonly ActedMessage[]): boolean {
    return messages.length > 0 && messages.every((message) => acts.asked.get(message.storedEmailId) === act);
}

export function MailboxActControls({
    messages,
    shape,
    onActed,
}: {
    /** The messages every one of these acts is about, in the order the list draws them. */
    readonly messages: readonly ActedMessage[];

    /** How the strip these stand in draws a control. */
    readonly shape: ControlShape;

    /** What the strip does once an act has been asked for, which is where a selection is let go. */
    readonly onActed?: () => void;
}) {
    const { locale, translate } = useLocalization();
    const acts = useMailboxActs();
    const deleting = useRef<HTMLDialogElement>(null);
    const filing = useRef<HTMLDialogElement>(null);

    function act(asked: MailboxAct): void {
        acts.perform(asked, messages);
        onActed?.();
    }

    return (
        <>
            {actsDrawn.map(({ act: asked, icon, label }) => {
                const refusal = acts.refusalOf(asked, messages);

                // An act already asked for on every message this control is about is one the deployment holds and the
                // account's pass has not carried out yet, so the control says so rather than offering a second
                // submission of the same act — which would answer for each message that it is already there.
                if (refusal === null && underway(acts, asked, messages)) {
                    return (
                        <PlannedControl
                            key={asked}
                            label={translate(label)}
                            icon={icon}
                            shape={shape}
                            why={translate('act.underway', { control: translate(label) })}
                        />
                    );
                }

                return refusal === null ? (
                    <Control
                        key={asked}
                        label={translate(label)}
                        icon={icon}
                        shape={shape}
                        onPress={() => {
                            if (asked === 'delete') {
                                deleting.current?.showModal();
                            } else if (asked === 'move') {
                                filing.current?.showModal();
                            } else {
                                act(asked);
                            }
                        }}
                    />
                ) : (
                    <PlannedControl
                        key={asked}
                        label={translate(label)}
                        icon={icon}
                        shape={shape}
                        why={translate(refusalSaid[refusal], { control: translate(label) })}
                    />
                );
            })}

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
                            act('delete');
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
