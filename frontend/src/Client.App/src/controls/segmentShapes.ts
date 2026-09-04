// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The four shapes one choice out of a group takes in the design project, stated once for the single component that
// draws one. They are here rather than in that component for the reason `controlShapes.ts` gives about its own table:
// a module Vite hot-reloads may export components alone.
//
// What varies between them is size, radius, whether the segment divides the row it stands in, and — for the one below
// that is a filter rather than a setting — how loudly the chosen one is said. The ring that says which has focus is
// never a shape's: it belongs to every group and is therefore the component's. The chip is the one that carries its
// own border and fill when nobody has chosen it, because it stands on the page rather than inside a pill that already
// drew them.
//
// **A filter is drawn more quietly than a setting**, which is the design project's own distinction and the whole
// reason `filter` stands beside `chip` rather than reusing it: a setting says what this client will do from now on and
// is drawn in the full accent, while a filter says what the list in front of you is showing and is drawn as a tint —
// loud enough to read against the other chip, quiet enough not to compete with the rows underneath it.

export type SegmentShape = 'row' | 'section' | 'compact' | 'chip' | 'filter';

interface SegmentLook {
    /** What the segment measures, whichever of the group is chosen. */
    readonly shape: string;

    /** What it looks like where somebody chose this one. */
    readonly chosen: string;

    /** What it looks like where somebody chose one of the others. */
    readonly unchosen: string;
}

const accented = 'bg-accent font-semibold text-on-accent';
const quiet = 'text-muted hover:bg-hover';

export const segmentShapes: Readonly<Record<SegmentShape, SegmentLook>> = {
    row: { shape: 'flex-1 rounded-lg py-1 text-center text-sm', chosen: accented, unchosen: quiet },
    section: { shape: 'flex-1 rounded-md py-1.5 text-center text-sm', chosen: accented, unchosen: quiet },
    compact: { shape: 'rounded-md px-2 py-1 text-xs', chosen: accented, unchosen: quiet },
    chip: {
        shape: 'rounded-lg px-3.25 py-1.5 text-sm',
        chosen: accented,
        unchosen: 'border border-line bg-sunken text-text-soft hover:bg-hover',
    },
    filter: {
        shape: 'rounded-lg border px-3.25 py-1.5 text-sm',
        chosen: 'border-accent-line bg-accent-soft font-semibold text-accent-deep',
        unchosen: 'border-line bg-sunken text-text-soft hover:bg-hover',
    },
};
