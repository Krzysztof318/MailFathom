// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type KeyboardEvent } from 'react';
import type { AgentConversationSummary, ClientFailureReason } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { ConversationList } from './ConversationList';
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
 * The conversations this person has had with the agent, newest first, and what can be done to them from here. The ones
 * put away stand under the rest in a section of their own, folded until it is opened.
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
    onArchive,
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

    /** Puts the conversations named away, or takes them back out of the archive where `archived` is false. */
    readonly onArchive: (conversations: readonly string[], archived: boolean) => void;

    readonly onAskDeletion: (conversations: readonly string[]) => void;
    readonly onReadAgain: () => void;

    /** Puts the panel away, which only a panel drawn over the thread offers. */
    readonly onClose: (() => void) | null;
}) {
    const { locale, translate } = useLocalization();
    const [menu, setMenu] = useState<{
        readonly conversation: AgentConversationSummary;
        readonly at: MenuPoint;
        readonly opener: HTMLElement | null;
    } | null>(null);

    const [reached, setReached] = useState<string | null>(null);
    const [archiveOpen, setArchiveOpen] = useState(false);
    const rows = useRef(new Map<string, HTMLLIElement>());

    const { conversations, reading, failure } = history;
    const working = conversations.filter((summary) => !summary.archived);
    const archived = conversations.filter((summary) => summary.archived);

    // One row is in the tab order, as in every list here: the one the keyboard last reached, else the one open, else
    // the first — so reaching the history is one stop and the arrow keys walk it, on into the archive where it is open.
    const walkable = archiveOpen ? [...working, ...archived] : working;
    const listed = (conversation: string | null): conversation is string =>
        walkable.some((summary) => summary.id === conversation);
    const focusable = listed(reached) ? reached : listed(open) ? open : (walkable[0]?.id ?? null);

    function focusOn(conversation: string | null): void {
        if (conversation !== null) {
            setReached(conversation);
            rows.current.get(conversation)?.focus();
        }
    }

    function walked(event: KeyboardEvent<HTMLUListElement>): void {
        const at = walkable.findIndex((summary) => summary.id === focusable);
        const to: Readonly<Record<string, number>> = {
            ArrowDown: Math.min(at + 1, walkable.length - 1),
            ArrowUp: Math.max(at - 1, 0),
            Home: 0,
            End: walkable.length - 1,
        };
        const reachedAt = to[event.key];

        if (reachedAt !== undefined) {
            event.preventDefault();
            focusOn(walkable[reachedAt]?.id ?? null);
        }
    }

    function attachRow(conversation: string, row: HTMLLIElement | null): void {
        if (row === null) {
            rows.current.delete(conversation);
        } else {
            rows.current.set(conversation, row);
        }
    }

    function pressRow(conversation: AgentConversationSummary, at: MenuPoint): void {
        // Focus goes back where it was when the menu closes, which for a menu opened from the keyboard is the row it
        // was opened on.
        const opener = document.activeElement;
        setMenu({ conversation, at, opener: opener instanceof HTMLElement ? opener : null });
    }

    function toggleRow(conversation: string): void {
        onSelected(withToggled(selected, conversation));
    }

    function listOf(label: string, listedHere: readonly AgentConversationSummary[]) {
        return (
            <ConversationList
                label={label}
                conversations={listedHere}
                open={open}
                selected={selected}
                focusable={focusable}
                onWalk={walked}
                attach={attachRow}
                onReached={setReached}
                onOpen={onOpen}
                onToggle={toggleRow}
                onPress={pressRow}
            />
        );
    }

    // The bar that held focus goes with the selection, so focus is put on the list before it does.
    function clearSelection(): void {
        focusOn(focusable);
        onSelected([]);
    }

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
                    onClear={clearSelection}
                    onArchive={() => {
                        clearSelection();
                        onArchive(selected, true);
                    }}
                    onAskDeletion={() => {
                        onAskDeletion(selected);
                    }}
                />
            ) : null}

            {working.length > 0 ? listOf(translate('agent.conversations'), working) : null}

            {archived.length > 0 ? (
                <>
                    <button
                        type="button"
                        aria-expanded={archiveOpen}
                        aria-label={translate(archiveOpen ? 'agent.hideArchived' : 'agent.showArchived')}
                        className="mt-1 flex cursor-pointer items-center gap-1.5 border-t border-line-soft px-0.5 pt-2 pb-1 text-muted hover:text-text"
                        onClick={() => {
                            setArchiveOpen((before) => !before);
                        }}
                    >
                        <Icon name={archiveOpen ? 'expand_more' : 'chevron_right'} className="size-4 shrink-0" />
                        <span className="text-xs tracking-widest uppercase">
                            {translate('agent.archived', {
                                count: new Intl.NumberFormat(locale).format(archived.length),
                            })}
                        </span>
                    </button>

                    {archiveOpen ? listOf(translate('agent.archivedConversations'), archived) : null}
                </>
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
                    archived={menu.conversation.archived}
                    at={menu.at}
                    onSelect={() => {
                        onSelected(onlySelected(menu.conversation.id));
                    }}
                    onOpen={() => {
                        onOpen(menu.conversation.id);
                    }}
                    onArchive={() => {
                        onArchive([menu.conversation.id], !menu.conversation.archived);
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
