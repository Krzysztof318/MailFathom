// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The three shapes a way out of a confirmation takes, stated once for the two components that draw one: the question
// itself and the proposal card that opens it. They are here rather than in either of those because the card's controls
// and the dialog's are read one after the other — the card is what the dialog is opened from — so a shape written twice
// is how the two come to disagree about the same act. `controls/controlShapes.ts` is the same rule for the toolbar, and
// it gives the other reason a table like this leaves the component file: a module Vite hot-reloads may export
// components alone.

/**
 * How a way out is drawn, which follows what pressing it costs rather than where it sits in the row.
 *
 * `back` leaves the act undone, `act` is the thing being confirmed, `aside` is a second way out that gives something
 * up without being the act — discarding what was written rather than filing it is the one today — and `destroy` is the
 * act where what it does is take something away: mail out of the folder it is in, or an operation half-finished. It is
 * the accent's weight in the error hue, which is how the design project draws the one control a reader should not
 * press by reflex.
 */
export type Manner = 'back' | 'aside' | 'act' | 'destroy';

// Exhaustive by its own type, so a manner added to the union fails to compile until it has been decided what it looks
// like.
export const mannerDrawn: Readonly<Record<Manner, string>> = {
    back: 'rounded-lg border border-line bg-sunken px-3.75 py-2 text-base text-text-soft transition hover:bg-hover',
    aside: 'rounded-lg border border-warning px-3.75 py-2 text-base text-warning-text transition hover:bg-warning-soft',
    act: 'rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90',
    destroy: 'rounded-lg bg-error px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90',
};
