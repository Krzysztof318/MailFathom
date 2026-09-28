// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// How a written block is drawn, whoever wrote it. The design project draws a message body and the prose of an agent's
// answer with the same helpers, so a quotation, a table, or a code block looks the same in both, and it is stated once
// here rather than twice.

/**
 * What each of the levels a heading was written at is drawn at, which is the design's own three steps rather than six
 * sizes: the first two are titles and everything below them is the label the design draws a subheading as.
 */
export const headingShapes = [
    'mt-0.5 text-3xl font-semibold tracking-tight text-text',
    'mt-0.5 text-xl font-semibold text-text',
    'mt-0.5 text-xs font-medium tracking-widest uppercase text-muted',
] as const;

/** The shape a heading written at a level is drawn in, the third step answering for every level below it. */
export function headingShapeAt(level: number): string {
    return headingShapes[Math.min(Math.max(level, 1), headingShapes.length) - 1] ?? headingShapes[2];
}

// The marker is the browser's own rather than a column of text drawn beside each item: the design gives it the faint
// tone and nothing else, and a hand-drawn bullet is one a screen reader would announce as a character in the sentence.
// `space-y` rather than a gap, because a list item may itself hold several blocks.
export const orderedListShape = 'list-decimal space-y-1.5 ps-6 marker:text-faint';
export const unorderedListShape = 'list-disc space-y-1.5 ps-6 marker:text-faint';

export const separatorShape = 'my-0.5 border-line';
export const quoteShape = 'rounded-e-lg border-s-3 border-highlight-line bg-highlight px-3.75 py-2.75';
export const preformattedShape =
    'overflow-x-auto rounded-xl border border-line bg-sunken px-3.5 py-3 font-mono text-base whitespace-pre-wrap';

export const tableRegionShape = 'overflow-x-auto rounded-xl border border-line bg-panel';
export const tableShape = 'w-full border-collapse text-start text-base';
export const tableRowShape = 'border-b border-line last:border-b-0';
export const tableCellShape = 'border-e border-line px-3 py-2.25 align-top last:border-e-0';
export const tableHeaderCellShape = 'bg-sunken font-semibold text-text';

export const inlineCodeShape = 'rounded-sm border border-line bg-hover px-1.25 py-px font-mono text-base';
export const strikethroughShape = 'text-muted';
export const strongShape = 'font-semibold text-text';
