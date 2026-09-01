// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one shape a control that is on or off has: a checkbox and its words in a box that fills while it is on. Two
// surfaces need it — each of the list's narrowings, and the mode that picks several messages out under a finger — and
// a second arrangement of the same utilities is how the two would stop looking like one product.
//
// A checkbox rather than a button that looks pressed, because whether it is on is what a reader has to be able to see
// and what a screen reader has to be able to say, and that is what the platform's own control already answers.
//
// It sits here beside `SecondaryButton` rather than in the screen that needed it first, for the reason stated there: a
// screen may reach what is shared, and what is shared reaches no screen.

import { borderedControl } from './chrome';

const checkable = `flex cursor-pointer items-center gap-1.5 px-2 py-1 text-sm ${borderedControl}`;

export function CheckControl({
    label,
    on,
    onChange,
}: {
    readonly label: string;
    readonly on: boolean;
    readonly onChange: (on: boolean) => void;
}) {
    return (
        <label className={`${checkable} ${on ? 'bg-accent-soft text-accent-strong' : ''}`}>
            <input
                type="checkbox"
                className="accent-accent"
                checked={on}
                onChange={(event) => {
                    onChange(event.target.checked);
                }}
            />
            {label}
        </label>
    );
}
