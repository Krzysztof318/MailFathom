// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useContext } from 'react';
import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { ComposingContext } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { refusalSaid, standsInTheWay } from '../mailboxActs/drawnActs';
import { useMailboxActs, type ActedMessage } from '../mailboxActs/useMailboxActs';

// What stands at the end of the head of a message or a conversation, beside its subject: handing the thread to the
// agent, and the three things the design project offers to do with it from there. One component because two heads
// draw it — a message opened from the list and the conversation it belongs to — and the acts have to be the same acts
// from both.
//
// The design draws the three as words alone wherever the head has a column to itself, and as symbols alone where the
// column is the whole screen and the head is one compact bar over the message. Asking the agent is drawn on the accent
// as a pill, and only where the head is not that compact bar: on a phone the design moves it into the row of the
// thread's own state, which is the AI enrichment the client does not draw yet — so that one is still a control the
// product will have, inert until it does.
//
// The other three act on the message the head is about, and each goes the way every other surface already goes: an
// answer is `useComposing`'s, exactly as the toolbar's three answers and a row's own menu are, and the flag is a
// mailbox act, exactly as the toolbar's five are. Neither is a second implementation — a head that composed its own
// reply or wrote its own flag is how two surfaces come to answer a message differently.
//
// **The flag is the one control in this client that goes both ways**, because this is the one surface that can: a head
// is about exactly one message and already holds whether it is flagged, while a strip stands over whatever is picked
// out and cannot say which direction a single control would take. `mailboxActs/drawnActs.ts` carries that difference.
//
// The head's own words for forwarding and flagging differ from the toolbar's — *Przekaż* and *Oflaguj* against
// *Prześlij dalej* and *Flaga* — which is why they are keys of their own rather than the toolbar's reused.

/** The message a head's acts are about: where it is, so a mailbox act can name it, and which way its flag goes. */
export interface HeadMessage extends ActedMessage {
    readonly flagged: boolean;
}

export function HeadActs({
    compact,
    message,
}: {
    readonly compact: boolean;

    /**
     * The message these acts are about, or `null` where the head is about something the client cannot act on yet.
     *
     * A conversation is the second of those. The design's acts in a conversation's head are about the conversation —
     * flagging one flags every message in it — and this client's acts name messages, so a head that handed them its
     * newest message would flag one message of a thread while the head said the thread was flagged. That is a screen
     * of its own rather than a prop to fill in, and until it exists the conversation draws the three as what they are.
     */
    readonly message: HeadMessage | null;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();

    // Read rather than required, for the reason `mailSpace/NothingOpen.tsx` reads it that way: a head is drawn in
    // surfaces a test mounts on their own, and writing is offered by a deployment rather than by this component.
    const composing = useContext(ComposingContext);

    const actShape: ControlShape = compact ? 'symbol' : 'named';

    // What each of the three does, or `null` where it cannot be done: the answers need a deployment that lets this
    // credential write a draft, and both need a message to be about. Written as the act rather than as a condition
    // read again inside the markup, which is what keeps the message's own identity out of every branch below.
    const answer =
        composing?.offered === true && message !== null
            ? (answers: MailDraftAnswer) => () => {
                  composing.compose({ kind: 'answer', answers, storedEmailId: message.storedEmailId });
              }
            : null;

    // Which way the flag goes, which is the message's own state rather than a control's: a flagged message is offered
    // the act that takes the flag off, and every other message the act that puts one on.
    const flagging = message?.flagged === true ? 'unflag' : 'flag';
    const flagLabel = translate(flagging === 'unflag' ? 'message.unflag' : 'message.flag');

    // Either the act or the sentence saying why it cannot be taken, decided here rather than inside the markup: a
    // control that cannot act says so before it is pressed, which is the rule `mailboxActs/MailboxActControls.tsx`
    // states for the strips, and the two shapes are exclusive.
    const flag = whatFlaggingComesTo();

    function whatFlaggingComesTo(): { readonly press: () => void } | { readonly why: string } {
        if (message === null) {
            return { why: translate(refusalSaid.nothingToActOn, { control: flagLabel }) };
        }

        const inTheWay = standsInTheWay(acts, flagging, [message]);

        if (inTheWay === null) {
            return {
                press: () => {
                    acts.perform(flagging, [message]);
                },
            };
        }

        return {
            why: translate(inTheWay === 'underway' ? 'act.underway' : refusalSaid[inTheWay], { control: flagLabel }),
        };
    }

    return (
        <div className="ms-auto flex shrink-0 items-center gap-2">
            {compact ? null : (
                <PlannedControl
                    label={translate('message.ask')}
                    icon="auto_awesome"
                    shape="accentPill"
                    why={translate('control.notBuiltYet', { control: translate('message.askTitle') })}
                />
            )}

            <div className="flex items-center gap-0.75">
                {answer === null ? (
                    <PlannedControl label={translate('mail.reply')} icon="reply" shape={actShape} />
                ) : (
                    <Control
                        label={translate('mail.reply')}
                        icon="reply"
                        shape={actShape}
                        onPress={answer('senderOnly')}
                    />
                )}

                {answer === null ? (
                    <PlannedControl label={translate('message.forward')} icon="forward" shape={actShape} />
                ) : (
                    <Control
                        label={translate('message.forward')}
                        icon="forward"
                        shape={actShape}
                        onPress={answer('forward')}
                    />
                )}

                {'press' in flag ? (
                    <Control label={flagLabel} icon="flag" shape={actShape} onPress={flag.press} />
                ) : (
                    <PlannedControl label={flagLabel} icon="flag" shape={actShape} why={flag.why} />
                )}
            </div>
        </div>
    );
}
