// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { glyphBox, type IconName, outlineOf } from './icons';

// The one place the client draws a symbol. What it draws from is `icons.ts` beside it, which is where the set and the
// outlines live.
//
// Every icon here is decorative. The control it stands in carries the accessible name, because a symbol repeated as
// alternative text beside the words it sits next to is noise a screen reader has to read twice.

export function Icon({ name, className }: { readonly name: IconName; readonly className?: string }) {
    const outline = outlineOf(name);

    if (outline === undefined) {
        return null;
    }

    return (
        <svg viewBox={glyphBox} aria-hidden="true" className={`shrink-0 fill-current ${className ?? 'size-5'}`}>
            <path d={outline} />
        </svg>
    );
}
