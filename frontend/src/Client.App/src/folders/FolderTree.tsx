// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { FolderRow } from './FolderRow';
import { folderTreeOf, visibleRows, type VisibleRow } from './folderTree';

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
    const [attempt, setAttempt] = useState(0);
    const [answered, setAnswered] = useState<Answered | null>(null);
    const [focused, setFocused] = useState<string | null>(null);
    const elements = useRef(new Map<string, HTMLLIElement>());

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
    }, [session, transport, attempt, online]);

    if (!online) {
        return <Note>{translate('connection.offline')}</Note>;
    }

    const reading = answered?.attempt !== attempt;

    // A tree already drawn stays on the screen while a re-read runs, but a failure has nothing worth keeping — so a
    // read started from one says it started rather than leaving the sentence about the last attempt under the button
    // that started this one.
    if (answered === null || (reading && answered.result.outcome === 'failed')) {
        return <Note announced>{translate('folders.reading')}</Note>;
    }

    if (answered.result.outcome === 'failed') {
        const reason = answered.result.failure.reason;

        return (
            <div className="flex flex-col items-start gap-2">
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

    const directory = answered.result.value;

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
        <div className="flex flex-col gap-2">
            {/* A tree already on the screen stays on it while a re-read runs, and what is waiting is said beside it
                rather than in place of it: replacing the tree with one line would move everything under a reader's
                cursor and drop the focus of whoever asked. */}
            {reading ? <Note announced>{translate('folders.reading')}</Note> : null}

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
        </div>
    );
}

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
