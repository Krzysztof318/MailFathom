// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    createManagedMailFolder,
    deleteManagedMailFolder,
    moveManagedMailFolder,
    renameManagedMailFolder,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type ManagedMailFolderChange,
    type ManagedMailFolderOutcome,
    type ManagedMailFolderRefusal,
} from '@mailfathom/client-backend';
import { useRef, useState, type ReactNode } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useToasts, type OperationSettled } from '../toasts/useToasts';
import { folderRoleLabels } from '../workspace/mailScope';
import { FolderDialog } from './FolderDialog';
import { draftForEditedFolder, draftForNewFolder, folderNameOf, type FolderDraft } from './folderDraft';
import { markEverythingRead } from './markingEverythingRead';
import {
    FolderMaintenanceContext,
    noFolderMaintenance,
    type FolderMailbox,
    type FolderMaintenance,
    type RemovedFolder,
} from './useFolderMaintenance';

// Performing the folder acts, which is the one place in this client that changes a mailbox's folders. It owns the
// dialog and the question because both are opened from two unrelated surfaces — the folder column's row menus and
// *New folder here* in the sheet that files mail — and a dialog mounted beside each of them would be two dialogs to
// keep in step.
//
// **Every act changes the mailbox, and what that meant is the service's answer rather than this client's guess.** A
// deletion is the case that decides the shape of everything here: it may move the folder into the trash, erase it
// outright, delete it on a mail server, or mark it deleted while the server keeps it — so what a person is told
// afterwards is read from the change the service reported, and nothing is said about the mail before the act is over.
// A client that composed that sentence in advance would be telling somebody their mail is gone when it is not, or the
// reverse.
//
// **The tree is read again rather than corrected here.** No act patches the column: the change committed against a
// mailbox this client does not hold a copy of, so a row drawn from what was asked for would be this client's opinion
// of what happened. That is also what makes a refused act leave the tree exactly as it was.
//
// **Renaming and moving are one dialog and up to two requests**, because the surface takes them as two acts and the
// design draws one dialog with two fields. Where both moved, the rename is asked for first; a rename that committed
// and a move that was then refused is its own sentence rather than either of the two, since the folder did change and
// not in the way somebody asked for.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// What each refusal is said as. Several share a sentence, and they share it because the remedy is the same one: an
// account, a folder, or a parent the deployment does not have is a tree read minutes ago, and reading it again is what
// to do about all three. The rest are apart because each names something different to do.
const refusalSaid: Readonly<Record<ManagedMailFolderRefusal, MessageKey>> = {
    accountMissing: 'folders.refusal.gone',
    folderMissing: 'folders.refusal.gone',
    parentMissing: 'folders.refusal.gone',
    accountRestoring: 'folders.refusal.restoring',
    protectedRole: 'folders.refusal.protectedRole',
    nameInvalid: 'folders.refusal.nameInvalid',
    inboxNameAtTopLevel: 'folders.refusal.inboxName',
    nameTaken: 'folders.nameTaken',
    nestedInItself: 'folders.nestedInItself',
    tooDeep: 'folders.tooDeep',
    tooManyFolders: 'folders.refusal.tooManyFolders',
    serverRefused: 'folders.refusal.serverRefused',
    serverUnavailable: 'folders.refusal.serverUnavailable',
    roleAlreadyPlayed: 'folders.refusal.roleAlreadyPlayed',
    notDeclaredByTheAccount: 'folders.refusal.notYours',
    notRecorded: 'folders.refusal.notRecorded',
    refusedForAnotherReason: 'folders.refusal.another',
};

// What each change is said as, which is the whole of what somebody is told about their mail. The four deletions are
// four sentences for that reason and are never collapsed into one.
const changeSaid: Readonly<Record<ManagedMailFolderChange, MessageKey>> = {
    created: 'folders.created',
    renamed: 'folders.renamed',
    moved: 'folders.moved',
    movedToTrash: 'folders.movedToTrash',
    erased: 'folders.erased',
    deleted: 'folders.deleted',
    markedDeleted: 'folders.markedDeleted',
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

/** Whether one act reached the mailbox, which is what says a second act may be chained behind it. */
function committed(answer: ClientResult<ManagedMailFolderOutcome>): boolean {
    return answer.outcome === 'read' && answer.value.committed;
}

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

    /** Whether this credential may change the mailbox's folders, which is what draws four of the five acts. */
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
    const [removing, setRemoving] = useState<{
        readonly mailbox: FolderMailbox;
        readonly folder: RemovedFolder;
    } | null>(null);
    const [changed, setChanged] = useState(0);

    function open(mailbox: FolderMailbox, draft: FolderDraft): void {
        setComposing({ mailbox, draft });
        dialog.current?.showModal();
    }

    /** Raises the toast an act stands under while it runs, which every act here is reported through. */
    function standing(said: MessageKey, about: Readonly<Record<string, string>>): OperationSettled {
        return toasts.raiseOperation({
            title: translate(said, about),

            // Nothing to stop: one request is in flight and it is durable the moment the deployment answers, so
            // closing the toast is letting go of the report rather than taking the act back.
            stoppingLeavesBehind: translate('folders.actStopped'),
            stop: () => undefined,
        });
    }

    /** Reports what one act came to, in the three sentences it can come to rather than in one. */
    function report(
        answer: ClientResult<ManagedMailFolderOutcome>,
        settle: OperationSettled,
        about: Readonly<Record<string, string>>,
    ): void {
        if (answer.outcome === 'failed') {
            settle({
                kind: 'error',
                title: translate('folders.actFailed', {
                    reason: translate(failureLabels[answer.failure.reason]),
                }),
            });

            return;
        }

        if (!answer.value.committed) {
            settle({
                kind: 'warning',
                title: translate('folders.actRefused', { reason: translate(refusalSaid[answer.value.refusal]) }),
            });

            return;
        }

        const { change, folder, mailErasureDeferred } = answer.value;

        settle({
            kind: 'success',
            title: translate(changeSaid[change], { ...about, folder: folder.name }),
        });

        // A second card rather than a longer sentence, because it is about the mail rather than about the folder: the
        // folder is gone and what it held is stored and out of every listing until the account's next erasure.
        if (mailErasureDeferred) {
            toasts.raise({ kind: 'warning', title: translate('folders.erasureDeferred', { folder: folder.name }) });
        }

        setChanged((made) => made + 1);
    }

    async function save({ mailbox, draft }: Composing): Promise<void> {
        if (session === null) {
            return;
        }

        const name = folderNameOf(draft);
        const about = { folder: name, mailbox: mailbox.accountName };

        if (draft.standingId === null) {
            // A creation for a role carries no name, the service giving it one, so the card standing while it runs
            // says what was asked for — *Creating Trash in Work…* — rather than leaving the subject of the sentence
            // out. What it settles to is the name the service answered with, which `report` fills in.
            const settle = standing('folders.creating', {
                folder: draft.role === null ? name : translate(folderRoleLabels[draft.role]),
                mailbox: mailbox.accountName,
            });

            report(
                await createManagedMailFolder(
                    session,
                    transport,
                    draft.role === null
                        ? { account: mailbox.accountId, parentId: draft.parentId, name }
                        : { account: mailbox.accountId, role: draft.role },
                ),
                settle,
                about,
            );

            return;
        }

        const folderId = draft.standingId;
        const renaming = name !== draft.standingName;
        const moving = draft.parentId !== draft.standingParentId;

        // Asked before the toast is raised rather than after it: a save with neither value moved has nothing to put
        // on the wire, and a card raised for it would stand over the screen with nothing coming to settle it.
        if (!renaming && !moving) {
            return;
        }

        const settle = standing('folders.saving', { folder: draft.standingName, mailbox: mailbox.accountName });

        if (renaming) {
            const renamed = await renameManagedMailFolder(session, transport, {
                account: mailbox.accountId,
                folderId,
                name,
            });

            // The toast is settled here only where the rename is the whole of the save. Where a move follows it, the
            // one card stands until that move has answered, because two outcomes reported as two cards for one press
            // is the client narrating its own requests rather than telling somebody what happened.
            if (!moving || !committed(renamed)) {
                report(renamed, settle, about);

                return;
            }

            setChanged((made) => made + 1);
        }

        const moved = await moveManagedMailFolder(session, transport, {
            account: mailbox.accountId,
            folderId,
            parentId: draft.parentId,
        });

        // A rename that committed and a move that did not is neither of the two sentences `report` has, so it is said
        // here: the folder did change, and not in the way somebody asked for.
        if (renaming && !committed(moved)) {
            settle({ kind: 'warning', title: translate('folders.renamedNotMoved', about) });

            return;
        }

        report(moved, settle, about);
    }

    async function remove(mailbox: FolderMailbox, folder: RemovedFolder): Promise<void> {
        if (session === null) {
            return;
        }

        const settle = standing('folders.removing', { folder: folder.name, mailbox: mailbox.accountName });

        report(
            await deleteManagedMailFolder(session, transport, {
                account: mailbox.accountId,
                folderId: folder.id,
            }),
            settle,
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
                  revise: (mailbox, folder) => {
                      open(mailbox, draftForEditedFolder(mailbox.accountId, mailbox.accountName, folder));
                  },
                  remove: (mailbox, folder) => {
                      setRemoving({ mailbox, folder });
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
                folders={composing?.mailbox.folders ?? []}
                creatableRoles={composing?.mailbox.creatableRoles ?? []}
                onDraft={(draft) => {
                    setComposing((open) => (open === null ? null : { ...open, draft }));
                }}
                onSave={(draft) => {
                    if (composing !== null) {
                        void save({ mailbox: composing.mailbox, draft });
                    }
                }}
            />

            <Confirmation
                asked={question}
                mark="delete"
                question={translate('folders.deleteQuestion', { folder: removing?.folder.name ?? '' })}
                consequence={
                    <p className="text-base text-text-soft text-pretty">
                        {translate('folders.deleteConsequence', {
                            folder: removing?.folder.name ?? '',
                            mailbox: removing?.mailbox.accountName ?? '',
                        })}
                    </p>
                }
                cautions={removing?.folder.holdsNested === true ? [translate('folders.deleteTakesNested')] : []}
                // What a deletion does to the mail is the account's own answer and is not known until it has been
                // asked, so the question states the strongest of the four rather than guessing which one applies —
                // and the toast afterwards says which of them it actually was.
                reversal={{ kind: 'permanent', said: translate('folders.deleteMayEraseMail') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('folders.deleteFolder'),
                        manner: 'destroy',
                        run: () => {
                            if (removing !== null) {
                                void remove(removing.mailbox, removing.folder);
                            }
                        },
                    },
                ]}
            />
        </FolderMaintenanceContext>
    );
}
