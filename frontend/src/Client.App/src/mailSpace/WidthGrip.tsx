// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, type KeyboardEvent, type PointerEvent } from 'react';

// The grip on a boundary between two columns, which is what lets a reader give either side more room. It draws the
// line the design project puts there and it is the control on it — those are one element rather than two, because a
// boundary somebody can move is a boundary they have to be able to point at.
//
// **One component for both boundaries the Mail space has**, the one between the mailboxes and the list and the one
// between the list and the reading pane. What differs between them is four numbers and two sentences, which is what
// it takes as props; what does not differ is everything below, and a second grip resembling this one is how a client
// comes to have two boundaries that behave differently under the keyboard.
//
// It is a separator that takes focus, which is what ARIA calls a window splitter: the position it reports is the
// width of the pane before it, so a reader who cannot see the columns still knows where the boundary stands and how
// far it may go. Everything it can be done with a pointer it can be done with the keyboard — the arrows move it in
// steps, and `Home` returns it to the width it started at, which is what a double-click does for a mouse.
//
// One pointer path rather than one per input, because pointer events are what a mouse, a finger, and a pen all arrive
// as. Capturing the pointer is what keeps a drag with it once it has left the five pixels the grip is drawn at, and
// `touch-none` is what keeps a finger dragging the boundary from scrolling the list underneath it instead.

export function WidthGrip({
    label,
    hint,
    width,
    narrowest,
    widest,
    step,
    startingWidth,
    onWidth,
    onChosen,
}: {
    /** What the boundary is called, which is the name a reader who cannot see the columns hears. */
    readonly label: string;

    /** What moving it does, as the pointer's own hint. */
    readonly hint: string;

    /** How wide the pane before it is drawn right now, which is the position this reports and a drag starts from. */
    readonly width: number;

    /** The narrowest that pane may be, which is the least this reports as a separator. */
    readonly narrowest: number;

    /** The widest it may be. */
    readonly widest: number;

    /** How far one arrow press moves the boundary. */
    readonly step: number;

    /** The width `Home` and a double-click return the boundary to. */
    readonly startingWidth: number;

    /** The width to draw while the boundary is being moved. */
    readonly onWidth: (width: number) => void;

    /** The width somebody settled on, which is the one worth keeping. */
    readonly onChosen: (width: number) => void;
}) {
    // What a drag is: the pointer that started it, and where it and the boundary stood then. A ref rather than state,
    // because nothing on the screen is drawn from it and a moving pointer would otherwise render for every pixel of
    // its own bookkeeping.
    const dragging = useRef<{ pointer: number; from: number; startedAt: number } | null>(null);

    function beginDrag(event: PointerEvent<HTMLDivElement>): void {
        const drag = dragging.current;

        // A second finger landing on the grip mid-drag is ignored rather than taking the drag over: the pointer that
        // started it is the one the two handlers below answer to, so letting a newcomer replace it would leave the
        // first pointer's move and release unmatched and the width it was dragging toward never settled.
        if (drag !== null && drag.pointer !== event.pointerId) {
            return;
        }

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
        const moved = keyboardWidths(width, step, startingWidth)[event.key];

        if (moved === undefined) {
            return;
        }

        // The arrows scroll the column behind the grip otherwise, and `Home` takes the page to the top of it.
        event.preventDefault();
        onChosen(moved);
    }

    return (
        <div
            role="separator"
            aria-orientation="vertical"
            aria-label={label}
            aria-valuenow={Math.round(width)}
            aria-valuemin={narrowest}
            aria-valuemax={widest}
            tabIndex={0}
            title={hint}
            /* Five pixels is what the design draws, and it is less than a finger or a shaking hand can reliably hit —
               so the line stays five pixels and the target around it is widened to twenty-five with a pseudo-element,
               which is the accessibility obligation rather than a departure from the design. */
            className="after:-inset-x-2.5 relative w-1.25 shrink-0 cursor-col-resize touch-none bg-line transition hover:bg-accent-line after:absolute after:inset-y-0 after:content-['']"
            onPointerDown={beginDrag}
            onPointerMove={moveGrip}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
            onDoubleClick={() => {
                onChosen(startingWidth);
            }}
            onKeyDown={moveByKey}
        />
    );
}

// What each key the grip answers moves the width to. A lookup rather than a chain inside the handler, so the keys the
// control offers are one list a reader can see the whole of.
function keyboardWidths(
    width: number,
    step: number,
    startingWidth: number,
): Readonly<Record<string, number | undefined>> {
    return {
        ArrowLeft: width - step,
        ArrowRight: width + step,
        Home: startingWidth,
    };
}
