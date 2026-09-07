// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MailboxActControls } from '../mailboxActs/MailboxActControls';
import { actedMessages, useListedMail } from '../messageList/useListedMail';
import { useWorkspace } from '../workspace/useWorkspace';
import type { StripFitting } from './useStripFit';

// The strip the design project draws across the top of the Mail space: composing, and the eight things a person does
// to a message. Four of them write a message and five change the mailbox it is in, and both halves act here — the
// mailbox half over the message that is open, which is what the design draws this strip as being about.
//
// What is open and whether writing is offered are both read from context rather than handed down. The toolbar is three
// components below the frame that knows either, and neither is this strip's to own: the mail space already reads the
// workspace, the composer is asked for from three unrelated places, which is what `useComposing` exists for, and where
// the open message belongs is the list's, which is what `useListedMail` exists for.
//
// **How wide a control is drawn is measured rather than decided at a width.** The design keeps every name in words for
// as long as the nine of them fit the strip; when they stop fitting, composing leaves the strip for the floating
// control over the list first, and only if the eight names still do not fit do they become symbols. That measurement
// is the mail space's, because the floating control is drawn over a column this strip is not in — so the space owns
// the fitting and hands this strip the half of it that is drawn here.

// Answering a message: which of the three answers each control writes, and the symbol and name the design gives it.
const answers: readonly { readonly answer: MailDraftAnswer; readonly icon: IconName; readonly label: MessageKey }[] = [
    { answer: 'senderOnly', icon: 'reply', label: 'mail.reply' },
    { answer: 'everyone', icon: 'reply_all', label: 'mail.replyAll' },
    { answer: 'forward', icon: 'forward', label: 'mail.forward' },
];

export function MailToolbar({ strip, fit }: StripFitting) {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const composing = useComposing();
    const listed = useListedMail();
    const open = workspace.selection;
    const actShape: ControlShape = fit === 'symbols' ? 'symbol' : 'labelled';

    return (
        <div
            ref={strip}
            role="toolbar"
            aria-label={translate('mail.toolbar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-line bg-panel px-3 py-2 shadow-raised"
        >
            {/* Composing stands first, and only while the names fit beside it: otherwise it is the floating control
                over the list, which the mail space draws. The divider after it is what the measurement reads as the
                second child, so nothing may stand between the two. */}
            {fit !== 'labelled' ? null : (
                <>
                    {composing.offered ? (
                        <Control
                            label={translate('mail.compose')}
                            icon="edit_square"
                            shape="primary"
                            onPress={() => {
                                composing.compose({ kind: 'new' });
                            }}
                        />
                    ) : (
                        <PlannedControl label={translate('mail.compose')} icon="edit_square" shape="primary" />
                    )}

                    <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-line" />
                </>
            )}

            {/* Answering needs something to answer, so with nothing open each of the three is drawn as what it is
                rather than left out: a strip whose controls appear as a message is opened is one that moves under a
                reader's cursor. */}
            {answers.map((answering) =>
                composing.offered && open !== null ? (
                    <Control
                        key={answering.icon}
                        label={translate(answering.label)}
                        icon={answering.icon}
                        shape={actShape}
                        onPress={() => {
                            composing.compose({ kind: 'answer', answers: answering.answer, storedEmailId: open });
                        }}
                    />
                ) : (
                    <PlannedControl
                        key={answering.icon}
                        label={translate(answering.label)}
                        icon={answering.icon}
                        shape={actShape}
                    />
                ),
            )}

            {/* The five that change the mailbox, over the one message that is open. With nothing open each of them
                says so rather than being left out, for the reason the three answers above are drawn either way. */}
            <MailboxActControls messages={open === null ? [] : actedMessages(listed, [open])} shape={actShape} />

            {/* The one control in the strip that changes the view rather than the message, which is why the design
                keeps it apart at the far edge and draws it as its symbol alone at every width. What it hides is the
                thread's state panels — MailFathom's own reading of a conversation — which the client does not draw
                yet, so there is nothing for it to hide. */}
            <PlannedControl label={translate('mail.hidePanels')} icon="fullscreen" shape="symbol" className="ms-auto" />
        </div>
    );
}
