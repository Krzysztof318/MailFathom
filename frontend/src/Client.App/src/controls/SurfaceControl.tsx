// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from './Icon';
import type { IconName } from './icons';

// The control a surface's own head carries — closing it, downloading what it is showing — drawn as the symbol alone.
//
// It is here rather than in either surface because both draw it: the surface that shows a message's own markup, and the
// surface that shows a file the message carries. Written twice it was already two sizes, which is the drift a shared
// component exists to stop; it is a component of its own rather than a fifth `ControlShape` because what differs from
// those four is the symbol's own size as well as the box around it.
//
// The name is on the control rather than beside it, because a head that named every symbol in words would be a second
// toolbar over the thing being read. `title` carries the same words for a pointer.

export function SurfaceControl({
    label,
    icon,
    onActivate,
}: {
    readonly label: string;
    readonly icon: IconName;
    readonly onActivate: () => void;
}) {
    return (
        <button
            type="button"
            aria-label={label}
            title={label}
            className="flex size-8 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
            onClick={onActivate}
        >
            <Icon name={icon} className="size-5" />
        </button>
    );
}
