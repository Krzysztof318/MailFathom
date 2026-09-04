// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The four shapes one choice out of a group takes in the design project, stated once for the single component that
// draws one. They are here rather than in that component for the reason `controlShapes.ts` gives about its own table:
// a module Vite hot-reloads may export components alone.
//
// What varies between them is size, radius, and whether the segment divides the row it stands in — never the accent
// that says which is chosen or the ring that says which has focus, both of which belong to every group and are
// therefore the component's rather than a shape's. The chip is the one that carries its own border and fill when
// nobody has chosen it, because it stands on the page rather than inside a pill that already drew them.

export type SegmentShape = 'row' | 'section' | 'compact' | 'chip';

interface SegmentLook {
    /** What the segment measures, whichever of the group is chosen. */
    readonly shape: string;

    /** What it looks like where somebody chose one of the others. */
    readonly unchosen: string;
}

export const segmentShapes: Readonly<Record<SegmentShape, SegmentLook>> = {
    row: { shape: 'flex-1 rounded-lg py-1 text-center text-sm', unchosen: 'text-muted hover:bg-hover' },
    section: { shape: 'flex-1 rounded-md py-1.5 text-center text-sm', unchosen: 'text-muted hover:bg-hover' },
    compact: { shape: 'rounded-md px-2 py-1 text-xs', unchosen: 'text-muted hover:bg-hover' },
    chip: {
        shape: 'rounded-lg px-3.25 py-1.5 text-sm',
        unchosen: 'border border-line bg-sunken text-text-soft hover:bg-hover',
    },
};
