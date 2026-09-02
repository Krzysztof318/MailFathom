// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The mark that tells one mailbox from the next, which the design project draws in two places: in front of a group in
// the folder tree, and beside each address in the account menu. It is shared rather than drawn twice because the whole
// of its meaning is that the same mailbox carries the same colour wherever it appears — two implementations would be
// two mailboxes as far as a reader is concerned.
//
// It is hidden from the accessibility tree: the name beside it is what says which mailbox this is, and a colour that
// has to be seen says nothing to somebody who cannot see it.

export function MailboxMark({ ordinal, className }: { readonly ordinal: number; readonly className?: string }) {
    // Ordinal zero stands for every mailbox at once, which the folder tree has a row for, so it takes both hues; the
    // mailboxes after it alternate between them.
    const fill =
        ordinal === 0
            ? 'bg-linear-to-br from-mailbox-mark from-50% to-mailbox-mark-alternate to-50%'
            : ordinal % 2 === 1
              ? 'bg-mailbox-mark'
              : 'bg-mailbox-mark-alternate';

    return <span aria-hidden="true" className={`shrink-0 rounded-full ${className ?? 'size-2'} ${fill}`} />;
}
