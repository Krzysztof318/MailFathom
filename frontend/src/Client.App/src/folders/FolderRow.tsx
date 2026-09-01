// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { MailFolderRole } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { isCurrent, needsAttention, synchronizationStateLabel } from '../synchronization/synchronizationState';
import type { FolderTreeRow } from './folderTree';

// One row of the tree, which is its own component because it is the row of a list and because it carries everything a
// tree asks of a row: where it sits, whether it is open, whether it is what the client is scoped to, and whether it is
// the one the keyboard is on.
//
// What names it is decided here rather than by the service. A folder playing a role is called by the role — an inbox
// is an inbox whatever the provider named the folder and in whatever language — and everything else is called what its
// mail server calls it.

const roleLabels: Readonly<Record<MailFolderRole, MessageKey>> = {
    Inbox: 'folder.inbox',
    Drafts: 'folder.drafts',
    Sent: 'folder.sent',
    Archive: 'folder.archive',
    Junk: 'folder.junk',
    Trash: 'folder.trash',
    Flagged: 'folder.flagged',
    Important: 'folder.important',
    All: 'folder.all',
    Outbox: 'folder.outbox',
};

// How far in each level sits. Stated as one list rather than as a width worked out from the level, because a computed
// indentation is a value written outside the token layer however it is arrived at. Anything deeper than the list sits
// where its last entry does: a mailbox nested six deep is rare, and a row indented off the side of a narrow column is
// worse than one that stops moving.
const levelIndents: readonly string[] = ['ps-2', 'ps-6', 'ps-10', 'ps-14', 'ps-16'];

export function FolderRow({
    row,
    position,
    setSize,
    expanded,
    selected,
    focusable,
    onSelect,
    onToggle,
    onKeyDown,
    onElement,
}: {
    readonly row: FolderTreeRow;
    readonly position: number;
    readonly setSize: number;
    readonly expanded: boolean | null;
    readonly selected: boolean;
    readonly focusable: boolean;
    readonly onSelect: () => void;
    readonly onToggle: () => void;
    readonly onKeyDown: (event: KeyboardEvent<HTMLLIElement>) => void;
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { translate } = useLocalization();
    const indent = levelIndents[Math.min(Math.max(row.level, 1), levelIndents.length) - 1] ?? '';

    return (
        <li
            ref={onElement}
            role="treeitem"
            aria-level={row.level}
            aria-posinset={position}
            aria-setsize={setSize}
            aria-expanded={expanded ?? undefined}
            aria-selected={row.scope === null ? undefined : selected}
            tabIndex={focusable ? 0 : -1}
            onClick={onSelect}
            onKeyDown={onKeyDown}
            className={`flex items-center gap-2 rounded-md py-1 pe-2 text-sm transition ${indent} ${
                row.scope === null ? '' : 'cursor-pointer'
            } ${selected ? 'bg-accent-soft text-accent-strong' : 'text-text-soft hover:bg-hover'}`}
        >
            <Twist
                expanded={expanded}
                onToggle={() => {
                    onToggle();
                }}
            />

            <span className="truncate">{nameOf(row, translate)}</span>

            <State row={row} />
            <Counts unread={row.unreadEmailCount} stored={row.storedEmailCount} />
        </li>
    );
}

function nameOf(row: FolderTreeRow, translate: (key: MessageKey) => string): string {
    if (row.scope?.kind === 'everything') {
        return translate('scope.allMailboxes');
    }

    return row.role === null ? row.name : translate(roleLabels[row.role]);
}

// The control that opens a row, and the room it takes when there is nothing to open, so every name on one level starts
// at the same place. It is hidden from the accessibility tree because a tree already says whether a row is open and
// opens one from the keyboard: an extra control here would be a second way to do what arrow keys already do, announced
// on every row.
function Twist({ expanded, onToggle }: { readonly expanded: boolean | null; readonly onToggle: () => void }) {
    if (expanded === null) {
        return <span aria-hidden="true" className="size-4 shrink-0" />;
    }

    return (
        <span
            aria-hidden="true"
            className="shrink-0 rounded p-0.5 hover:bg-hover"
            onClick={(event) => {
                event.stopPropagation();
                onToggle();
            }}
        >
            <svg viewBox="0 0 24 24" className={`size-3 fill-current transition ${expanded ? 'rotate-90' : ''}`}>
                <path d="M9 5l7 7-7 7V5Z" />
            </svg>
        </span>
    );
}

// Said in words rather than in a colour, and only where there is something to say: a row whose last attempt succeeded
// and left nothing behind carries nothing, so the two rows that do are the ones a reader's eye lands on.
function State({ row }: { readonly row: FolderTreeRow }) {
    const { translate } = useLocalization();

    if (row.state === null || isCurrent(row.state, row.behind)) {
        return null;
    }

    return (
        <span className={`shrink-0 text-xs ${needsAttention(row.state) ? 'text-warning' : 'text-muted'}`}>
            {translate(synchronizationStateLabel(row.state, row.behind))}
        </span>
    );
}

// What is unread here, and what is held here at all. Both are of the deployment's own copy rather than of the mailbox,
// which is what the state beside them says; the words a reader hears are carried for both, because a bare number on a
// row is a number of nothing to somebody who cannot see the column it is in.
function Counts({ unread, stored }: { readonly unread: number | null; readonly stored: number | null }) {
    const { locale, translate } = useLocalization();

    if (unread === null || stored === null) {
        return null;
    }

    const numbers = new Intl.NumberFormat(locale);

    return (
        <span className="ms-auto flex shrink-0 items-baseline gap-2 text-xs tabular-nums">
            {unread === 0 ? null : (
                <span className="font-semibold text-accent-strong">
                    <span aria-hidden="true">{numbers.format(unread)}</span>
                    <span className="sr-only">{translate('folders.unread', { count: numbers.format(unread) })}</span>
                </span>
            )}

            <span className="text-faint">
                <span aria-hidden="true">{numbers.format(stored)}</span>
                <span className="sr-only">{translate('folders.stored', { count: numbers.format(stored) })}</span>
            </span>
        </span>
    );
}
