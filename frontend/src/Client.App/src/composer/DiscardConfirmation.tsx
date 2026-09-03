// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useRef } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

// Closing the composer, and the question in front of it where closing would cost something. Discarding mail somebody
// wrote is destructive, so the question names what goes and offers the way out of losing it; a message with nothing in
// it is closed without being asked about, because a confirmation for nothing is what teaches a reader to dismiss them.
//
// The control and the question are one component for the reason `SendConfirmation.tsx` gives about the same pairing.
const wordsGo = 'discard';
const wordsKept = 'keep';

export function DiscardConfirmation({
    written,
    onDiscard,
    onKeep,
}: {
    /** Whether anything has been written that closing would throw away. */
    readonly written: boolean;

    /** Closes the composer, giving up what was written and any draft the deployment already holds for it. */
    readonly onDiscard: () => void;

    /** Files the draft in the owner's own drafts folder and closes. */
    readonly onKeep: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);
    const question = useId();

    return (
        <>
            <button
                type="button"
                aria-label={translate('compose.close')}
                title={translate('compose.close')}
                className="flex size-7 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                onClick={() => {
                    if (written) {
                        asked.current?.showModal();
                    } else {
                        onDiscard();
                    }
                }}
            >
                <Icon name="close" className="size-4.5" />
            </button>

            <dialog
                ref={asked}
                aria-labelledby={question}
                className="m-auto w-96 max-w-full rounded-3xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
                onClose={() => {
                    if (asked.current?.returnValue === wordsGo) {
                        onDiscard();
                    }

                    if (asked.current?.returnValue === wordsKept) {
                        onKeep();
                    }
                }}
            >
                <div className="flex flex-col gap-3.5">
                    <h2 id={question} className="text-xl font-semibold">
                        {translate('compose.discardQuestion')}
                    </h2>

                    <p className="text-base text-muted text-pretty">{translate('compose.discardExplanation')}</p>

                    <div className="flex flex-wrap justify-end gap-2">
                        <button
                            type="button"
                            className="rounded-lg px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                asked.current?.close();
                            }}
                        >
                            {translate('compose.backToEditing')}
                        </button>

                        <button
                            type="button"
                            className="rounded-lg border border-warning px-3.75 py-2 text-base text-warning-text transition hover:bg-warning-soft"
                            onClick={() => {
                                asked.current?.close(wordsGo);
                            }}
                        >
                            {translate('compose.discard')}
                        </button>

                        <button
                            type="button"
                            className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                            onClick={() => {
                                asked.current?.close(wordsKept);
                            }}
                        >
                            {translate('compose.saveDraft')}
                        </button>
                    </div>
                </div>
            </dialog>
        </>
    );
}
