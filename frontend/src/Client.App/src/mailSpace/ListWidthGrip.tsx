// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, type KeyboardEvent, type PointerEvent } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { listWidthStep, narrowestList, startingListWidth, widestList } from './listWidth';

// The grip on the boundary between the message list and the reading pane, which is what lets a reader give either side
// more room. It draws the line the design project puts there and it is the control on it — those are one element
// rather than two, because a boundary somebody can move is a boundary they have to be able to point at.
//
// It is a separator that takes focus, which is what ARIA calls a window splitter: the position it reports is the width
// of the pane before it, so a reader who cannot see the columns still knows where the boundary stands and how far it
// may go. Everything it can be done with a pointer it can be done with the keyboard — the arrows move it in steps, and
// `Home` returns it to the width it started at, which is what a double-click does for a mouse.
//
// One pointer path rather than one per input, because pointer events are what a mouse, a finger, and a pen all arrive
// as. Capturing the pointer is what keeps a drag with it once it has left the five pixels the grip is drawn at, and
// `touch-none` is what keeps a finger dragging the boundary from scrolling the list underneath it instead.

export function ListWidthGrip({
    width,
    onWidth,
    onChosen,
}: {
    /** How wide the list is drawn right now, which is the position this reports and the width a drag starts from. */
    readonly width: number;

    /** The width to draw while the boundary is being moved. */
    readonly onWidth: (width: number) => void;

    /** The width somebody settled on, which is the one worth keeping. */
    readonly onChosen: (width: number) => void;
}) {
    const { translate } = useLocalization();

    // What a drag is: the pointer that started it, and where it and the boundary stood then. A ref rather than state,
    // because nothing on the screen is drawn from it and a moving pointer would otherwise render for every pixel of
    // its own bookkeeping.
    const dragging = useRef<{ pointer: number; from: number; startedAt: number } | null>(null);

    function beginDrag(event: PointerEvent<HTMLDivElement>): void {
        // Without this a drag across the columns selects the rows it passes over, and the boundary arrives with half
        // the mailbox highlighted behind it.
        event.preventDefault();

        dragging.current = { pointer: event.pointerId, from: event.clientX, startedAt: width };

        try {
            event.currentTarget.setPointerCapture(event.pointerId);
        } catch {
            // A runtime that is not tracking this pointer refuses to hand its capture over, which is what a synthesised
            // event is. The drag then follows the handlers on the element instead, which is everything but the part
            // where it keeps following a pointer that has left the grip.
        }
    }

    function moveGrip(event: PointerEvent<HTMLDivElement>): void {
        const drag = dragging.current;

        if (drag?.pointer !== event.pointerId) {
            return;
        }

        onWidth(drag.startedAt + (event.clientX - drag.from));
    }

    function endDrag(event: PointerEvent<HTMLDivElement>): void {
        const drag = dragging.current;

        if (drag?.pointer !== event.pointerId) {
            return;
        }

        dragging.current = null;
        onChosen(drag.startedAt + (event.clientX - drag.from));
    }

    function moveByKey(event: KeyboardEvent<HTMLDivElement>): void {
        const moved = keyboardWidths[event.key];

        if (moved === undefined) {
            return;
        }

        // The arrows scroll the column behind the grip otherwise, and `Home` takes the page to the top of it.
        event.preventDefault();
        onChosen(moved(width));
    }

    return (
        <div
            role="separator"
            aria-orientation="vertical"
            aria-label={translate('mail.listWidth')}
            aria-valuenow={Math.round(width)}
            aria-valuemin={narrowestList}
            aria-valuemax={widestList}
            tabIndex={0}
            title={translate('mail.listWidthHint')}
            /* Five pixels is what the design draws, and it is less than a finger or a shaking hand can reliably hit —
               so the line stays five pixels and the target around it is widened to twenty-five with a pseudo-element,
               which is the accessibility obligation rather than a departure from the design. */
            className="after:-inset-x-2.5 relative w-1.25 shrink-0 cursor-col-resize touch-none bg-line transition hover:bg-accent-line after:absolute after:inset-y-0 after:content-['']"
            onPointerDown={beginDrag}
            onPointerMove={moveGrip}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
            onDoubleClick={() => {
                onChosen(startingListWidth);
            }}
            onKeyDown={moveByKey}
        />
    );
}

// What each key the grip answers does to the width. A lookup rather than a chain inside the handler, so the keys the
// control offers are one list a reader can see the whole of.
const keyboardWidths: Readonly<Record<string, ((width: number) => number) | undefined>> = {
    ArrowLeft: (width) => width - listWidthStep,
    ArrowRight: (width) => width + listWidthStep,
    Home: () => startingListWidth,
};
