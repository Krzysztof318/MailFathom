// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useRef } from 'react';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { useLocalization } from '../localization/useLocalization';

// The one way out of a blocking overlay, and the question in front of it. The control and the question are one
// component for the reason `DiscardConfirmation.tsx` gives about the same pairing: the dialog is the platform's own, so
// whether it is open is the element's state rather than a second copy of it, and leaving it either way puts focus back
// on the control that opened it without anything here remembering which.
//
// It asks rather than acts because stopping is what a stray press on this control would otherwise do, and the operation
// behind it is one whose whole reason for blocking the client is that being interrupted halfway costs something. The
// question is therefore not a formality: it is where the person is told what stopping leaves behind, in the operation's
// own words, at the only moment they can still decide against it.
//
// It opens over a dialog that is already open, which the platform's top layer stacks rather than replaces — so the
// operation stays on the screen behind the question about stopping it. Escape leaves *this* dialog, and that is
// deliberate: leaving the question is continuing the operation, which is the safe answer, while the overlay underneath
// refuses Escape because leaving it would be stopping.
const operationStops = 'stop-the-operation';

export function StopConfirmation({
    leavesBehind,
    onStop,
}: {
    /** What stopping would leave behind, in the operation's own words. */
    readonly leavesBehind: string;

    /** Stops the operation. Called once the person has confirmed and not before. */
    readonly onStop: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);
    const question = useId();

    return (
        <>
            <button
                type="button"
                className="mt-1.25 flex h-9.5 items-center justify-center rounded-xl border border-line-strong px-5 text-base font-semibold text-text-soft transition hover:bg-hover hover:text-text"
                onClick={() => {
                    if (asked.current !== null) {
                        // A `close()` given no answer leaves the previous one in place, and the close Escape performs
                        // is one of those — so the answer is cleared where the question is asked rather than at each
                        // way out of it, and only an explicit press on the stopping control can ever read as one.
                        asked.current.returnValue = '';
                        asked.current.showModal();
                    }
                }}
            >
                {translate('blocking.cancel')}
            </button>

            <dialog
                ref={asked}
                aria-labelledby={question}
                className="m-auto w-95 max-w-full rounded-3xl border border-line bg-panel px-5.5 py-5 text-text shadow-dialog backdrop:bg-scrim"
                onClose={() => {
                    if (asked.current?.returnValue === operationStops) {
                        onStop();
                    }
                }}
            >
                <div className="flex flex-col gap-3.25">
                    <h2 id={question} className="text-xl font-semibold">
                        {translate('blocking.stopQuestion')}
                    </h2>

                    <p className="text-base text-muted text-pretty">{leavesBehind}</p>

                    <div className="flex flex-wrap justify-end gap-2.25">
                        <button
                            type="button"
                            className="rounded-lg border border-line-strong px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                asked.current?.close();
                            }}
                        >
                            {translate('blocking.continue')}
                        </button>

                        <button
                            type="button"
                            className={mannerDrawn.destroy}
                            onClick={() => {
                                asked.current?.close(operationStops);
                            }}
                        >
                            {translate('blocking.stop')}
                        </button>
                    </div>
                </div>
            </dialog>
        </>
    );
}
