// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one shape a button that is not the main action of its surface has. Four surfaces need it already — reading the
// accounts again, pointing the client somewhere else, signing out, and giving up on an attempt — and each of them
// composed it by hand until they stopped agreeing on the border, the fill, and the padding. Stated once, they cannot
// drift apart again.
//
// It sits here rather than in `shell/` because two of the four are on the sign-in screen, which is not the frame: a
// screen may reach what is shared, and what is shared reaches no screen.

import { borderedControl } from './chrome';

/**
 * How much room the button takes, which is the one thing its four callers genuinely differ on.
 *
 * `compact` is a control sitting on a line of text — a header, a summary — where a taller button would push the line
 * apart. `form` is a control standing beside a submit button, where it has to read as its equal.
 */
export type SecondaryButtonShape = 'compact' | 'form';

const shapes: Readonly<Record<SecondaryButtonShape, string>> = {
    compact: 'px-2 py-0.5 text-sm',
    form: 'px-4 py-2 font-medium',
};

export function SecondaryButton({
    label,
    shape = 'compact',
    onActivate,
}: {
    readonly label: string;
    readonly shape?: SecondaryButtonShape;
    readonly onActivate: () => void;
}) {
    return (
        <button className={`${borderedControl} ${shapes[shape]}`} type="button" onClick={onActivate}>
            {label}
        </button>
    );
}
