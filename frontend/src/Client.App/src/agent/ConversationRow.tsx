// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { AgentConversationSummary } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { useLocalization } from '../localization/useLocalization';

/**
 * One conversation in the history: its title and when it last moved, marked down its leading edge where it is the one
 * open, and washed in the accent where it is picked out.
 *
 * A row answers a press the way every list in this client does: a modifier-held click picks it out, a plain click picks
 * it out while a selection is held and opens it otherwise, and a right-click, a long press, the menu key or Shift+F10
 * opens its menu.
 */
export function ConversationRow({
    conversation,
    open,
    selected,
    selecting,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly conversation: AgentConversationSummary;
    readonly open: boolean;
    readonly selected: boolean;

    /** Whether a selection is being held, which turns a plain press into picking the row out. */
    readonly selecting: boolean;

    readonly onOpen: () => void;
    readonly onToggle: () => void;
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { translate } = useLocalization();
    const press = useRowPress(onPress);

    function pressed(event: KeyboardEvent<HTMLLIElement>): void {
        if (event.key === 'ContextMenu' || (event.key === 'F10' && event.shiftKey)) {
            const box = event.currentTarget.getBoundingClientRect();

            event.preventDefault();
            onPress({ x: box.left + box.width / 2, y: box.top + box.height / 2 });

            return;
        }

        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }

        event.preventDefault();

        if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
            onToggle();
        } else {
            onOpen();
        }
    }

    return (
        <li
            role="option"
            aria-selected={selected}
            aria-current={open ? 'true' : undefined}
            tabIndex={0}
            className={`flex cursor-pointer flex-col gap-0.5 rounded-lg border-s-3 py-2.25 pe-2.75 ps-2.25 ${
                open || selected ? 'border-s-accent bg-accent-soft' : 'border-s-transparent hover:bg-hover'
            }`}
            onKeyDown={pressed}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(event) => {
                if (press.tapSuppressed()) {
                    return;
                }

                if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                    onToggle();
                } else {
                    onOpen();
                }
            }}
        >
            <span className="flex items-baseline gap-2">
                {selected ? <Icon name="check" className="size-3.5 shrink-0 self-center text-accent-deep" /> : null}

                <span className="min-w-0 flex-1 truncate text-md font-semibold">
                    {conversation.title ?? translate('agent.newConversation')}
                </span>

                <ReceivedAt at={conversation.lastActivityAt} />
            </span>
        </li>
    );
}
