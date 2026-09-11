// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { useReadMarking } from '../readMarking/useReadMarking';
import { ActQuestions } from './ActQuestions';
import { actsDrawn, actsOnAStrip, flagActFor, readActFor, refusalSaid, standsInTheWay } from './drawnActs';
import { useMailboxActs, type ActedMessage, type MailboxAct } from './useMailboxActs';

// The five things a person does to a mailbox, drawn once for the two strips that offer them: the toolbar, over the
// message that is open, and the selection bar, over everything picked out. One component rather than two rows of
// controls that resemble each other, because the acts are the same acts — a second arrangement of them is how the
// toolbar and the bar come to file mail differently. A row's own menu is the third surface onto them and draws its
// items rather than these controls, but it reads the same table and asks the same two questions.
//
// **A control that cannot act says so before it is pressed.** An account with no archive folder, a selection spanning
// two accounts, a credential without the grant — each is a sentence on the control rather than a refusal that arrives
// after somebody pressed it and watched nothing happen. That is a strip's answer rather than the client's: a menu
// leaves such an item out instead, because a column of sentences nobody can act on is not a menu.

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
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const marking = useReadMarking();
    const deleting = useRef<HTMLDialogElement>(null);
    const filing = useRef<HTMLDialogElement>(null);

    function act(asked: MailboxAct): void {
        acts.perform(asked, messages);
        onActed?.();
    }

    return (
        <>
            {actsOnAStrip.map((slot) => {
                // Two slots go both ways, and which way is the messages' own state rather than the strip's:
                // everything under the read control drawn read is offered the act that marks them unread, everything
                // under the flag control drawn flagged is offered the act that takes the flag off, and anything else
                // is offered the act each slot is named for. Every other slot is the act it names.
                const asked =
                    slot === 'markUnread'
                        ? readActFor(acts, marking, messages)
                        : slot === 'flag'
                          ? flagActFor(acts, messages)
                          : slot;
                const { icon, label } = actsDrawn[asked];

                // Either the deployment's refusal or the fact that this act is already on its way for every message
                // the control is about — the deployment holding it while the account's pass has not carried it out.
                // Both end in a control that says why instead of offering a second submission of the same act, which
                // would answer for each message that it is already there.
                const inTheWay = standsInTheWay(acts, asked, messages);

                return inTheWay === null ? (
                    <Control
                        key={slot}
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
                        key={slot}
                        label={translate(label)}
                        icon={icon}
                        shape={shape}
                        why={translate(inTheWay === 'underway' ? 'act.underway' : refusalSaid[inTheWay], {
                            control: translate(label),
                        })}
                    />
                );
            })}

            <ActQuestions messages={messages} deleting={deleting} filing={filing} onActed={onActed} />
        </>
    );
}
