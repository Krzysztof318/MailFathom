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
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadMarking } from '../readMarking/useReadMarking';
import { useSignalledChanges } from '../signals/signalledChanges';
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { FolderRow } from './FolderRow';
import { folderTreeOf, visibleRows, type VisibleRow } from './folderTreeRows';
import { unreadAfterMarking } from './unreadAfterMarking';

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
// and is what `workspace.collapsed` holds. The column folds to a rail, which is the composition's and is what
// `workspace.mailboxesFolded` holds — read here rather than handed in because the composition that owns it renders
// this tree as a region it was given rather than as a child it built. Neither touches the other: a rail draws the
// same rows a column would, each as a symbol.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
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
    const signalledChanges = useSignalledChanges();
    const [attempt, setAttempt] = useState(0);

    // A second counter, and deliberately not the one above: an attempt is a read with nothing worth keeping behind it
    // and it replaces the tree with a line saying so, while this one is a read the deployment asked for underneath a
    // tree somebody is looking at. Raising `attempt` for it would blank the column every time mail arrived.
    const [refreshed, setRefreshed] = useState(0);
    const [answered, setAnswered] = useState<Answered | null>(null);
    const [focused, setFocused] = useState<string | null>(null);
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
                setAnswered({ attempt, result });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, attempt, refreshed, online]);

    // Three of the five kinds move this tree, because all three move a count it draws: mail arriving in a folder, a
    // message changing folder or read state, and the mapping itself moving. It re-reads under whatever is drawn rather
    // than replacing it, so a reader whose pointer is on a row keeps the row.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (
                    signal.kind === 'folders.changed' ||
                    signal.kind === 'mail.arrived' ||
                    signal.kind === 'mail.changed'
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

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
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

    // An owner holding no account is told so and told what would fill it, rather than being handed an empty tree.
    if (directory.accounts.length === 0) {
        return (
            <Note>
                {translate('connection.noAccounts')} {translate('accounts.noneDeclared')}
            </Note>
        );
    }

    const collapsed = new Set(workspace.collapsed);
    const visible = visibleRows(folderTreeOf(directory), collapsed);

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

    function focusRow(at: number): void {
        const moved = visible[Math.min(Math.max(at, 0), visible.length - 1)];

        if (moved !== undefined) {
            setFocused(moved.row.key);
            elements.current.get(moved.row.key)?.focus();
        }
    }

    function fold(key: string, away: boolean): void {
        const folded = new Set(collapsed);

        if (away) {
            folded.add(key);
        } else {
            folded.delete(key);
        }

        revise({ collapsed: [...folded] });
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
                    fold(visibleRow.row.key, false);
                } else if (visibleRow.expanded === true) {
                    focusRow(at + 1);
                }

                break;
            case 'ArrowLeft':
                if (visibleRow.expanded === true) {
                    fold(visibleRow.row.key, true);
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

    return (
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
                        fold(visibleRow.row.key, visibleRow.expanded === true);
                    }}
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
    );
}

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="px-2.75 py-2 text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
