// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { AgentConversationSummary } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { ConversationRow } from './ConversationRow';

/**
 * One list of the history — the conversations being worked in, or the ones put away — drawn as rows that can be picked
 * out. Which row the keyboard is on is the history's to decide, because the arrow keys walk from one list into the next.
 */
export function ConversationList({
    label,
    conversations,
    open,
    selected,
    focusable,
    onWalk,
    attach,
    onReached,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly label: string;
    readonly conversations: readonly AgentConversationSummary[];

    /** The conversation in front, and `null` where a new one is. */
    readonly open: string | null;

    readonly selected: readonly string[];

    /** The row that is the history's one tab stop, which may stand in the other list. */
    readonly focusable: string | null;

    readonly onWalk: (event: KeyboardEvent<HTMLUListElement>) => void;
    readonly attach: (conversation: string, row: HTMLLIElement | null) => void;
    readonly onReached: (conversation: string) => void;
    readonly onOpen: (conversation: string) => void;
    readonly onToggle: (conversation: string) => void;
    readonly onPress: (conversation: AgentConversationSummary, at: MenuPoint) => void;
}) {
    return (
        <ul
            role="listbox"
            aria-label={label}
            aria-multiselectable={true}
            className="flex flex-col gap-px"
            onKeyDown={onWalk}
        >
            {conversations.map((conversation) => (
                <ConversationRow
                    key={conversation.id}
                    conversation={conversation}
                    open={conversation.id === open}
                    selected={selected.includes(conversation.id)}
                    selecting={selected.length > 0}
                    focusable={conversation.id === focusable}
                    attach={(row) => {
                        attach(conversation.id, row);
                    }}
                    onReached={() => {
                        onReached(conversation.id);
                    }}
                    onOpen={() => {
                        onOpen(conversation.id);
                    }}
                    onToggle={() => {
                        onToggle(conversation.id);
                    }}
                    onPress={(at) => {
                        onPress(conversation, at);
                    }}
                />
            ))}
        </ul>
    );
}
