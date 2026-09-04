// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { segmentShapes, type SegmentShape } from './segmentShapes';

// One choice out of a group of them: the theme, the language, and which of the two surfaces a message opens on are
// each drawn this way, in four sizes and in five places.
//
// A radio button rather than a button with a role written onto it: the platform already announces a group of them as
// one set of choices, reports which is in force, moves between them with the arrow keys, and leaves one tab stop where
// a row of buttons would leave one each. The input is hidden from sight rather than from the accessibility tree, and
// the label naming it carries both the accent that says which is chosen and the ring that says which has focus.
//
// What stands around the segments — the `fieldset`, its legend, and the pill they sit in — is the caller's, because
// the design project draws that differently in each of the five places while the segment itself is one thing.

export function ChoiceSegment({
    shape,
    name,
    value,
    chosen,
    onChoose,
    children,
}: {
    readonly shape: SegmentShape;

    /** What groups these segments into one set of choices, which is the radio group's name. */
    readonly name: string;

    readonly value: string;
    readonly chosen: boolean;

    /** Said with the value of whichever segment was chosen, which the caller reads back into its own closed set. */
    readonly onChoose: (value: string) => void;

    readonly children: ReactNode;
}) {
    const look = segmentShapes[shape];

    return (
        <label
            className={`cursor-pointer transition has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent ${look.shape} ${
                chosen ? look.chosen : look.unchosen
            }`}
        >
            <input
                type="radio"
                name={name}
                value={value}
                checked={chosen}
                className="sr-only"
                onChange={(event) => {
                    onChoose(event.target.value);
                }}
            />
            {children}
        </label>
    );
}
