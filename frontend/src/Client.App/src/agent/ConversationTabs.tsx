// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type KeyboardEvent } from 'react';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

// The keyboard contract is `mailSpace/TabStrip.tsx`'s, stated for conversations rather than for the mail space's four
// kinds of tab and its confirmed close-everything: one tab stop for the strip, the arrow keys, Home and End walking it,
// Enter and Space bringing a conversation forward, Delete putting the focused one down, and the close control beside
// the tab rather than inside it, since the `tab` role makes its contents presentational.

/** One conversation held open beside the others. */
export interface OpenConversation {
    readonly id: string;
    readonly title: string | null;
}

/**
 * The conversations held open, as tabs over the thread, with a way to start another. Closing a tab puts the
 * conversation down and deletes nothing.
 */
export function ConversationTabs({
    open,
    current,
    onChoose,
    onClose,
    onNew,
}: {
    readonly open: readonly OpenConversation[];
    readonly current: string | null;
    readonly onChoose: (conversation: string) => void;
    readonly onClose: (conversation: string) => void;
    readonly onNew: () => void;
}) {
    const { translate } = useLocalization();
    const [reached, setReached] = useState<string | null>(null);
    const tabs = useRef(new Map<string, HTMLButtonElement>());

    const focusable = open.some((held) => held.id === reached)
        ? reached
        : open.some((held) => held.id === current)
          ? current
          : (open[0]?.id ?? null);

    function focusOn(conversation: string | null): void {
        if (conversation !== null) {
            setReached(conversation);
            tabs.current.get(conversation)?.focus();
        }
    }

    // Focus goes where the screen goes after the close — the last conversation still held where the one in front was
    // put down, else the one in front — so it is never left on a tab that is no longer there. Where one conversation is
    // left the strip itself goes, and the screen places focus instead.
    function close(conversation: string): void {
        const remaining = open.filter((held) => held.id !== conversation);

        if (remaining.length > 1) {
            focusOn(current === conversation ? (remaining.at(-1)?.id ?? null) : current);
        }

        onClose(conversation);
    }

    function onKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
        const at = open.findIndex((held) => held.id === focusable);

        if (at < 0) {
            return;
        }

        switch (event.key) {
            case 'ArrowRight':
                focusOn(open[Math.min(at + 1, open.length - 1)]?.id ?? null);
                break;
            case 'ArrowLeft':
                focusOn(open[Math.max(at - 1, 0)]?.id ?? null);
                break;
            case 'Home':
                focusOn(open[0]?.id ?? null);
                break;
            case 'End':
                focusOn(open[open.length - 1]?.id ?? null);
                break;
            case 'Delete':
                close(open[at]?.id ?? '');
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    return (
        <div className="flex shrink-0 items-stretch border-b border-line bg-sunken">
            <div
                role="tablist"
                aria-label={translate('agent.tabs')}
                className="flex min-w-0 flex-1 items-end gap-0.75 overflow-x-auto ps-3 pe-1 pt-1.75"
                onKeyDown={onKeyDown}
            >
                {open.map((conversation) => {
                    const chosen = conversation.id === current;
                    const title = conversation.title ?? translate('agent.newConversation');

                    return (
                        <div
                            key={conversation.id}
                            role="presentation"
                            className={`flex max-w-agent-tab items-center gap-1 rounded-t-lg border text-base whitespace-nowrap ${
                                chosen
                                    ? 'border-line border-b-transparent bg-panel text-text'
                                    : 'border-transparent text-muted hover:text-text'
                            }`}
                        >
                            <button
                                ref={(tab) => {
                                    if (tab === null) {
                                        tabs.current.delete(conversation.id);
                                    } else {
                                        tabs.current.set(conversation.id, tab);
                                    }
                                }}
                                type="button"
                                role="tab"
                                aria-selected={chosen}
                                aria-keyshortcuts="Delete"
                                tabIndex={conversation.id === focusable ? 0 : -1}
                                className={`flex min-w-0 items-center gap-2 py-2 ps-2.75 ${chosen ? 'font-semibold' : ''}`}
                                onClick={() => {
                                    setReached(conversation.id);
                                    onChoose(conversation.id);
                                }}
                            >
                                <Icon name="auto_awesome" className="size-3.5 shrink-0 text-faint" />
                                <span className="truncate">{title}</span>
                            </button>

                            <button
                                type="button"
                                aria-label={translate('agent.closeTab', { title })}
                                title={translate('agent.closeTab', { title })}
                                tabIndex={conversation.id === focusable ? 0 : -1}
                                className="me-1.5 rounded-sm p-0.5 text-faint hover:text-text"
                                onClick={() => {
                                    close(conversation.id);
                                }}
                            >
                                <Icon name="close" className="size-3.75" />
                            </button>
                        </div>
                    );
                })}
            </div>

            <Control
                label={translate('agent.newConversation')}
                icon="add"
                shape="symbol"
                className="mx-2 self-center"
                onPress={onNew}
            />
        </div>
    );
}
