// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { placedWithin, type MenuPoint } from './menuPlacement';

// What a row answers a press with, which the design project draws on seven of its lists: a header naming the row, then
// the acts that row offers, the one that cannot be taken back drawn as what it is. It is one component for all seven
// rather than a menu per screen, because a second arrangement of a header and a column of items is how a client comes
// to have two menus that resemble each other — and because what a menu *is* has nothing to do with what a row holds.
//
// So what a list supplies is its items, and everything else is here: where the menu stands, that it stays on the
// screen, that it can be walked without a pointer, and that leaving it puts the reader back where they were.
//
// **The two compositions are the design project's own.** Given room, the menu opens at the point the gesture happened
// and is pushed back inside the pane rather than off its edge; given a phone, it stands in the middle of the screen,
// because a menu anchored under a thumb at the foot of a narrow window has nowhere to go. Neither is a question about
// which head this is — it is the width the window has, asked once.
//
// **It announces as a menu**, which is what makes every item reachable by name rather than by position: the header
// names it, the items are its own, and nothing else is inside the element carrying the role.

/** One thing a row offers, as the menu draws it. */
export interface ContextMenuItem {
    readonly icon: IconName;
    readonly label: string;

    /** Whether this is the act that cannot be taken back, which the design project draws apart from the others. */
    readonly destroys?: boolean;

    readonly choose: () => void;
}

const menuItem =
    'flex min-h-9 cursor-pointer items-center gap-2.75 rounded-lg px-3 text-start text-base transition hover:bg-hover pointer-coarse:min-h-12 pointer-coarse:text-md';

export function ContextMenu({
    header,
    at,
    items,
    onClose,
}: {
    /** What the menu is about, in the row's own words, which is also the name the menu carries. */
    readonly header: string;

    /** Where the gesture happened, in the window's own coordinates. */
    readonly at: MenuPoint;

    readonly items: readonly ContextMenuItem[];

    /** Closes the menu and puts focus back on what it was opened from, which is the caller's to know. */
    readonly onClose: () => void;
}) {
    const wide = useWideWorkspace();
    const names = useId();
    const within = useRef<HTMLDivElement>(null);
    const panel = useRef<HTMLDivElement>(null);
    const [placed, setPlaced] = useState<MenuPoint | null>(null);

    // Measured rather than computed from what the items would come to: the menu's own size is the stylesheet's answer,
    // and a second copy of it here would be two numbers that have to agree. A layout effect, because this runs against
    // a commit the browser has already laid out and before it has painted one — so the menu is never drawn in the wrong
    // place and then corrected.
    useLayoutEffect(() => {
        const box = within.current;
        const menu = panel.current;

        if (box === null || menu === null || !wide) {
            return;
        }

        const bounds = box.getBoundingClientRect();

        setPlaced(
            placedWithin(
                { x: at.x - bounds.left, y: at.y - bounds.top },
                { width: menu.offsetWidth, height: menu.offsetHeight },
                { width: box.clientWidth, height: box.clientHeight },
            ),
        );
    }, [at, wide]);

    // Focus moves into the menu whichever opener asked for it, because a menu that opened somewhere else on the screen
    // is one a reader working from the keyboard would have to hunt for — and because Escape has to reach it.
    useEffect(() => {
        walkable(panel.current)[0]?.focus();
    }, []);

    // Closed by a press outside it rather than by the click that follows one. The tap ending a long press arrives as a
    // click with no press of its own behind it, so a menu listening for that would close under the finger that had
    // just asked for it — and the press that opened this one is already over by the time this is listening.
    useEffect(() => {
        function pressedElsewhere(event: Event): void {
            if (panel.current?.contains(event.target as Node) !== true) {
                onClose();
            }
        }

        document.addEventListener('pointerdown', pressedElsewhere, true);

        return () => {
            document.removeEventListener('pointerdown', pressedElsewhere, true);
        };
    }, [onClose]);

    function onKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
        const walked = walkable(panel.current);
        const standing = walked.indexOf(document.activeElement as HTMLElement);

        switch (event.key) {
            // Tab beside Escape, because leaving by the keyboard's own way out has to close the menu rather than put
            // focus behind it, where a reader would be tabbing through a screen with a menu still open over it.
            case 'Escape':
            case 'Tab':
                onClose();
                break;
            case 'ArrowDown':
                walked[(standing + 1) % walked.length]?.focus();
                break;
            case 'ArrowUp':
                walked[(standing - 1 + walked.length) % walked.length]?.focus();
                break;
            case 'Home':
                walked[0]?.focus();
                break;
            case 'End':
                walked[walked.length - 1]?.focus();
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    return (
        <>
            {/* What the page is dimmed to behind the menu. A second menu gesture out here is a reader pointing
                somewhere else, so the browser's own menu is refused and this one goes; the panel refuses the same
                gesture on its own account below, because it paints over this rather than inside it. Everything else
                about leaving is the two effects above. */}
            <div
                aria-hidden="true"
                className="fixed inset-0 z-70 bg-scrim"
                onContextMenu={(event) => {
                    event.preventDefault();
                    onClose();
                }}
            />

            {/* The space the menu is kept inside, which is the window less whatever a notch or a rounded corner takes
                out of it. It is a box of its own so that staying on the screen is one measurement rather than an inset
                added to every number below. */}
            <div
                ref={within}
                className="pointer-events-none fixed top-safe-top right-safe-right bottom-safe-bottom left-safe-left z-70 flex items-center justify-center p-4.5 workspace:block workspace:p-0"
            >
                <div
                    ref={panel}
                    style={placed === null ? undefined : { left: placed.x, top: placed.y }}
                    className={`pointer-events-auto flex max-h-full w-75 max-w-full flex-col overflow-y-auto rounded-3xl border border-line bg-panel pt-1.75 pr-1.5 pb-2 pl-1.5 text-text shadow-dialog workspace:absolute workspace:w-59 workspace:pointer-coarse:w-65.5 ${
                        wide && placed === null ? 'invisible' : ''
                    }`}
                    onKeyDown={onKeyDown}
                    // The same gesture landing on the menu itself asks for what is already open, so it is refused and
                    // nothing else happens: closing here would take the menu away from somebody who pointed at it.
                    onContextMenu={(event) => {
                        event.preventDefault();
                    }}
                >
                    <p id={names} className="truncate px-3 pt-1 pb-1.5 text-2xs tracking-widest text-muted">
                        {header}
                    </p>

                    {/* Nothing but the items sits inside the element carrying the role, which is what a menu is
                        allowed to hold — the header names it from outside rather than standing in it as one more
                        thing to walk past. */}
                    <div role="menu" aria-labelledby={names} className="flex flex-col gap-0.5">
                        {items.map((item) => (
                            <button
                                key={item.label}
                                type="button"
                                role="menuitem"
                                // Focus is moved by the arrow keys rather than by Tab, which is what a menu is: one
                                // stop on the way in, and the items reached from there.
                                tabIndex={-1}
                                className={`${menuItem} ${item.destroys ? 'text-error-text' : ''}`}
                                onClick={() => {
                                    onClose();
                                    item.choose();
                                }}
                            >
                                {/* The symbol keeps its own column so every label starts in the same place, which is
                                    what the design project draws and what makes the list readable as a column. */}
                                <span className="flex w-6 shrink-0 justify-center">
                                    <Icon name={item.icon} className="size-5" />
                                </span>

                                <span className="truncate">{item.label}</span>
                            </button>
                        ))}
                    </div>
                </div>
            </div>
        </>
    );
}

/** The items as the keyboard reaches them, read out of the document so nothing here holds a second list of them. */
function walkable(panel: HTMLElement | null): readonly HTMLElement[] {
    return panel === null ? [] : Array.from(panel.querySelectorAll<HTMLElement>('[role="menuitem"]'));
}
