// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useState, type ReactNode } from 'react';
import { useScreenLayer } from '../shell/screenLayers';

// The other shape the two surfaces opened from a message take, and the one `useOpenTabs.ts` describes as standing *in
// front of* the message rather than beside it: where somebody does not work in tabs, the sender's own markup and an
// opened file are drawn in a window over the message instead of taking the reading column. The message they were opened
// from is therefore still where it was when the window goes, which is the whole reason the design draws one.
//
// It is a `dialog` opened as a modal rather than a panel drawn to look like one, for the reason `settings/Settings.tsx`
// gives about the same choice: focus moves in and is kept, Escape leaves it, the page behind it is out of reach, and
// closing hands focus back to the control that opened it — five obligations the element already holds and a
// hand-written trap would be a second, worse implementation of.
//
// **Every way out therefore leaves through the element's own `close`.** The surface inside is handed the way to close
// this window rather than the way to clear what the workspace holds, so its close control, Escape, and the back gesture
// all arrive at one event and the platform restores focus on each of them. Taking the surface off the screen from
// underneath instead would remove an open modal from the document, which closes it and hands focus to nothing.
//
// Three compositions, and none of them is asked for in code: the design draws the window over a scrim where
// there is room for two panes, the whole screen between that width and the phone shape, and the whole screen stopping
// above the bottom navigation on a phone. All three are the width variants on the element itself.

/** Which of the two windows the design draws. It is a size and nothing else. */
export type SurfaceWindowKind = 'markup' | 'file';

// The design draws the two at two sizes rather than at one, and each stops short of the viewport by the room the
// design leaves around it — which is what the tokens carry, so this names them rather than composing a size here.
const windowSizes: Readonly<Record<SurfaceWindowKind, string>> = {
    markup: 'panes:h-markup-window-tall panes:w-markup-window',
    file: 'panes:h-file-window-tall panes:w-file-window',
};

export function SurfaceWindow({
    label,
    drawn,
    onClosed,
    children,
}: {
    /** What this window is, for a reader who meets it as a dialog before they meet what is inside it. */
    readonly label: string;

    /** Which of the two the design draws, which decides the size and nothing else. */
    readonly drawn: SurfaceWindowKind;

    /** What the window having closed comes to, which is the workspace letting go of the surface that stood in it. */
    readonly onClosed: () => void;

    /** The surface itself, handed the one way out of this window. */
    readonly children: (close: () => void) => ReactNode;
}) {
    // The element is held as state rather than in a ref because the way out of this window is handed *to* the surface
    // inside it, which happens while this component renders — and a ref is a value a render may not read. What that
    // costs is one further render as the element arrives.
    const [standing, setStanding] = useState<HTMLDialogElement | null>(null);

    // The one way out, and every other one leads here: the surface's own close control, Escape, and the back gesture
    // all arrive at the element's `close` so the platform hands focus back on each of them.
    const close = useCallback(() => {
        standing?.close();
    }, [standing]);

    // The window is opened as its element arrives rather than in an effect of this component's, because the surface
    // inside places focus at its own start in an effect of its own, and a child's effect runs before its parent's.
    // Opened any later, that focus is asked of a region inside a dialog the platform is not drawing yet — which a
    // browser refuses, so the reader would be left wherever the platform's own opening steps happened to land. A
    // window already open is never opened again: `showModal` on one throws rather than doing nothing, and StrictMode
    // hands the element over twice.
    const arrived = useCallback((dialog: HTMLDialogElement | null) => {
        if (dialog !== null && !dialog.open) {
            dialog.showModal();
        }

        setStanding(dialog);
    }, []);

    // It stands over the message, so the back gesture closes it before it navigates anywhere, and going to another
    // destination leaves it behind rather than over the screen somebody arrives at.
    useScreenLayer(true, close);

    return (
        <dialog
            ref={arrived}
            aria-label={label}
            onClose={onClosed}
            // The insets are the screen's here rather than the frame's, exactly as they are for the settings surface: a
            // modal dialog stands in the platform's own top layer, so the padding the frame carries is not around it.
            className={`fixed inset-x-0 top-0 bottom-navigation m-0 mb-safe-bottom h-auto max-h-none w-auto max-w-none overflow-hidden rounded-none border-0 bg-panel p-0 text-text open:flex open:flex-col workspace:bottom-0 workspace:mb-0 panes:inset-0 panes:m-auto panes:rounded-lg panes:border panes:border-line panes:shadow-dialog panes:backdrop:bg-scrim ${windowSizes[drawn]}`}
        >
            {children(close)}
        </dialog>
    );
}
