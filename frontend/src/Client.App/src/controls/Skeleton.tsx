// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// One block standing in the space something that has not arrived will occupy. It is the whole of what a skeleton is
// made of in this client: the four the design project draws — a folder's list, a message, the full HTML surface, and
// an opened attachment — are four arrangements of this one block rather than four ways of drawing a placeholder, which
// is what § *UI* means by a repeated structure having one shape.
//
// It carries no words and is hidden from the accessibility tree, because a skeleton says nothing a reader can hear.
// What says the surface is waiting is the sentence beside it, which every one of those four keeps: a screen that
// replaced its wait announcement with shapes would be a screen that waits in silence for anybody not looking at it.
//
// What it draws is `shimmering` in `styles.css`, so the gradient, the band's width, and how fast it travels are stated
// once there. Under `prefers-reduced-motion` the travel goes and the block stays, which is the half that carries the
// meaning: the space is still reserved, so nothing shifts when the answer lands.

export function Skeleton({
    className,
    fills,
}: {
    /** The shape it takes where it stands: its height, and any width the arrangement gives it. */
    readonly className?: string;

    /**
     * How much of the line it fills, as a percentage.
     *
     * It is a number rather than a utility because it is data: a skeleton reads as words by its lines being ragged,
     * and the raggedness is a list of lengths the design project chose rather than a size the theme holds.
     */
    readonly fills?: number;
}) {
    return (
        <span
            aria-hidden="true"
            className={`shimmering block ${className ?? ''}`}
            style={fills === undefined ? undefined : { width: `${String(fills)}%` }}
        />
    );
}

/**
 * One line of a block standing where words will be.
 *
 * A line filling nothing is the gap between two paragraphs rather than a line of no length, which is how the design
 * project writes them: a block of even lines reads as a loading bar, and what makes it read as prose is that it is
 * ragged and broken into paragraphs.
 */
export interface SkeletonLine {
    /** How much of the width this line fills, as a percentage, or `0` where it is a gap. */
    readonly fills: number;

    /** How tall it stands, as the utility the design project's measurement is written in. */
    readonly height: string;
}

/** A block of lines standing where words will be, which three of the four waits the design project draws are. */
export function SkeletonLines({
    lines,
    className,
}: {
    readonly lines: readonly SkeletonLine[];
    readonly className?: string;
}) {
    return (
        <div className={`flex flex-col ${className ?? ''}`}>
            {lines.map((line, at) =>
                line.fills === 0 ? (
                    <div key={at} aria-hidden="true" className={line.height} />
                ) : (
                    <Skeleton key={at} className={line.height} fills={line.fills} />
                ),
            )}
        </div>
    );
}
