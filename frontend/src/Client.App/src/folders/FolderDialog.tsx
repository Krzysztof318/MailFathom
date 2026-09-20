// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFolderRole, ManagedMailFolder } from '@mailfathom/client-backend';
import { useId, type RefObject } from 'react';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import {
    admissibleParents,
    namePathOf,
    refusalOf,
    unchanged,
    withName,
    withParent,
    withRole,
    type FolderDraft,
    type FolderDraftRefusal,
} from './folderDraft';

// Making a folder and editing one, which the design project draws as a single dialog in two modes: the title and the
// button change, and nothing else does. It is one component for that reason — a second dialog for the edit is how a
// client comes to have two ideas of what a folder is — and it is a dialog rather than a confirmation because what it
// takes is values rather than an answer.
//
// **The two fields are the two acts behind the dialog.** The design draws a name and a second field beside it, and
// that second field used to be the folder's path on a mail server. It is not one any more and could not be: the
// managed surface owns where a folder lives and publishes no path, so what stands there is where the folder sits in
// this mailbox — which is what a move asks for. Saving therefore asks for a rename, a move, or both, and asks for
// nothing at all where neither value moved.
//
// **A folder for a role is chosen rather than named.** Where the mailbox is missing one of the folders it files by,
// the dialog offers it as a kind to create, and choosing one puts both fields away: the service supplies the standard
// name and the top of the hierarchy, so a name field there would be asking for a value the request will not carry. One
// that already plays a role keeps the second field and loses the first — what it is called is the service's, and where
// it sits is still a question somebody may answer.
//
// **The save is refused before the request rather than after it**, and the reason is said. An empty name is what the
// design draws the button flat for; a folder that would nest past the third level and a name a sibling already
// carries are each a sentence the deployment would have answered with, said here so nobody spends a round trip on it.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

const refusalSaid: Readonly<Record<FolderDraftRefusal, MessageKey>> = {
    nameEmpty: 'folders.nameEmpty',
    tooDeep: 'folders.tooDeep',
    nameTaken: 'folders.nameTaken',
    nestedInItself: 'folders.nestedInItself',
};

/** What each role is called where somebody is choosing one to create, which is the folder's own standing name. */
const roleSaid: Readonly<Partial<Record<MailFolderRole, MessageKey>>> = {
    Inbox: 'folders.role.inbox',
    Archive: 'folders.role.archive',
    Drafts: 'folders.role.drafts',
    Sent: 'folders.role.sent',
    Junk: 'folders.role.junk',
    Trash: 'folders.role.trash',
};

export function FolderDialog({
    asked,
    draft,
    folders,
    creatableRoles,
    onDraft,
    onSave,
}: {
    /** The dialog itself, held by the caller so that both acts reach the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** What is being composed, or `null` while nothing is — which is what the dialog draws nothing for. */
    readonly draft: FolderDraft | null;

    /** The mailbox's folders, which the parent field is drawn from and a name collision is judged against. */
    readonly folders: readonly ManagedMailFolder[];

    /** The roles the mailbox has no folder for, which is what it may be asked to create one for. */
    readonly creatableRoles: readonly MailFolderRole[];

    /** What one keystroke left, which the caller holds because the save is composed from it. */
    readonly onDraft: (draft: FolderDraft) => void;

    /** Saves what is composed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (draft: FolderDraft) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();
    const names = useId();
    const parents = useId();
    const kinds = useId();

    const refusal = draft === null ? 'nameEmpty' : refusalOf(draft, folders);
    const editing = draft?.mode === 'edit';

    // A save with nothing to ask for is refused for the same reason an empty name is: two requests that would change
    // nothing, and a success toast about a folder nobody touched.
    const savable = draft !== null && refusal === null && !unchanged(draft);
    const choices = draft === null ? [] : admissibleParents(draft, folders);
    const roles = editing ? [] : creatableRoles;

    // A folder asked for by role is neither named nor placed here, both being the service's. A folder that already
    // plays one is still placed — its role fixes what it is called and nothing about where it sits.
    const askedForByRole = draft !== null && !editing && draft.role !== null;
    const naming = draft !== null && draft.role === null;
    const placing = draft !== null && !askedForByRole;

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would make the folder again.
                dialog.returnValue = '';

                if (answer === 'save' && draft !== null && refusalOf(draft, folders) === null && !unchanged(draft)) {
                    onSave(draft);
                }
            }}
        >
            {draft === null ? null : (
                <form
                    className="flex flex-col"
                    onSubmit={(submitting) => {
                        // The dialog is closed by hand rather than by `method="dialog"`, because Enter in the name
                        // field submits with no button behind it — and a form closed that way answers with nothing,
                        // which would drop a save somebody made from the keyboard.
                        submitting.preventDefault();

                        if (savable) {
                            asked.current?.close('save');
                        }
                    }}
                >
                    <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                        <Icon name={editing ? 'edit' : 'create_new_folder'} className="size-5 shrink-0 text-muted" />

                        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                            <h2 id={asks} className="text-lg font-semibold">
                                {translate(editing ? 'folders.editFolderTitle' : 'folders.newFolderTitle')}
                            </h2>

                            {/* A folder asked for by role goes where the service puts it rather than inside whatever
                                row the dialog was opened on, so it says the mailbox: naming a parent it will not be
                                made in is the one line here that would be untrue by the time it is read. */}
                            <p className="truncate text-xs text-muted">
                                {draft.parentId === null || !placing
                                    ? translate('folders.inMailbox', { mailbox: draft.accountName })
                                    : translate('folders.insideFolder', {
                                          folder: namePathOf(draft.parentId, folders),
                                          mailbox: draft.accountName,
                                      })}
                            </p>
                        </div>

                        <SurfaceControl
                            label={translate('folders.close')}
                            icon="close"
                            onActivate={() => {
                                asked.current?.close();
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                        {roles.length === 0 ? null : (
                            <div className="flex flex-col gap-1.5">
                                <label htmlFor={kinds} className={dialogFieldLabel}>
                                    {translate('folders.kind')}
                                </label>

                                <select
                                    id={kinds}
                                    value={draft.role ?? ''}
                                    className={dialogField}
                                    onChange={(choosing) => {
                                        const chosen = choosing.target.value;

                                        onDraft(withRole(draft, chosen === '' ? null : (chosen as MailFolderRole)));
                                    }}
                                >
                                    <option value="">{translate('folders.kindOrdinary')}</option>

                                    {roles.map((role) => (
                                        <option key={role} value={role}>
                                            {translate(roleSaid[role] ?? 'folders.kindOrdinary')}
                                        </option>
                                    ))}
                                </select>

                                <p className="text-xs text-muted text-pretty">{translate('folders.kindHint')}</p>
                            </div>
                        )}

                        {!naming ? null : (
                            <div className="flex flex-col gap-1.5">
                                <label htmlFor={names} className={dialogFieldLabel}>
                                    {translate('folders.name')}
                                </label>

                                {/* Controlled, unlike the display name in the settings screen, because what may be
                                    saved is read off the pair on every keystroke: a name a sibling already carries has
                                    to be said while it is being typed rather than afterwards. */}
                                <input
                                    id={names}
                                    type="text"
                                    autoFocus
                                    value={draft.name}
                                    placeholder={translate('folders.namePlaceholder')}
                                    className={dialogField}
                                    onChange={(typing) => {
                                        onDraft(withName(draft, typing.target.value));
                                    }}
                                />
                            </div>
                        )}

                        {!placing ? null : (
                            <div className="flex flex-col gap-1.5">
                                <label htmlFor={parents} className={dialogFieldLabel}>
                                    {translate('folders.parent')}
                                </label>

                                <select
                                    id={parents}
                                    value={draft.parentId ?? ''}
                                    className={dialogField}
                                    onChange={(choosing) => {
                                        const chosen = choosing.target.value;

                                        onDraft(withParent(draft, chosen === '' ? null : chosen));
                                    }}
                                >
                                    <option value="">
                                        {translate('folders.parentTop', { mailbox: draft.accountName })}
                                    </option>

                                    {choices.map((folder) => (
                                        <option key={folder.id} value={folder.id}>
                                            {namePathOf(folder.id, folders)}
                                        </option>
                                    ))}
                                </select>
                            </div>
                        )}

                        {/* Said only once the name is there, because *a folder needs a name* under an empty field
                            somebody has not finished typing in is the client complaining about a state it put them
                            in. The button is flat meanwhile, which is what the design draws instead. */}
                        {refusal === null || refusal === 'nameEmpty' ? null : (
                            <p role="alert" className="text-sm text-warning-text">
                                {translate(refusalSaid[refusal])}
                            </p>
                        )}
                    </div>

                    <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                        <button type="button" className={mannerDrawn.back} onClick={() => asked.current?.close()}>
                            {translate('act.cancel')}
                        </button>

                        <button
                            type="submit"
                            disabled={!savable}
                            className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100`}
                        >
                            {translate(editing ? 'folders.save' : 'folders.create')}
                        </button>
                    </div>
                </form>
            )}
        </dialog>
    );
}
