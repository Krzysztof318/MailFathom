// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The strip the design project draws across the top of the Mail space: composing, and the eight things a person does
// to a message. None of them exists in the client yet — every one is a write to a mailbox, which is later stages' work
// — so each stands here as what it is: a control the product will have, drawn so that its absence is not mistaken for
// a client that forgot it, and inert so that its presence is not mistaken for one that can do it.

const actions: readonly { readonly icon: IconName; readonly label: MessageKey }[] = [
    { icon: 'reply', label: 'mail.reply' },
    { icon: 'reply_all', label: 'mail.replyAll' },
    { icon: 'forward', label: 'mail.forward' },
    { icon: 'archive', label: 'mail.archive' },
    { icon: 'delete', label: 'mail.delete' },
    { icon: 'flag', label: 'mail.flag' },
    { icon: 'mark_email_unread', label: 'mail.markUnread' },
    { icon: 'drive_file_move', label: 'mail.move' },
];

export function MailToolbar() {
    const { translate } = useLocalization();

    return (
        <div
            role="toolbar"
            aria-label={translate('mail.toolbar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-line bg-panel px-3.5 py-2 shadow-raised"
        >
            <PlannedControl label={translate('mail.compose')} icon="edit_square" shape="primary" />

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-line" />

            {actions.map((action) => (
                <PlannedControl key={action.icon} label={translate(action.label)} icon={action.icon} />
            ))}
        </div>
    );
}
