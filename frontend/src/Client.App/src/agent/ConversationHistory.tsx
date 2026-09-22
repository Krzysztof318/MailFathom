// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import type { AgentConversationSummary, ClientFailureReason } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { Control } from '../controls/Control';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { ConversationRow } from './ConversationRow';
import { ConversationRowMenu } from './ConversationRowMenu';
import { ConversationSelectionBar } from './ConversationSelectionBar';
import type { ConversationHistory as History } from './useConversationHistory';

const historyFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'agent.historyFailed.unauthenticated',
    unauthorized: 'agent.historyFailed.unauthorized',
    unavailable: 'agent.historyFailed.unavailable',
    unreadable: 'agent.historyFailed.unreadable',
    missing: 'agent.historyFailed.unavailable',
};

/**
 * The conversations this person has had with the agent, newest first, and what can be done to them from here.
 *
 * The design draws a search field over the list; it is left out until the history can be searched the way mail is,
 * because filtering the hundred titles this client happens to hold would answer a different question.
 */
export function ConversationHistory({
    history,
    open,
    selected,
    onOpen,
    onSelected,
    onNew,
    onAskDeletion,
    onReadAgain,
    onClose,
}: {
    readonly history: History;

    /** The conversation in front, and `null` where a new one is. */
    readonly open: string | null;

    readonly selected: readonly string[];
    readonly onOpen: (conversation: string) => void;
    readonly onSelected: (selected: readonly string[]) => void;
    readonly onNew: () => void;
    readonly onAskDeletion: (conversations: readonly string[]) => void;
    readonly onReadAgain: () => void;

    /** Puts the panel away, which only a panel drawn over the thread offers. */
    readonly onClose: (() => void) | null;
}) {
    const { translate } = useLocalization();
    const [menu, setMenu] = useState<{
        readonly conversation: AgentConversationSummary;
        readonly at: MenuPoint;
        readonly opener: HTMLElement | null;
    } | null>(null);

    const { conversations, reading, failure } = history;

    return (
        <div className="flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto px-2.5 py-3.5">
            <div className="flex items-center gap-2.25 px-0.5 pb-1">
                <h2 className="text-xs tracking-widest text-muted uppercase">{translate('agent.history')}</h2>

                <span className="ms-auto">
                    <Control label={translate('agent.new')} shape="link" onPress={onNew} />
                </span>

                {onClose === null ? null : (
                    <Control label={translate('agent.closeHistory')} icon="close" shape="symbol" onPress={onClose} />
                )}
            </div>

            {selected.length > 0 ? (
                <ConversationSelectionBar
                    count={selected.length}
                    onClear={() => {
                        onSelected([]);
                    }}
                    onAskDeletion={() => {
                        onAskDeletion(selected);
                    }}
                />
            ) : null}

            {conversations.length > 0 ? (
                <ul
                    role="listbox"
                    aria-label={translate('agent.conversations')}
                    aria-multiselectable={true}
                    className="flex flex-col gap-px"
                >
                    {conversations.map((conversation) => (
                        <ConversationRow
                            key={conversation.id}
                            conversation={conversation}
                            open={conversation.id === open}
                            selected={selected.includes(conversation.id)}
                            selecting={selected.length > 0}
                            onOpen={() => {
                                onOpen(conversation.id);
                            }}
                            onToggle={() => {
                                onSelected(withToggled(selected, conversation.id));
                            }}
                            onPress={(at) => {
                                // Focus goes back where it was when the menu closes, which for a menu opened from the
                                // keyboard is the row it was opened on.
                                const opener = document.activeElement;
                                setMenu({ conversation, at, opener: opener instanceof HTMLElement ? opener : null });
                            }}
                        />
                    ))}
                </ul>
            ) : null}

            {reading && conversations.length === 0 ? (
                <p role="status" className="px-1 py-2 text-sm text-muted">
                    {translate('agent.historyReading')}
                </p>
            ) : null}

            {failure === null ? null : (
                <div className="flex flex-col items-start gap-2 px-1 py-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(historyFailures[failure])}
                    </p>

                    {failure === 'unavailable' ? (
                        <SecondaryButton
                            label={translate('agent.readAgain')}
                            shape="compact"
                            onActivate={onReadAgain}
                        />
                    ) : null}
                </div>
            )}

            {!reading && failure === null && conversations.length === 0 ? (
                <p className="px-1 py-2 text-sm text-muted text-pretty">{translate('agent.historyEmpty')}</p>
            ) : null}

            {menu === null ? null : (
                <ConversationRowMenu
                    title={menu.conversation.title ?? translate('agent.newConversation')}
                    at={menu.at}
                    onSelect={() => {
                        onSelected(onlySelected(menu.conversation.id));
                    }}
                    onOpen={() => {
                        onOpen(menu.conversation.id);
                    }}
                    onAskDeletion={() => {
                        onAskDeletion([menu.conversation.id]);
                    }}
                    onClose={() => {
                        menu.opener?.focus();
                        setMenu(null);
                    }}
                />
            )}
        </div>
    );
}
