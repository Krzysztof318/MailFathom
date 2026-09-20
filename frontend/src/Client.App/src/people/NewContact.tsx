// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState, type RefObject } from 'react';
import { looksLikeAnAddress } from '../composer/composition';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';

// Writing somebody down, which is the one way this client puts a person into the book. What it records is asserted by
// the person typing it, which is not a field on this form and never could be: the surface reads the origin off who is
// writing rather than off what they send.
//
// **Two fields rather than the four the design project draws.** A name and an address are what a contact record holds
// on this deployment; a company and a role are not fields of it, so a form offering them would be asking for values no
// request could carry and no screen could ever draw back. That is a correction owed to the design rather than a pair
// of inputs to add here.
//
// **The save is refused before the request where the client can tell**, which is an empty field and text that is not
// shaped like an address at all. Whether it is shaped like one is the composer's own question and is asked with the
// composer's own function, because *has this person finished typing an address* is one question in this client rather
// than one per form. Everything else the book judges — an address another contact already holds above all — is an
// outcome the screen reports rather than a rule restated here.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

/** What a person has typed into the form, which is the whole of what a record this client writes carries. */
export interface ContactDraft {
    readonly displayName: string;
    readonly address: string;
}

export function NewContact({
    asked,
    onSave,
}: {
    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** Records what was typed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (draft: ContactDraft) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();
    const names = useId();
    const addresses = useId();

    const [draft, setDraft] = useState<ContactDraft>({ displayName: '', address: '' });

    const named = draft.displayName.trim() !== '';
    const addressed = draft.address.trim() !== '';
    const usable = !addressed || looksLikeAnAddress(draft.address.trim());
    const savable = named && addressed && usable;

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would record the person again.
                dialog.returnValue = '';

                if (answer === 'save' && draft.displayName.trim() !== '' && looksLikeAnAddress(draft.address.trim())) {
                    onSave({ displayName: draft.displayName.trim(), address: draft.address.trim() });
                }

                // The form is emptied on the way out rather than on the way in, so a dialog opened again is a blank
                // one and nothing of what was typed outlives the question it was typed into.
                setDraft({ displayName: '', address: '' });
            }}
        >
            <form
                className="flex flex-col"
                onSubmit={(submitting) => {
                    // Closed by hand rather than by `method="dialog"`, because Enter in a field submits with no button
                    // behind it — and a form closed that way answers with nothing, which would drop a save somebody
                    // made from the keyboard.
                    submitting.preventDefault();

                    if (savable) {
                        asked.current?.close('save');
                    }
                }}
            >
                <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <Icon name="person" className="size-5 shrink-0 text-muted" />

                    <h2 id={asks} className="min-w-0 flex-1 truncate text-lg font-semibold">
                        {translate('people.newContact')}
                    </h2>

                    <SurfaceControl
                        label={translate('people.closeNewContact')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={names} className={dialogFieldLabel}>
                            {translate('people.contactName')}
                        </label>

                        <input
                            id={names}
                            type="text"
                            autoFocus
                            value={draft.displayName}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, displayName: typing.target.value });
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={addresses} className={dialogFieldLabel}>
                            {translate('people.contactAddress')}
                        </label>

                        <input
                            id={addresses}
                            type="email"
                            value={draft.address}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, address: typing.target.value });
                            }}
                        />
                    </div>

                    {/* Said only once there is something to judge, because a complaint under a field nobody has
                        finished typing in is the client complaining about a state it put them in. The button is flat
                        meanwhile, which is what the design draws instead. */}
                    {usable ? null : (
                        <p role="alert" className="text-sm text-warning-text text-pretty">
                            {translate('people.addressNotUsable')}
                        </p>
                    )}

                    <p className="rounded-lg bg-accent-soft px-3 py-2.5 text-sm text-accent-deep text-pretty">
                        {translate('people.newContactHint')}
                    </p>
                </div>

                <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>

                    <button
                        type="submit"
                        disabled={!savable}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100`}
                    >
                        {translate('people.saveContact')}
                    </button>
                </div>
            </form>
        </dialog>
    );
}
