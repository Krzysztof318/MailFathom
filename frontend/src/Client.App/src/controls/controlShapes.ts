// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The seven shapes a control takes in the design project, stated once for the two components that draw one: the control
// that does something and the control that stands for something the client cannot do yet. They are here rather than in
// either of those because a shape written twice is how the same button comes to look like two buttons — and because a
// module Vite hot-reloads may export components alone, which is what keeps this table out of the component files.
//
// Two of them are the same two controls standing on the accent fill rather than on a panel, which the selection bar is
// drawn as. They are shapes of their own rather than a colour passed in, because what changes is every colour the
// control has: a control that took `text-text-soft` from the table and a foreground from its caller would be two
// utilities of one property fighting over which of them the stylesheet emitted last.
//
// Two more are the same pairing again for the width a strip has: the design draws the toolbar's controls as words
// beside their symbols only where there is a mailbox column beside them, and as symbols alone at every narrower
// composition. So `symbol` is what `labelled` narrows to, and `primarySymbol` is what `primary` narrows to.

export type ControlShape =
    'labelled' | 'symbol' | 'primary' | 'primarySymbol' | 'floating' | 'onAccent' | 'onAccentSymbol';

export const controlShapes: Readonly<Record<ControlShape, string>> = {
    labelled: 'gap-1.75 rounded-lg px-2.75 py-1.75 text-base text-text-soft hover:bg-hover',
    symbol: 'size-9.5 justify-center rounded-lg text-text-soft hover:bg-hover',
    primary: 'gap-1.75 rounded-lg bg-accent px-3.25 py-2 text-base font-semibold text-on-accent shadow-raised',
    primarySymbol: 'size-9.5 justify-center rounded-lg bg-accent text-on-accent shadow-raised',
    floating: 'size-13.5 justify-center rounded-4xl bg-accent text-on-accent shadow-overlay',
    onAccent: 'gap-1.75 rounded-lg px-2.75 py-1.75 text-base text-on-accent hover:bg-accent-strong',
    onAccentSymbol: 'size-9.5 justify-center rounded-lg text-on-accent hover:bg-accent-strong',
};

/** Whether a control of that shape carries its name as words beside the symbol, or as the name of the symbol alone. */
export function labelledShape(shape: ControlShape): boolean {
    return shape === 'labelled' || shape === 'primary' || shape === 'onAccent';
}
