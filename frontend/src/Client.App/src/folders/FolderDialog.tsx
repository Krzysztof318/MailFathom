// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type RefObject } from 'react';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import {
    refusalOf,
    remotePathBase,
    withName,
    withRemotePath,
    type FolderDraft,
    type FolderDraftRefusal,
} from './folderDraft';

// Making a folder and editing one, which the design project draws as a single dialog in two modes: the title and the
// button change, and nothing else does. It is one component for that reason — a second dialog for the edit is how a
// client comes to have two ideas of what a folder is — and it is a dialog rather than a confirmation because what it
// takes is two values rather than an answer.
//
// **The two fields are two different things**, which is the whole reason the design draws both. The name is what the
// folder is called here, and the alias every route on the client surface names it by is that name under its parent's;
// the remote path is where the folder sits on somebody's mail server, which a provider, a delimiter and a language
// each get a say in. `folderDraft.ts` holds the arithmetic over the pair, including the rule that the path follows
// the name until somebody types one in.
//
// **The save is refused before the request rather than after it**, and the reason is said. An empty name is what the
// design draws the button flat for; a name that would nest past the third level and a name the mailbox already has
// are each a sentence the deployment would have answered with, said here so nobody spends a round trip on it.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

const refusalSaid: Readonly<Record<FolderDraftRefusal, MessageKey>> = {
    nameEmpty: 'folders.nameEmpty',
    tooDeep: 'folders.tooDeep',
    aliasTaken: 'folders.aliasTaken',
    remotePathEmpty: 'folders.remotePathEmpty',
};

const field =
    'w-full rounded-lg border border-line-strong bg-sunken px-2.75 py-2 text-base text-text outline-none focus:border-accent';

const fieldLabel = 'text-2xs tracking-widest text-muted uppercase';

export function FolderDialog({
    asked,
    draft,
    declaredAliases,
    onDraft,
    onSave,
}: {
    /** The dialog itself, held by the caller so that both acts reach the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** What is being composed, or `null` while nothing is — which is what the dialog draws nothing for. */
    readonly draft: FolderDraft | null;

    /** Every alias the mailbox already declares, which is what a name collision is judged against. */
    readonly declaredAliases: readonly string[];

    /** What one keystroke left, which the caller holds because the save is composed from it. */
    readonly onDraft: (draft: FolderDraft) => void;

    /** Saves what is composed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (draft: FolderDraft) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();
    const names = useId();
    const paths = useId();
    const explains = useId();

    const refusal = draft === null ? 'nameEmpty' : refusalOf(draft, declaredAliases);
    const editing = draft?.mode === 'edit';

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would declare the folder again.
                dialog.returnValue = '';

                if (answer === 'save' && draft !== null && refusalOf(draft, declaredAliases) === null) {
                    onSave(draft);
                }
            }}
        >
            {draft === null ? null : (
                <form
                    className="flex flex-col"
                    onSubmit={(submitting) => {
                        // The dialog is closed by hand rather than by `method="dialog"`, because Enter in either
                        // field submits with no button behind it — and a form closed that way answers with nothing,
                        // which would drop a save somebody made from the keyboard.
                        submitting.preventDefault();

                        if (refusal === null) {
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

                            <p className="truncate text-xs text-muted">
                                {draft.parent === null
                                    ? translate('folders.inMailbox', { mailbox: draft.accountName })
                                    : translate('folders.insideFolder', {
                                          folder: draft.parent.alias,
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
                        <div className="flex flex-col gap-1.5">
                            <label htmlFor={names} className={fieldLabel}>
                                {translate('folders.name')}
                            </label>

                            {/* Controlled, unlike the display name in the settings screen, because the field beside
                                it follows what is typed here: the proposal is composed from the name on every
                                keystroke, so the name has to be a value rather than something only the DOM holds. */}
                            <input
                                id={names}
                                type="text"
                                autoFocus
                                value={draft.name}
                                placeholder={translate('folders.namePlaceholder')}
                                className={field}
                                onChange={(typing) => {
                                    onDraft(withName(draft, typing.target.value));
                                }}
                            />
                        </div>

                        <div className="flex flex-col gap-1.5">
                            <label htmlFor={paths} className={fieldLabel}>
                                {translate('folders.remotePath')}
                            </label>

                            <input
                                id={paths}
                                type="text"
                                value={draft.remotePath}
                                placeholder={translate('folders.remotePathPlaceholder', {
                                    base: remotePathBase(draft),
                                })}
                                aria-describedby={explains}
                                className={field}
                                onChange={(typing) => {
                                    onDraft(withRemotePath(draft, typing.target.value));
                                }}
                            />

                            <p id={explains} className="text-xs text-muted text-pretty">
                                {translate('folders.remotePathHint')}
                            </p>
                        </div>

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
                            disabled={refusal !== null}
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
