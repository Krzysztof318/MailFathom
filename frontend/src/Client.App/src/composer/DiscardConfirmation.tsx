// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

// Closing the composer, and the question in front of it where closing would cost something. Discarding mail somebody
// wrote is destructive, so the question names what goes and offers the way out of losing it; a message with nothing in
// it is closed without being asked about, because a confirmation for nothing is what teaches a reader to dismiss them.
//
// The question is `confirmation/Confirmation.tsx`, as every act that leaves the deployment is. Three ways out rather
// than two is what makes this one worth asking at all: the way that gives the words up and the way that keeps them are
// different acts, and offering only *cancel* and *discard* would put filing the draft behind a control that reads as
// refusing the question.
//
// The control and the question are one component for the reason `SendConfirmation.tsx` gives about the same pairing.

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

            <Confirmation
                asked={asked}
                mark="draft"
                question={translate('compose.discardQuestion')}
                consequence={
                    <p className="text-base text-muted text-pretty">{translate('compose.discardExplanation')}</p>
                }
                reversal={{ kind: 'permanent', said: translate('compose.discardIsFinal') }}
                ways={[
                    { said: translate('compose.backToEditing'), manner: 'back' },
                    { said: translate('compose.discard'), manner: 'aside', run: onDiscard },
                    { said: translate('compose.saveDraft'), manner: 'act', run: onKeep },
                ]}
            />
        </>
    );
}
