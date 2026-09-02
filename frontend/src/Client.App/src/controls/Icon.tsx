// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { glyphOf, type IconName } from './icons';

// The one place the client draws a symbol. What it draws from is `icons.ts` beside it, which is where the set and the
// outlines live, and each outline arrives with the box it was drawn in rather than with one assumed for all of them.
//
// Every icon here is decorative. The control it stands in carries the accessible name, because a symbol repeated as
// alternative text beside the words it sits next to is noise a screen reader has to read twice.

export function Icon({ name, className }: { readonly name: IconName; readonly className?: string }) {
    const glyph = glyphOf(name);

    if (glyph === undefined) {
        return null;
    }

    return (
        <svg viewBox={glyph.box} aria-hidden="true" className={`shrink-0 fill-current ${className ?? 'size-5'}`}>
            <path d={glyph.outline} />
        </svg>
    );
}
