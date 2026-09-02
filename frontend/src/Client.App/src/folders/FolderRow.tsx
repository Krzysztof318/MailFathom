// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { KeyboardEvent } from 'react';
import type { MailFolderRole } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { isCurrent, needsAttention, synchronizationStateLabel } from '../synchronization/synchronizationState';
import { folderRoleLabels } from '../workspace/mailScope';
import type { FolderTreeRow } from './folderTreeRows';

// One row of the tree, which is its own component because it is the row of a list and because it carries everything a
// tree asks of a row: where it sits, whether it is open, whether it is what the client is scoped to, and whether it is
// the one the keyboard is on.
//
// What names it is decided here rather than by the service. A folder playing a role is called by the role — an inbox
// is an inbox whatever the provider named the folder and in whatever language — and everything else is called what its
// mail server calls it.
//
// Two shapes, as the design project draws them. The first level is the groups — the whole workspace and each mailbox —
// drawn as a small heading with a mark in front of it, and everything under a group is a folder drawn with the symbol
// of what it is: the role's own where it plays one, and a folder's otherwise.

// How far in each level under a group sits. Stated as one list rather than as a width worked out from the level,
// because a computed indentation is a value written outside the token layer however it is arrived at. Anything deeper
// than the list sits where its last entry does: a mailbox nested six deep is rare, and a row indented off the side of
// a narrow column is worse than one that stops moving.
const levelIndents: readonly string[] = ['ps-2.75', 'ps-6', 'ps-9', 'ps-12', 'ps-14'];

const roleIcons: Readonly<Record<MailFolderRole, IconName>> = {
    Inbox: 'inbox',
    Drafts: 'draft',
    Sent: 'send',
    Archive: 'archive',
    Junk: 'report',
    Trash: 'delete',
    Flagged: 'flag',
    Important: 'label_important',
    All: 'all_inbox',
    Outbox: 'outbox',
};

export function FolderRow({
    row,
    position,
    setSize,
    expanded,
    selected,
    focusable,
    groupOrdinal,
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

    /** Which group this row heads, counted from the whole workspace at zero, or `null` for a row under one. */
    readonly groupOrdinal: number | null;

    readonly onSelect: () => void;
    readonly onToggle: () => void;
    readonly onKeyDown: (event: KeyboardEvent<HTMLLIElement>) => void;
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { translate } = useLocalization();
    const group = groupOrdinal !== null;
    const indent = group ? '' : (levelIndents[Math.min(Math.max(row.level - 2, 0), levelIndents.length - 1)] ?? '');

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
            className={`flex items-center gap-2 pe-2 transition ${indent} ${row.scope === null ? '' : 'cursor-pointer'} ${
                group
                    ? `mt-3 rounded-sm py-1.25 ps-2.25 text-xs tracking-wide first:mt-0 ${
                          selected ? 'font-semibold text-accent-deep' : 'text-muted hover:text-text'
                      }`
                    : `rounded-md py-1.75 text-md ${
                          selected ? 'bg-accent-soft font-semibold text-accent-deep' : 'text-text-soft hover:bg-hover'
                      }`
            }`}
        >
            {group ? (
                <GroupMark ordinal={groupOrdinal} />
            ) : (
                <Icon
                    name={row.role === null ? 'folder' : roleIcons[row.role]}
                    className={`size-4.5 shrink-0 ${selected ? 'text-accent-deep' : 'text-muted'}`}
                />
            )}

            <span className="min-w-0 flex-1 truncate">{nameOf(row, translate)}</span>

            <State row={row} />
            <Unread count={row.unreadEmailCount} />

            <Twist
                expanded={expanded}
                onToggle={() => {
                    onToggle();
                }}
            />
        </li>
    );
}

function nameOf(row: FolderTreeRow, translate: (key: MessageKey) => string): string {
    if (row.scope?.kind === 'everything') {
        return translate('scope.allMailboxes');
    }

    return row.role === null ? row.name : translate(folderRoleLabels[row.role]);
}

// The mark in front of a group, which is how the design project tells one mailbox's folders from the next mailbox's:
// the workspace as a whole takes both colours, and the mailboxes alternate between them.
function GroupMark({ ordinal }: { readonly ordinal: number }) {
    const fill =
        ordinal === 0
            ? 'bg-linear-to-br from-mailbox-mark from-50% to-mailbox-mark-alternate to-50%'
            : ordinal % 2 === 1
              ? 'bg-mailbox-mark'
              : 'bg-mailbox-mark-alternate';

    return <span aria-hidden="true" className={`size-2 shrink-0 rounded-full ${fill}`} />;
}

// The control that opens a row, absent where there is nothing to open. It is hidden from the accessibility tree because
// a tree already says whether a row is open and opens one from the keyboard: an extra control here would be a second
// way to do what arrow keys already do, announced on every row.
function Twist({ expanded, onToggle }: { readonly expanded: boolean | null; readonly onToggle: () => void }) {
    if (expanded === null) {
        return null;
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
            <Icon name="chevron_right" className={`size-3.5 transition ${expanded ? 'rotate-90' : ''}`} />
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

// What is unread here, of the deployment's own copy rather than of the mailbox, which is what the state beside it
// says. The words a reader hears are carried, because a bare number on a row is a number of nothing to somebody who
// cannot see the column it is in. A row with nothing unread carries no number, as the design project draws it.
function Unread({ count }: { readonly count: number | null }) {
    const { locale, translate } = useLocalization();

    if (count === null || count === 0) {
        return null;
    }

    const shown = new Intl.NumberFormat(locale).format(count);

    return (
        <span className="shrink-0 text-sm tabular-nums text-faint">
            <span aria-hidden="true">{shown}</span>
            <span className="sr-only">{translate('folders.unread', { count: shown })}</span>
        </span>
    );
}
