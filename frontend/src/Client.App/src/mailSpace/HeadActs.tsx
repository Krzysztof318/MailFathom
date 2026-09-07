// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';

// What stands at the end of the head of a message or a conversation, beside its subject: handing the thread to the
// agent, and the three things the design project offers to do with it from there. One component because two heads
// draw it — a message opened from the list and the conversation it belongs to — and the acts have to be the same acts
// from both.
//
// The design draws the three as words alone wherever the head has a column to itself, and as symbols alone where the
// column is the whole screen and the head is one compact bar over the message. Asking the agent is drawn on the accent
// as a pill, and only where the head is not that compact bar: on a phone the design moves it into the row of the
// thread's own state, which is the AI enrichment the client does not draw yet.
//
// None of the four exists in the client yet, so each is drawn as what it is: a control the product will have, inert
// until it does. The head's own words for forwarding and flagging differ from the toolbar's — *Przekaż* and *Oflaguj*
// against *Prześlij dalej* and *Flaga* — which is why they are keys of their own rather than the toolbar's reused.

export function HeadActs({ compact }: { readonly compact: boolean }) {
    const { translate } = useLocalization();
    const actShape: ControlShape = compact ? 'symbol' : 'named';

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
                <PlannedControl label={translate('mail.reply')} icon="reply" shape={actShape} />
                <PlannedControl label={translate('message.forward')} icon="forward" shape={actShape} />
                <PlannedControl label={translate('message.flag')} icon="flag" shape={actShape} />
            </div>
        </div>
    );
}
