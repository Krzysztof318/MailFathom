// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWorkspace } from '../workspace/useWorkspace';

// The strip the design project draws across the top of the Mail space: composing, and the eight things a person does
// to a message. The four that write a message are what this stage builds; the five that change a mailbox are later
// stages' work, so each of those still stands here as what it is — a control the product will have, drawn so that its
// absence is not mistaken for a client that forgot it, and inert so that its presence is not mistaken for one that can
// do it.
//
// What is open and whether writing is offered are both read from context rather than handed down. The toolbar is three
// components below the frame that knows either, and neither is this strip's to own: the mail space already reads the
// workspace, and the composer is asked for from three unrelated places, which is what `useComposing` exists for.

// Answering a message: which of the three answers each control writes, and the symbol and name the design gives it.
const answers: readonly { readonly answer: MailDraftAnswer; readonly icon: IconName; readonly label: MessageKey }[] = [
    { answer: 'senderOnly', icon: 'reply', label: 'mail.reply' },
    { answer: 'everyone', icon: 'reply_all', label: 'mail.replyAll' },
    { answer: 'forward', icon: 'forward', label: 'mail.forward' },
];

// What a message is done with once a mailbox may be written to, which is a later stage in every case.
const planned: readonly { readonly icon: IconName; readonly label: MessageKey }[] = [
    { icon: 'archive', label: 'mail.archive' },
    { icon: 'delete', label: 'mail.delete' },
    { icon: 'flag', label: 'mail.flag' },
    { icon: 'mark_email_unread', label: 'mail.markUnread' },
    { icon: 'drive_file_move', label: 'mail.move' },
];

export function MailToolbar() {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const composing = useComposing();
    const open = workspace.selection;

    return (
        <div
            role="toolbar"
            aria-label={translate('mail.toolbar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-line bg-panel px-3.5 py-2 shadow-raised"
        >
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

            {/* Answering needs something to answer, so with nothing open each of the three is drawn as what it is
                rather than left out: a strip whose controls appear as a message is opened is one that moves under a
                reader's cursor. */}
            {answers.map((answering) =>
                composing.offered && open !== null ? (
                    <Control
                        key={answering.icon}
                        label={translate(answering.label)}
                        icon={answering.icon}
                        shape="symbol"
                        onPress={() => {
                            composing.compose({ kind: 'answer', answers: answering.answer, storedEmailId: open });
                        }}
                    />
                ) : (
                    <PlannedControl
                        key={answering.icon}
                        label={translate(answering.label)}
                        icon={answering.icon}
                        shape="symbol"
                    />
                ),
            )}

            {planned.map((action) => (
                <PlannedControl key={action.icon} label={translate(action.label)} icon={action.icon} shape="symbol" />
            ))}
        </div>
    );
}
