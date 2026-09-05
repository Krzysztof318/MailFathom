// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The host the message came from, which is what the reader recognises when the display name is somebody's first name
// and the address is not shown. Absent where the sender wrote no address, rather than drawn as an empty parenthesis.
// It gives way before the name does: a line this narrow cannot hold both in full, and the name is what is scanned.
//
// Shared for the reason `ReceivedAt` and `MessageMarkers` are: the mail list and the conversation say the same thing
// about the same sender, and two spellings of one host is how they start disagreeing about who wrote.
//
// It is drawn in the desktop composition alone, which is where the design project draws it. That is a decision about
// the composition rather than about the room on the line: the project gives the list the same width at the tablet as
// at the desktop and still leaves the host off the narrower one, because a column that has given up its mailboxes to a
// drawer has given up the least-read thing on the row with them. Nothing is lost by width alone — the address is on
// the message's own head, which is one press away and is where somebody checking who wrote actually looks.

/** Where a sender wrote from, or nothing at all where the address says nothing a reader could use. */
export function Organisation({ address }: { readonly address: string | null }) {
    const at = address?.lastIndexOf('@') ?? -1;

    if (address === null || at < 0 || at === address.length - 1) {
        return null;
    }

    return <span className="hidden truncate text-xs text-faint desktop:inline">{address.slice(at + 1)}</span>;
}
