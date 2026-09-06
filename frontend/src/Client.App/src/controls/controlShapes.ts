// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The shapes a control takes in the design project, stated once for the two components that draw one: the control that
// does something and the control that stands for something the client cannot do yet. They are here rather than in
// either of those because a shape written twice is how the same button comes to look like two buttons — and because a
// module Vite hot-reloads may export components alone, which is what keeps this table out of the component files.
//
// Two of them are the same two controls standing on the accent tint rather than on a panel, which the selection bar is
// drawn as. They are shapes of their own rather than a colour passed in, because what changes is every colour the
// control has: a control that took `text-text-soft` from the table and a foreground from its caller would be two
// utilities of one property fighting over which of them the stylesheet emitted last.
//
// Three are the same pairing again for the width a strip has: the design keeps the words beside the symbols for as long
// as they fit the strip and draws the symbols alone once they do not — `mailSpace/useStripFit.ts` is where that is
// measured. So `symbol` is what `labelled` narrows to, `selectedSymbol` what `selected` narrows to, and
// `primarySymbol` what `primary` narrows to. `named` is the words alone, which is how the design draws the acts in
// the head of a message beside its subject.
//
// What is deliberately absent is the size a finger needs. Every shape below is drawn at the measure the design project
// gives it, and the floor a control grows to under a coarse pointer is stated once for the whole client in
// `styles.css` — a bar met in eight shapes and forty hand-written controls is a bar most of them eventually stop
// meeting.

export type ControlShape =
    'labelled' | 'named' | 'symbol' | 'primary' | 'primarySymbol' | 'floating' | 'selected' | 'selectedSymbol';

export const controlShapes: Readonly<Record<ControlShape, string>> = {
    labelled: 'gap-1.75 rounded-lg px-2.75 py-1.75 text-base text-text-soft hover:bg-hover',
    named: 'rounded-md px-2 py-1.25 text-base text-muted hover:bg-hover hover:text-text',
    symbol: 'size-9.5 justify-center rounded-lg text-text-soft hover:bg-hover',
    primary: 'gap-1.75 rounded-lg bg-accent px-3.25 py-2 text-base font-semibold text-on-accent shadow-raised',
    primarySymbol: 'size-9.5 justify-center rounded-lg bg-accent text-on-accent shadow-raised',
    floating: 'size-13.5 justify-center rounded-4xl bg-accent text-on-accent shadow-overlay',
    selected: 'gap-1.75 rounded-lg px-2.75 py-1.75 text-base text-accent-deep hover:bg-accent-line',
    selectedSymbol: 'size-9.5 justify-center rounded-lg text-accent-deep hover:bg-accent-line',
};

/** Whether a control of that shape carries its name as words, or as the name of the symbol alone. */
export function labelledShape(shape: ControlShape): boolean {
    return shape === 'labelled' || shape === 'named' || shape === 'primary' || shape === 'selected';
}

/** Whether a control of that shape draws its symbol at all: the words-only shape carries its name and nothing else. */
export function symbolShown(shape: ControlShape): boolean {
    return shape !== 'named';
}
