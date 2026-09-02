// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/**
 * What every bordered control standing on a panel is drawn with: the corner, the line around it, the fill, the weight
 * its words are set in, and what it becomes under a pointer.
 *
 * Stated once because three controls draw it — a secondary button, a checkbox that reads as a control, and the list's
 * order chooser — and a restyle of that look made in one of them would leave the other two behind. What is deliberately
 * not here is the padding and the type size: a button standing beside a submit and a checkbox on a filter line do not
 * agree on those, so each states its own.
 */
export const borderedControl = 'rounded-md border border-line bg-panel text-text-soft transition hover:bg-hover';

/**
 * What a chip is drawn with: the pill the design project draws a filter, a scope, or a choice as, on the rail surface
 * with a line around it. The list's order chooser, its filters, and the composer's scope chips all take it, so a
 * restyle of the pill is one edit rather than three that drift.
 */
export const chip = 'rounded-full border border-line bg-rail text-base text-text-soft transition hover:bg-hover';
