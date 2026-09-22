// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { ContextMenu } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';

/**
 * What a conversation row offers when it is pressed and held: picking it out, opening it, and deleting it.
 *
 * The design draws archiving beside these, and it is left out rather than drawn inert because the deployment keeps no
 * archive of conversations to put one in.
 */
export function ConversationRowMenu({
    title,
    at,
    onSelect,
    onOpen,
    onAskDeletion,
    onClose,
}: {
    readonly title: string;
    readonly at: MenuPoint;
    readonly onSelect: () => void;
    readonly onOpen: () => void;
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
