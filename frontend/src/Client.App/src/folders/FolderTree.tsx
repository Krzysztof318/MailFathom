// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
    readMailFolders,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
} from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadMarking } from '../readMarking/useReadMarking';
import { useSignalledChanges } from '../signals/signalledChanges';
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { FolderRow } from './FolderRow';
import { FolderRowMenu } from './FolderRowMenu';
import { actsOffered, type FolderAct } from './folderActs';
import { aliasSegments } from './folderTreeRows';
import { folderRowName } from './folderRowNames';
import {
    folderTreeOf,
    namesRow,
    openingScope,
    visibleRows,
    type FolderTreeRow,
    type VisibleRow,
} from './folderTreeRows';
import { unreadAfterMarking } from './unreadAfterMarking';
import { useFolderMaintenance, type FolderMailbox } from './useFolderMaintenance';

// The client's scope selector: which mailbox and which folder everything else is about. It is a tree because the
// mailboxes are a tree, and it is one tree rather than one per account because several mailboxes are one workspace —
// the row above them all, and the roles under it, are what make asking about every inbox at once a single act.
//
// It reads the folders route rather than the accounts route the frame polls, because that route is the tree: it costs
// what counting a folder's mail costs, and it answers the mailboxes and their folders in one exchange so that a screen
// never draws one picture out of two answers.
//
// What it does not do is decide anything about the mail itself. Selecting a row writes the scope into the workspace,
// and the list, the search, and the next question read it from there.
//
// Two things fold here and they are different questions. A row folds away what is under it, which is this tree's own
// and is what `workspace.foldsToggled` holds — the rows whose fold somebody moved away from what the row opens at,
// rather than the rows that are folded, because a mailbox opens expanded and a subfolder opens collapsed. The column
// folds to a rail, which is the composition's and is what `workspace.mailboxesFolded` holds — read here rather than
// handed in because the composition that owns it renders this tree as a region it was given rather than as a child it
// built. Neither touches the other: a rail draws the same rows a column would, each as a symbol.
//
// **What a row answers a press with is not this tree's decision either.** `folderActs.ts` says which acts a row
// offers, `folders/FolderMaintenance.tsx` performs them, and what this component does is name the mailbox each act
// happens inside — because the tree is where the account, its name, and the aliases it already declares are held.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

/** What one attempt answered, tagged with the attempt, so whether a read is in flight is worked out rather than kept. */
interface Answered {
    readonly attempt: number;
    readonly result: ClientResult<MailFolderDirectory>;
}

export function FolderTree({
    session,
    transport,
    online,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly online: boolean;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const { marked } = useReadMarking();
    const maintenance = useFolderMaintenance();
    const signalledChanges = useSignalledChanges();
    const [attempt, setAttempt] = useState(0);

    // A second counter, and deliberately not the one above: an attempt is a read with nothing worth keeping behind it
    // and it replaces the tree with a line saying so, while this one is a read the deployment asked for underneath a
    // tree somebody is looking at. Raising `attempt` for it would blank the column every time mail arrived.
    const [refreshed, setRefreshed] = useState(0);
    const [answered, setAnswered] = useState<Answered | null>(null);
    const [focused, setFocused] = useState<string | null>(null);

    // Which row's menu is open and where the gesture that opened it happened. One at a time, because a second menu
    // over the first is a reader choosing which of them their next press belongs to.
    const [menu, setMenu] = useState<{ readonly key: string; readonly at: MenuPoint } | null>(null);
    const [connected, setConnected] = useState(online);
    const elements = useRef(new Map<string, HTMLLIElement>());

    // A network gap ends the answer it interrupted rather than outliving it. Coming back re-reads with the attempt
    // unchanged, so an answer kept across the gap would report a read that is over while the new one is still running,
    // and the tree would swap under a reader with nothing having said one was in flight. Adjusted during render, which
    // is where React answers a changed prop: an effect would set it a rendered frame too late, which is the frame the
    // stale tree would be drawn in.
    if (connected !== online) {
        setConnected(online);

        if (!online) {
            setAnswered(null);
        }
    }

    // Nothing is read without a network, and coming back re-runs this — which is the whole of the recovery from that
    // direction, exactly as it is for the accounts the frame reads.
    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void readMailFolders(session, transport).then((result) => {
            if (listening) {
                // A read under a tree already drawn — a signal, a refresh — that the deployment did not answer leaves the
                // tree standing rather than replacing it with a sentence about a failure: what is drawn is still the
                // truest thing anybody has, and the next signal or refresh asks again. A first read and a retry have
                // nothing drawn under them, so their failure is said.
                setAnswered((current) =>
                    result.outcome === 'failed' && current?.attempt === attempt && current.result.outcome === 'read'
                        ? current
                        : { attempt, result },
                );
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, attempt, refreshed, online, maintenance.changed]);

    // A scope outlives the tree it was chosen from, so the answer that arrives is also what says whether it still
    // names anything: the session's store carries a folder across a reload, and a folder deleted on the mail server in
    // between would otherwise leave the list asking for a mailbox nobody can reach and the tree highlighting no row.
    // Falling back to what the tree opens on is what the client starts at, so a folder that has gone reads as never
    // having been chosen rather than as an error somebody has to press through.
    //
    // The question is asked of the rows rather than of the directory, and that is what makes the fallback right for a
    // user holding one account: the tree offers them no row spanning every account, so `everything` is a scope nothing
    // draws even though the directory still allows it, and a column whose open row is nowhere is the defect. Every
    // row, folded or not, because what is folded away is still a row somebody chose.
    //
    // An effect rather than a derivation, which § *State* would otherwise prefer: the scope is one value the whole
    // client reads and this tree is one of its readers, so deriving a second scope here would be the two-values-that
    // -must-agree defect rather than a cure for it. It is also not the reconciling effect that rule refuses — nothing
    // is being kept in step, and this runs once per answer that disagrees rather than on every render.
    useEffect(() => {
        if (answered?.result.outcome !== 'read') {
            return;
        }

        const offered = answered.result.value;
        const inScope = scopeKey(workspace.scope);

        if (namesRow(folderTreeOf(offered), inScope)) {
            return;
        }

        revise({ scope: openingScope(offered) });
    }, [answered, workspace.scope, revise]);

    // Four of the six kinds move this tree, because all four move a count it draws: mail arriving in a folder, a
    // message changing folder, the mapping itself moving, and a read mark. It re-reads under whatever is drawn rather
    // than replacing it, so a reader whose pointer is on a row keeps the row. A refresh reads it again the same way, for
    // the reason it reads everything again: something may have moved that nobody said.
    //
    // A stated flag is the one that is read rather than acted on wholesale. A count is derived from every message in a
    // folder rather than from the ones a statement names, so there is nothing to apply in place — but a statement about
    // stars alone moves no count, and re-reading the tree for one would be a request per star.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (
                    signal.kind === 'refresh' ||
                    signal.kind === 'folders.changed' ||
                    signal.kind === 'mail.arrived' ||
                    signal.kind === 'mail.changed' ||
                    (signal.kind === 'mail.flags.changed' && signal.flags.some((stated) => stated.isSeen !== null))
                ) {
                    setRefreshed((token) => token + 1);
                }
            }),
        [signalledChanges],
    );

    if (!online) {
        return <Note>{translate('connection.offline')}</Note>;
    }

    // Every read this tree starts is one with nothing worth keeping on the screen behind it: the first, one a retry
    // started from a failure, and one the network coming back started after the offline note had already replaced the
    // tree. So a read in flight is the whole of what the screen says, rather than a line beside a drawing that would
    // otherwise be a sentence about the attempt before it.
    if (answered?.attempt !== attempt) {
        return <Note announced>{translate('folders.reading')}</Note>;
    }

    if (answered.result.outcome === 'failed') {
        const reason = answered.result.failure.reason;

        return (
            <div className="flex flex-col items-start gap-2 px-2.75 py-2">
                <p className="text-sm text-warning">
                    {translate('folders.failed', { reason: translate(failureLabels[reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the five failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
                {reason === 'unavailable' ? (
                    <SecondaryButton
                        label={translate('connection.retry')}
                        onActivate={() => {
                            setAttempt(attempt + 1);
                        }}
                    />
                ) : null}
            </div>
        );
    }

    // What the deployment answered, less the mail this client has marked read since it answered. A count that still
    // named a message the reader has just opened would disagree with the row drawing that message read, which is the
    // one thing about an unread count somebody notices.
    const directory = unreadAfterMarking(answered.result.value, marked);

    // A user holding no account is told so and told what would fill it, rather than being handed an empty tree.
    if (directory.accounts.length === 0) {
        return (
            <Note>
                {translate('connection.noAccounts')} {translate('accounts.noneDeclared')}
            </Note>
        );
    }

    const foldsToggled = new Set(workspace.foldsToggled);
    const visible = visibleRows(folderTreeOf(directory), foldsToggled);

    // A row is what the client is scoped to when it stands for that scope, which is a comparison of one string because
    // a row is keyed by the scope selecting it writes.
    const inScope = scopeKey(workspace.scope);

    // The row the keyboard is on, which is the first one until somebody moves it, and the first one again whenever the
    // row it was on has been folded away with its parent.
    const carryingFocus = visible.find((visibleRow) => visibleRow.row.key === focused) ?? visible[0];

    // Which group each first-level row heads, counted from the whole workspace, which is what colours its mark.
    const groupOrdinals = new Map(
        visible
            .filter((visibleRow) => visibleRow.row.level === 1)
            .map((visibleRow, ordinal) => [visibleRow.row.key, ordinal] as const),
    );

    // What each row offers, less what this credential may not do. The grant narrows the list rather than the row
    // being absent from it: `folderActs.ts` answers what the *row* is, and what the *credential* is comes from the
    // acts themselves, which is the one place either question is asked.
    function actsFor(row: FolderTreeRow): readonly FolderAct[] {
        return actsOffered(row).filter((act) => (act === 'markAllRead' ? maintenance.marksRead : maintenance.offered));
    }

    /** The mailbox a row belongs to, or `null` for a row spanning every mailbox and for an account nothing answered for. */
    function mailboxOf(row: FolderTreeRow): FolderMailbox | null {
        const entry = directory.accounts.find(({ account }) => account.id === row.accountId);

        return entry === undefined
            ? null
            : {
                  accountId: entry.account.id,
                  accountName: entry.account.displayName,
                  declaredAliases: entry.folders.map((folder) => folder.alias),
              };
    }

    function takeAct(act: FolderAct, row: FolderTreeRow): void {
        const mailbox = mailboxOf(row);

        if (mailbox === null) {
            return;
        }

        const said = folderRowName(row, translate);

        switch (act) {
            case 'newFolder':
                maintenance.declare(mailbox, null);
                break;
            case 'newFolderInside':
                // The row itself is the parent, and a level of an alias nothing is bound to has no place on a server
                // to propose a child's path from — so the dialog proposes one from the root of the mailbox instead.
                maintenance.declare(
                    mailbox,
                    row.alias === null ? null : { alias: row.alias, remotePath: row.remotePath ?? [] },
                );
                break;
            case 'markAllRead':
                maintenance.markAllRead(mailbox.accountId, row.alias, said);
                break;
            case 'editFolder':
                if (row.alias !== null && row.remotePath !== null) {
                    maintenance.revise(mailbox, { alias: row.alias, remotePath: row.remotePath }, above(row));
                }

                break;
            case 'deleteFolder':
                if (row.alias !== null && row.remotePath !== null) {
                    maintenance.withdraw(mailbox, {
                        alias: row.alias,
                        remotePath: row.remotePath,
                        name: said,
                        holdsNested: row.children.length > 0,
                    });
                }

                break;
        }
    }

    function focusRow(at: number): void {
        const moved = visible[Math.min(Math.max(at, 0), visible.length - 1)];

        if (moved !== undefined) {
            setFocused(moved.row.key);
            elements.current.get(moved.row.key)?.focus();
        }
    }

    // What is recorded is the move away from what the row opens at rather than the fold itself, which is what lets
    // one set answer both directions: a mailbox somebody folded away and a subfolder somebody opened are the same act.
    function fold(row: FolderTreeRow, away: boolean): void {
        const moved = new Set(foldsToggled);

        if (away === row.opensCollapsed) {
            moved.delete(row.key);
        } else {
            moved.add(row.key);
        }

        revise({ foldsToggled: [...moved] });
    }

    // The folder a row sits inside, which an edit needs so that the path it proposes is composed under the same
    // parent the folder already has. Read off the row's own alias and path rather than by walking the tree: both nest
    // on the same levels, and a row that has neither is one nothing is being edited on.
    function above(row: FolderTreeRow): { readonly alias: string; readonly remotePath: readonly string[] } | null {
        const segments = row.alias === null ? [] : aliasSegments(row.alias);

        return segments.length < 2
            ? null
            : {
                  alias: segments.slice(0, -1).join('/'),
                  remotePath: (row.remotePath ?? []).slice(0, -1),
              };
    }

    // The parent of a row is the nearest row above it sitting one level out, which is what a flat list of rows that
    // each state their own level makes answerable without a second structure to walk.
    function parentOf(at: number): number {
        const level = visible[at]?.row.level ?? 1;

        for (let above = at - 1; above >= 0; above -= 1) {
            const candidate = visible[above];

            if (candidate !== undefined && candidate.row.level < level) {
                return above;
            }
        }

        return at;
    }

    function onKeyDown(event: KeyboardEvent<HTMLLIElement>, at: number, visibleRow: VisibleRow): void {
        switch (event.key) {
            case 'ArrowDown':
                focusRow(at + 1);
                break;
            case 'ArrowUp':
                focusRow(at - 1);
                break;
            case 'Home':
                focusRow(0);
                break;
            case 'End':
                focusRow(visible.length - 1);
                break;
            case 'ArrowRight':
                if (visibleRow.expanded === false) {
                    fold(visibleRow.row, false);
                } else if (visibleRow.expanded === true) {
                    focusRow(at + 1);
                }

                break;
            case 'ArrowLeft':
                if (visibleRow.expanded === true) {
                    fold(visibleRow.row, true);
                } else {
                    focusRow(parentOf(at));
                }

                break;
            case 'Enter':
            case ' ':
                if (visibleRow.row.scope !== null) {
                    revise({ scope: visibleRow.row.scope });
                }

                break;
            default:
                return;
        }

        event.preventDefault();
    }

    // The row whose menu is open, looked up rather than held, so a tree read again under an open menu draws the menu
    // about the row as it now is rather than about a copy of the row it was opened on.
    const pressed = menu === null ? undefined : visible.find((visibleRow) => visibleRow.row.key === menu.key)?.row;

    // A row can leave the tree while its menu is open — a read the deployment signalled that no longer names the
    // folder, or an ancestor folded away — and the menu leaves with it while the state that opened it stays behind.
    // Nothing would then close that state, so the row's own next press would reopen a menu the tree believes is
    // already open. Cleared during render, where the row's absence is first known, rather than in an effect, which
    // would leave one frame drawn with a menu whose row is gone. What `onClose` does beside this — putting focus back
    // on the row — has nothing left to put it on here, and the tab stop the tree keeps is what a reader tabs back to.
    if (menu !== null && pressed === undefined) {
        setMenu(null);
    }

    return (
        <>
            <ul aria-label={translate('folders.label')} className="flex flex-col gap-0.5" role="tree">
                {visible.map((visibleRow, at) => (
                    <FolderRow
                        key={visibleRow.row.key}
                        row={visibleRow.row}
                        position={visibleRow.position}
                        setSize={visibleRow.setSize}
                        expanded={visibleRow.expanded}
                        selected={visibleRow.row.key === inScope}
                        focusable={visibleRow.row.key === carryingFocus?.row.key}
                        folded={workspace.mailboxesFolded}
                        groupOrdinal={groupOrdinals.get(visibleRow.row.key) ?? null}
                        onSelect={() => {
                            setFocused(visibleRow.row.key);

                            if (visibleRow.row.scope !== null) {
                                revise({ scope: visibleRow.row.scope });
                            }
                        }}
                        onToggle={() => {
                            // The tab stop follows the row a pointer just acted on, exactly as selecting one moves it:
                            // the browser has already put DOM focus on this row, and a tab stop left on another is a
                            // reader tabbing out of the tree from somewhere they never were.
                            setFocused(visibleRow.row.key);
                            fold(visibleRow.row, visibleRow.expanded === true);
                        }}
                        onPress={
                            actsFor(visibleRow.row).length === 0
                                ? undefined
                                : (at) => {
                                      setFocused(visibleRow.row.key);
                                      setMenu({ key: visibleRow.row.key, at });
                                  }
                        }
                        onKeyDown={(event) => {
                            onKeyDown(event, at, visibleRow);
                        }}
                        onElement={(element) => {
                            if (element === null) {
                                elements.current.delete(visibleRow.row.key);
                            } else {
                                elements.current.set(visibleRow.row.key, element);
                            }
                        }}
                    />
                ))}
            </ul>

            {menu === null || pressed === undefined ? null : (
                <FolderRowMenu
                    acts={actsFor(pressed)}
                    header={folderRowName(pressed, translate)}
                    at={menu.at}
                    onAct={(act) => {
                        takeAct(act, pressed);
                    }}
                    onClose={() => {
                        setMenu(null);
                        elements.current.get(pressed.key)?.focus();
                    }}
                />
            )}
        </>
    );
}

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="px-2.75 py-2 text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
