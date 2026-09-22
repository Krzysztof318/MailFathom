// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

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

    return (
        <div className="flex shrink-0 items-stretch border-b border-line bg-sunken">
            <ul
                aria-label={translate('agent.tabs')}
                className="flex min-w-0 flex-1 items-end gap-0.75 overflow-x-auto ps-3 pe-1 pt-1.75"
            >
                {open.map((conversation) => {
                    const chosen = conversation.id === current;
                    const title = conversation.title ?? translate('agent.newConversation');

                    return (
                        <li
                            key={conversation.id}
                            className={`flex max-w-agent-tab items-center gap-1 rounded-t-lg border text-base whitespace-nowrap ${
                                chosen
                                    ? 'border-line border-b-transparent bg-panel text-text'
                                    : 'border-transparent text-muted hover:text-text'
                            }`}
                        >
                            <button
                                type="button"
                                aria-current={chosen ? 'page' : undefined}
                                className={`flex min-w-0 items-center gap-2 py-2 ps-2.75 ${chosen ? 'font-semibold' : ''}`}
                                onClick={() => {
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
                                className="me-1.5 rounded-sm p-0.5 text-faint hover:text-text"
                                onClick={() => {
                                    onClose(conversation.id);
                                }}
                            >
                                <Icon name="close" className="size-3.75" />
                            </button>
                        </li>
                    );
                })}
            </ul>

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
