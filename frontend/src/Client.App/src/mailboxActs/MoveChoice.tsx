// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import type { MoveDestination } from './mailboxDestinations';

// Where mail is being filed, asked as the design project draws it: the folders of the one account the messages are in,
// each named by its place on the server rather than by MailFathom's own name for it.
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
    destinations,
    onChosen,
}: {
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** The folders that may be picked, which is what the account holds. */
    readonly destinations: readonly MoveDestination[];

    /** What to do with the folder somebody picked, run once the dialog has closed and focus has been restored. */
    readonly onChosen: (destination: MoveDestination) => void;
}) {
    const { translate } = useLocalization();
    const asks = useId();

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

                const destination = picked === '' ? undefined : destinations[Number(picked)];

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

                <button
                    type="button"
                    aria-label={translate('act.moveClose')}
                    title={translate('act.moveClose')}
                    className="flex size-8 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onClick={() => {
                        asked.current?.close();
                    }}
                >
                    <Icon name="close" className="size-5" />
                </button>
            </div>

            <ul className="flex max-h-96 flex-col overflow-y-auto py-1.5">
                {destinations.map((destination, pressed) => (
                    <li key={destination.alias}>
                        <button
                            type="button"
                            className="flex w-full items-center gap-2.5 px-4 py-2.25 text-start text-base text-text-soft transition hover:bg-hover hover:text-text"
                            onClick={() => {
                                asked.current?.close(String(pressed));
                            }}
                        >
                            <Icon name="folder" className="size-4.5 shrink-0 text-muted" />
                            <span className="truncate">{destination.name}</span>
                        </button>
                    </li>
                ))}
            </ul>
        </dialog>
    );
}
