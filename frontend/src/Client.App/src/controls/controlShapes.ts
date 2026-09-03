// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The four shapes a control takes in the design project, stated once for the two components that draw one: the control
// that does something and the control that stands for something the client cannot do yet. They are here rather than in
// either of those because a shape written twice is how the same button comes to look like two buttons — and because a
// module Vite hot-reloads may export components alone, which is what keeps this table out of the component files.

export type ControlShape = 'labelled' | 'symbol' | 'primary' | 'floating';

export const controlShapes: Readonly<Record<ControlShape, string>> = {
    labelled: 'gap-1.75 rounded-lg px-2.75 py-1.75 text-base text-text-soft hover:bg-hover',
    symbol: 'size-9.5 justify-center rounded-lg text-text-soft hover:bg-hover',
    primary: 'gap-1.75 rounded-lg bg-accent px-3.25 py-2 text-base font-semibold text-on-accent shadow-raised',
    floating: 'size-13.5 justify-center rounded-4xl bg-accent text-on-accent shadow-overlay',
};

/** Whether a control of that shape carries its name as words beside the symbol, or as the name of the symbol alone. */
export function labelledShape(shape: ControlShape): boolean {
    return shape === 'labelled' || shape === 'primary';
}
