// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type ReactNode } from 'react';
import {
    declareMailFolder,
    readFolderConfigurationVersion,
    replaceMailFolder,
    withdrawMailFolder,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderConfigurationOutcome,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import { FolderDialog } from './FolderDialog';
import { draftAlias, draftForEditedFolder, draftForNewFolder, draftRemotePath, type FolderDraft } from './folderDraft';
import { markEverythingRead } from './markingEverythingRead';
import {
    FolderMaintenanceContext,
    noFolderMaintenance,
    type FolderMailbox,
    type FolderMaintenance,
    type WithdrawnFolder,
} from './useFolderMaintenance';

// Performing the folder acts, which is the one place in this client that changes which folders the deployment reads.
// It owns the dialog and the question because both are opened from two unrelated surfaces — the folder column's row
// menus and *New folder here* in the sheet that files mail — and a dialog mounted beside each of them would be two
// dialogs to keep in step.
//
// **Nothing here reaches a mail server.** Each act writes the person's own record: a declaration is a mapping the
// account's next run resolves and is permitted to create, a replacement states that mapping afresh, and a withdrawal
// takes the mapping out and leaves the mail already stored and the folder on the server exactly where they are. ADR
// 0007 refuses renaming and deleting a folder on somebody's server outright, which is why the question in front of a
// removal says what it says rather than promising the folder will go.
//
// **Every write states the version it was composed over**, which is what stops two clients from overwriting each
// other. The version is read immediately before the write rather than held, because a record read at sign-in is a
// record several minutes stale by the time somebody opens a menu.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// How many messages a partial pass marked, in each of the forms a language has for the noun. Selected rather than
// spelled, exactly as `mailboxActs/MailboxActs.tsx` selects one: Polish needs three forms and English hides that it
// needs two, and a ceiling somebody raises to one page would otherwise read as “The first 1 unread messages”.
const markedCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'folders.markedReadInPart.other',
    one: 'folders.markedReadInPart.one',
    two: 'folders.markedReadInPart.other',
    few: 'folders.markedReadInPart.few',
    many: 'folders.markedReadInPart.many',
    other: 'folders.markedReadInPart.other',
};

/** What the dialog is open over: the draft being composed, and the mailbox it is being composed inside. */
interface Composing {
    readonly mailbox: FolderMailbox;
    readonly draft: FolderDraft;
}

export function FolderMaintenanceProvider({
    session,
    transport,
    offered,
    marksRead,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to act for. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    /** Whether this credential may change what the deployment reads, which is what draws four of the five acts. */
    readonly offered: boolean;

    /** Whether it may write the read flag, which is the grant the fifth is reached under. */
    readonly marksRead: boolean;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const dialog = useRef<HTMLDialogElement>(null);
    const question = useRef<HTMLDialogElement>(null);
    const [composing, setComposing] = useState<Composing | null>(null);
    const [withdrawing, setWithdrawing] = useState<{
        readonly mailbox: FolderMailbox;
        readonly folder: WithdrawnFolder;
    } | null>(null);
    const [changed, setChanged] = useState(0);

    function open(mailbox: FolderMailbox, draft: FolderDraft): void {
        setComposing({ mailbox, draft });
        dialog.current?.showModal();
    }

    /** Reports what a write to the record came to, in the three sentences it can come to rather than in one. */
    function report(
        answer: ClientResult<MailFolderConfigurationOutcome>,
        said: MessageKey,
        about: Readonly<Record<string, string>>,
    ): void {
        if (answer.outcome === 'failed') {
            toasts.raise({
                kind: 'error',
                title: translate('folders.writeFailed', {
                    reason: translate(failureLabels[answer.failure.reason]),
                }),
            });

            return;
        }

        if (!answer.value.committed) {
            toasts.raise({
                kind: 'warning',
                title: translate('folders.writeRefused', { reason: answer.value.messages.join(' ') }),
            });

            return;
        }

        // The tree is read again rather than corrected here: what a mapping resolves to is the deployment's answer,
        // and a column drawn from what this client asked for would draw a folder whose server path had been refused.
        setChanged((made) => made + 1);
        toasts.raise({ kind: 'success', title: translate(said, about) });
    }

    /** Reads the version the record stands at and writes over it, which is the shape all three writes share. */
    async function overVersion(
        asking: ClientSession,
        write: (version: number) => Promise<ClientResult<MailFolderConfigurationOutcome>>,
        said: MessageKey,
        about: Readonly<Record<string, string>>,
    ): Promise<void> {
        const version = await readFolderConfigurationVersion(asking, transport);

        if (version.outcome === 'failed') {
            toasts.raise({
                kind: 'error',
                title: translate('folders.writeFailed', {
                    reason: translate(failureLabels[version.failure.reason]),
                }),
            });

            return;
        }

        report(await write(version.value), said, about);
    }

    function save({ mailbox, draft }: Composing): void {
        if (session === null) {
            return;
        }

        const alias = draftAlias(draft);
        const remotePath = draftRemotePath(draft);
        const about = { folder: remotePath.join('/'), mailbox: mailbox.accountName };
        const standing = draft.standingAlias;

        void overVersion(
            session,
            (version) =>
                standing === null
                    ? declareMailFolder(session, transport, {
                          accountId: mailbox.accountId,
                          folder: { alias, remotePath },
                          version,
                      })
                    : replaceMailFolder(session, transport, {
                          accountId: mailbox.accountId,
                          alias: standing,
                          folder: { alias, remotePath },
                          version,
                      }),
            standing === null ? 'folders.created' : 'folders.renamed',
            about,
        );
    }

    function withdrawn(mailbox: FolderMailbox, folder: WithdrawnFolder): void {
        if (session === null) {
            return;
        }

        void overVersion(
            session,
            (version) =>
                withdrawMailFolder(session, transport, {
                    accountId: mailbox.accountId,
                    alias: folder.alias,
                    version,
                }),
            'folders.removed',
            { folder: folder.name, mailbox: mailbox.accountName },
        );
    }

    function markAllRead(accountId: string, folder: string | null, said: string): void {
        if (session === null) {
            return;
        }

        const settle = toasts.raiseOperation({
            title: translate('folders.markingRead', { folder: said }),
            stoppingLeavesBehind: translate('folders.markingReadStopped'),

            // Nothing to stop: what is in flight is a bounded run of reads and one flag mutation per batch, each of
            // which is durable the moment the deployment answers. Closing the toast is therefore letting go of the
            // report rather than taking anything back, which is what the sentence above says.
            stop: () => undefined,
        });

        void markEverythingRead(session, transport, { account: accountId, folder }).then((outcome) => {
            if (outcome.failure !== null) {
                settle({
                    kind: 'error',
                    title: translate('folders.markReadFailed', {
                        folder: said,
                        reason: translate(failureLabels[outcome.failure]),
                    }),
                });

                return;
            }

            if (outcome.marked === 0) {
                settle({ kind: 'neutral', title: translate('folders.nothingUnread', { folder: said }) });

                return;
            }

            // ponytail: the tree's own counts correct when the deployment's convergence pass reports the flag, which
            // is minutes rather than milliseconds. Routing this through `readMarking/` is the upgrade, and it needs
            // the reader's mark-on-open preference separated from the grant that gate reads today.
            settle(
                outcome.leftBehind
                    ? {
                          kind: 'warning',
                          title: translate(markedCounted[new Intl.PluralRules(locale).select(outcome.marked)], {
                              folder: said,
                              count: new Intl.NumberFormat(locale).format(outcome.marked),
                          }),
                      }
                    : { kind: 'success', title: translate('folders.allRead', { folder: said }) },
            );
        });
    }

    const maintenance: FolderMaintenance =
        session === null || !(offered || marksRead)
            ? noFolderMaintenance
            : {
                  offered,
                  marksRead,
                  changed,
                  declare: (mailbox, parent) => {
                      open(mailbox, draftForNewFolder(mailbox.accountId, mailbox.accountName, parent));
                  },
                  revise: (mailbox, folder, parent) => {
                      open(mailbox, draftForEditedFolder(mailbox.accountId, mailbox.accountName, folder, parent));
                  },
                  withdraw: (mailbox, folder) => {
                      setWithdrawing({ mailbox, folder });
                      question.current?.showModal();
                  },
                  markAllRead,
              };

    return (
        <FolderMaintenanceContext value={maintenance}>
            {children}

            {/* Mounted whether or not they are open, which is what lets the platform's own dialog travel on and off
                the screen rather than appearing and disappearing — and what keeps the reference the acts above close
                and open pointing at one element. */}
            <FolderDialog
                asked={dialog}
                draft={composing?.draft ?? null}
                declaredAliases={composing?.mailbox.declaredAliases ?? []}
                onDraft={(draft) => {
                    setComposing((open) => (open === null ? null : { ...open, draft }));
                }}
                onSave={(draft) => {
                    if (composing !== null) {
                        save({ mailbox: composing.mailbox, draft });
                    }
                }}
            />

            <Confirmation
                asked={question}
                mark="delete"
                question={translate('folders.deleteQuestion', { folder: withdrawing?.folder.name ?? '' })}
                consequence={
                    <p className="text-base text-text-soft text-pretty">
                        {translate('folders.deleteConsequence', {
                            folder: withdrawing?.folder.name ?? '',
                            mailbox: withdrawing?.mailbox.accountName ?? '',
                        })}
                    </p>
                }
                cautions={withdrawing?.folder.holdsNested === true ? [translate('folders.deleteKeepsNested')] : []}
                // Permanent as far as the record is concerned — nothing puts the mapping back but declaring it again —
                // and what that costs is the one thing a reader is most likely to be wrong about, so it is what the
                // sentence says: the mail and the server-side folder both stay where they are.
                reversal={{ kind: 'permanent', said: translate('folders.deleteKeepsMail') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('folders.deleteFolder'),
                        manner: 'destroy',
                        run: () => {
                            if (withdrawing !== null) {
                                withdrawn(withdrawing.mailbox, withdrawing.folder);
                            }
                        },
                    },
                ]}
            />
        </FolderMaintenanceContext>
    );
}
