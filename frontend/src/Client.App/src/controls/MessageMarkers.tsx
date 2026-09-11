// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { Icon } from './Icon';
import type { IconName } from './icons';
import { useLocalization } from '../localization/useLocalization';

// What the mail server said about the message, in the order a reader scans for it. Each carries its own words, because
// a mark with no name is a mark nobody using a screen reader can see at all.
//
// It sits here rather than beside the mail list for the reason `ReceivedAt` does: the conversation draws the same three
// marks about the same message, and a second copy of them is how one screen comes to say a message carries a file and
// the other says it carries none.

/** The marks a message carries, pushed to the end of the line it is drawn on. */
export function MessageMarkers({
    email,
    flagged,
}: {
    readonly email: MailTimelineEntry;

    /**
     * Whether the flag is drawn, which is a value rather than `email.flagged` because the two differ for minutes.
     *
     * Flagging is a durable mutation the account's own pass carries to the mail server, and the stored flag is an
     * observation of what that server was seen to hold — so what the reader is shown is what was asked for, and only
     * the surface that holds the pending acts can say what that is. `mailboxActs/useMailboxActs.ts` answers it once,
     * in `drawnFlagged`, so two screens drawing these marks cannot come to disagree about one message.
     */
    readonly flagged: boolean;
}) {
    const { translate } = useLocalization();

    return (
        <span className="ms-auto flex shrink-0 items-center gap-1">
            {email.answered ? <Marker name="reply" label={translate('list.answered')} /> : null}

            {email.hasAttachments ? (
                <Marker
                    name="attach_file"
                    label={translate('list.attachments', { count: String(email.attachmentCount) })}
                />
            ) : null}

            {flagged ? <Marker name="flag" label={translate('list.flagged')} /> : null}
        </span>
    );
}

function Marker({ name, label }: { readonly name: IconName; readonly label: string }) {
    return (
        <span className="text-muted">
            <Icon name={name} className="size-4" />
            <span className="sr-only">{label}</span>
        </span>
    );
}
