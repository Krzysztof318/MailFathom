// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { ActQuestions } from './ActQuestions';
import { actsDrawn, actsOnAStrip, refusalSaid, underway } from './drawnActs';
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
    const deleting = useRef<HTMLDialogElement>(null);
    const filing = useRef<HTMLDialogElement>(null);

    function act(asked: MailboxAct): void {
        acts.perform(asked, messages);
        onActed?.();
    }

    return (
        <>
            {actsOnAStrip.map((asked) => {
                const { icon, label } = actsDrawn[asked];
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

            <ActQuestions messages={messages} deleting={deleting} filing={filing} onActed={onActed} />
        </>
    );
}
