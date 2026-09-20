// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The four shapes one event takes in the design project, stated once for the single component that draws one. They are
// here rather than in that component for the reason `controls/controlShapes.ts` gives about its own table: a module
// Vite hot-reloads may export components alone.
//
// What varies between them is what the surrounding view has already said. A card in a week column stands in a day that
// is one column wide, so it carries the time above the title; a card in an hour row stands in an hour the row already
// named, so the two sit on one line; an agenda row stands under its own day heading; and a chip in a month cell has
// room for the title and nothing else. None of them varies in colour, which is what keeps an event looking like one
// thing across four views.

export type EventShape = 'column' | 'hour' | 'agenda' | 'cell';

interface EventLook {
    /** What the entry measures, whether or not it is picked out. */
    readonly shape: string;

    /** What it looks like where it is one of those picked out. */
    readonly selected: string;

    /** What it looks like where it is not. */
    readonly unselected: string;
}

const pickedOut = 'border-accent bg-accent-soft';

export const eventShapes: Readonly<Record<EventShape, EventLook>> = {
    column: {
        shape: 'flex cursor-pointer flex-col gap-0.75 rounded-lg border px-2.5 py-2 text-start',
        selected: pickedOut,
        unselected: 'border-line bg-panel hover:border-accent',
    },
    hour: {
        shape: 'flex cursor-pointer flex-col gap-0.75 rounded-lg border px-2.5 py-2 text-start',
        selected: pickedOut,
        unselected: 'border-line bg-panel hover:border-accent',
    },
    agenda: {
        shape: 'flex cursor-pointer flex-col gap-0.75 rounded-lg border px-2.5 py-2 text-start',
        selected: pickedOut,
        unselected: 'border-transparent hover:bg-hover',
    },
    cell: {
        shape: 'flex cursor-pointer flex-col rounded-md border px-1.5 py-0.75 text-start',
        selected: pickedOut,
        unselected: 'border-line bg-panel hover:border-accent',
    },
};

/** Whether an entry of that shape says when the event is, which the surrounding view may already have said. */
export function timeShown(shape: EventShape): boolean {
    return shape !== 'cell';
}
