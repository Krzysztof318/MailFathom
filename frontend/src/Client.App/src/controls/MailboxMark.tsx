// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { mailboxMarkHue } from './mailboxRamp';

// The mark that tells one mailbox from the next, which the design project draws in two places: in front of a group in
// the folder tree, and beside each address in the account menu. It is shared rather than drawn twice because the whole
// of its meaning is that the same mailbox carries the same colour wherever it appears — two implementations would be
// two mailboxes as far as a reader is concerned.
//
// It is hidden from the accessibility tree: the name beside it is what says which mailbox this is, and a colour that
// has to be seen says nothing to somebody who cannot see it.

export function MailboxMark({ ordinal, className }: { readonly ordinal: number; readonly className?: string }) {
    // Ordinal zero stands for every mailbox at once, which the folder tree has a row for, so it takes the ramp's first
    // two hues split across the diagonal; every other ordinal takes one hue, which `mailboxRamp.ts` chooses.
    //
    // The fill is written as a value rather than composed out of utilities because the hue is chosen while the client
    // runs and a Tailwind class name cannot be. A utility for each hue would be a second list of the ramp's names to
    // keep in step with `styles.css`, which is the drift the numbered ramp exists to remove; what is named here is
    // still the token layer and never a colour.
    const fill =
        ordinal === 0
            ? 'linear-gradient(to bottom right, var(--color-mailbox-mark-1) 50%, var(--color-mailbox-mark-2) 50%)'
            : `var(--color-mailbox-mark-${String(mailboxMarkHue(ordinal))})`;

    return (
        <span
            aria-hidden="true"
            className={`shrink-0 rounded-full ${className ?? 'size-2'}`}
            style={{ background: fill }}
        />
    );
}
