// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from './Icon';
import type { IconName } from './icons';

// The control the head of a surface, a column, or a dialog carries — closing it, folding it, opening it, downloading
// what it is showing — drawn as the symbol alone.
//
// It is here rather than in any of them because five draw it: the surface that shows a message's own markup and the
// control on the message's head that opens it, the surface that shows a file the message carries, the mailbox column
// and the drawer that replaces it, and the sheet that asks which folder to file a message in. Written out by hand it
// was already two sizes, which is the drift a shared component exists to stop; it is a component of its own rather
// than an eighth `ControlShape` because what differs from those is the symbol's own size as well as the box around it.
//
// The name is on the control rather than beside it, because a head that named every symbol in words would be a second
// toolbar over the thing being read. `title` carries the same words for a pointer.
//
// It is drawn at the size the design gives it and grows under a finger to the floor `styles.css` states for every
// control in the client, which is why no measurement here mentions a pointer.

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
