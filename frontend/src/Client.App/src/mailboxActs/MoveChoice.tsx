// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import { MailboxMark } from '../controls/MailboxMark';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { folderRoleIcons } from '../workspace/mailScope';
import { destinationName, type MoveDestination, type MoveDestinationGroup } from './mailboxDestinations';

// Where mail is being filed, asked as the design project draws it: the folders grouped under the mailbox they belong
// to, each group headed by that mailbox's name and its colour, and each folder named and drawn the way the folder
// column names and draws it — by the role it plays where it plays one, and by its place on the server otherwise.
//
// The grouping is what a reader needs first, and it is a statement rather than a list header: filing is an act inside
// one mailbox, so saying which one is saying what will happen. Exactly one group is offered today, because a message
// moves between folders of its own account and nowhere else and a selection spanning two accounts is refused before
// this dialog is reached; `mailboxDestinations.ts` is where that is decided.
//
// It is a choice rather than a confirmation, which is why it is not `Confirmation`: nothing here states a consequence
// or offers a way back, because picking a folder *is* the act and the toast that follows is where taking it back is
// offered. Filing mail is reversible, so the design puts no question in front of it.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

export function MoveChoice({
    asked,
    groups,
    onChosen,
}: {
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** The folders that may be picked, under the account each belongs to, which is what the user holds. */
    readonly groups: readonly MoveDestinationGroup[];

    /** What to do with the folder somebody picked, run once the dialog has closed and focus has been restored. */
    readonly onChosen: (destination: MoveDestination) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();

    // What a press answers with is a position in this one list, so the groups are flattened once here rather than the
    // press composing an identity out of two indices. An alias would not do: it names a folder inside its own account
    // and the type admits more than one account, so two groups could answer with the same word.
    const offered = groups.flatMap((group) => group.destinations);

    return (
        <dialog
            ref={asked}
            aria-labelledby={asks}
            className="m-auto w-96 max-w-full rounded-2xl border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const picked = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would file the mail again.
                dialog.returnValue = '';

                const destination = picked === '' ? undefined : offered[Number(picked)];

                if (destination !== undefined) {
                    onChosen(destination);
                }
            }}
        >
            <div className="flex items-center gap-2.5 border-b border-line px-4 py-3">
                <Icon name="drive_file_move" className="size-5 shrink-0 text-accent-strong" />

                <h2 id={asks} className="flex-1 text-base font-semibold">
                    {translate('act.moveTitle')}
                </h2>

                <SurfaceControl
                    label={translate('act.moveClose')}
                    icon="close"
                    onActivate={() => {
                        asked.current?.close();
                    }}
                />
            </div>

            <div className="flex max-h-96 flex-col overflow-y-auto py-1.5">
                {groups.map((group) => (
                    <section key={group.accountId} aria-label={group.accountName}>
                        <p className="flex items-center gap-2 px-4 pt-2 pb-1 text-2xs tracking-widest text-faint uppercase">
                            <MailboxMark ordinal={group.ordinal} />
                            <span className="min-w-0 flex-1 truncate">{group.accountName}</span>
                        </p>

                        <ul className="flex flex-col">
                            {group.destinations.map((destination) => (
                                <li key={destination.alias}>
                                    <button
                                        type="button"
                                        className="flex w-full items-center gap-2.5 px-4 py-2.25 text-start text-base text-text-soft transition hover:bg-hover hover:text-text"
                                        onClick={() => {
                                            asked.current?.close(String(offered.indexOf(destination)));
                                        }}
                                    >
                                        <Icon
                                            name={
                                                destination.role === null ? 'folder' : folderRoleIcons[destination.role]
                                            }
                                            className="size-4.5 shrink-0 text-muted"
                                        />

                                        <span className="truncate">{destinationName(destination, translate)}</span>
                                    </button>
                                </li>
                            ))}
                        </ul>
                    </section>
                ))}
            </div>
        </dialog>
    );
}
