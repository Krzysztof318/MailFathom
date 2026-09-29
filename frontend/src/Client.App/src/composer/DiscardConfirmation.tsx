// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';

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
    inFront,
    edged,
    onDiscard,
    onKeep,
}: {
    /** Whether anything has been written that closing would throw away. */
    readonly written: boolean;

    /** Whether the space the composer is written in is the one in front, which is the only time it stands on the screen. */
    readonly inFront: boolean;

    /** Whether the close is drawn with an edge, which it is where the composer is a sheet rather than a column. */
    readonly edged: boolean;

    /** Closes the composer, giving up what was written and any draft the deployment already holds for it. */
    readonly onDiscard: () => void;

    /** Files the draft in the user's own drafts folder and closes. */
    readonly onKeep: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);
    const [timesStayed, setTimesStayed] = useState(0);

    // Leaving the composer, whichever way somebody asked to: the control below, and the back gesture, which reaches
    // the same decision rather than a shorter one. A message with words in it is never given up by a gesture — what
    // back does then is put the question on the screen, exactly as pressing the control does.
    function leave(): void {
        if (written) {
            setTimesStayed((times) => times + 1);
            asked.current?.showModal();
        } else {
            onDiscard();
        }
    }

    // The composer stands where a message being read stands, and it is what the back gesture meets first while it is
    // open and its space is in front: this component is on the screen for exactly as long as the composer is, which is
    // what makes it the place that registers one.
    //
    // Two things reach it and leave it standing, and each has to record it again, which is what the count is for. A
    // press that got the question rather than the composer closing leaves it on the screen behind the question. And
    // clearing the screen leaves it alone altogether: what is being written outlives moving between the spaces, and a
    // question asked on the way out would stand in a space already set aside, where nothing can answer it. That clearing
    // still reaches it at all is the shell arriving back at this space, which registered the composer again first.
    useScreenLayer(
        inFront,
        (clearingTheScreen) => {
            if (clearingTheScreen) {
                setTimesStayed((times) => times + 1);
            } else {
                leave();
            }
        },
        timesStayed,
    );

    return (
        <>
            <SurfaceControl label={translate('compose.close')} icon="close" edged={edged} onActivate={leave} />

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
