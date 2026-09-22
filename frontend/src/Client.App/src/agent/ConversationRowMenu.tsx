// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { ContextMenu } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';

/**
 * What a conversation row offers when it is pressed and held: picking it out, opening it, putting it away or taking it
 * back out of the archive, and deleting it.
 */
export function ConversationRowMenu({
    title,
    archived,
    at,
    onSelect,
    onOpen,
    onArchive,
    onAskDeletion,
    onClose,
}: {
    readonly title: string;

    /** Whether the conversation is archived, which turns archiving it into restoring it. */
    readonly archived: boolean;

    readonly at: MenuPoint;
    readonly onSelect: () => void;
    readonly onOpen: () => void;
    readonly onArchive: () => void;
    readonly onAskDeletion: () => void;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <ContextMenu
            header={title}
            at={at}
            items={[
                { icon: 'check_box', label: translate('agent.selectConversations'), choose: onSelect },
                { icon: 'forum', label: translate('agent.openConversation'), choose: onOpen },
                archived
                    ? { icon: 'unarchive', label: translate('agent.restoreConversation'), choose: onArchive }
                    : { icon: 'archive', label: translate('agent.archiveConversation'), choose: onArchive },
                {
                    icon: 'delete',
                    label: translate('agent.deleteConversation'),
                    destroys: true,
                    choose: onAskDeletion,
                },
            ]}
            onClose={onClose}
        />
    );
}
