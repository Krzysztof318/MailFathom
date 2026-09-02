// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a downloaded file is named on somebody's machine. It sits apart from the row that offers the download because a
// module Vite hot-reloads may export components alone, which is the same reason `localization/useLocalization.ts` sits
// apart from the provider that fills it.

// The most of a sender's own file name this client will write to somebody's machine.
const longestSavedName = 128;

// What no file name may carry: the characters nothing draws — C0, then DEL with C1 behind it, then the bidirectional
// controls, which occupy no width while reordering everything around them — beside the separators and the reserved
// characters a file system acts on. Naming each range by number is the point of the pattern rather than an accident
// of it, which is what the suppression below says.
// eslint-disable-next-line no-control-regex
const refusedInAFileName = /[\u0000-\u001F\u007F-\u009F\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069/\\:*?"<>|]/gu;

// A leading dot hides a file and a trailing dot or space is dropped by a file system rather than kept, so a name
// ending in either is a name that is not the one anybody agreed to.
const hiddenOrDropped = /^[.\s]+|[.\s]+$/gu;

/**
 * The name a downloaded file is offered under, reduced to something this client is willing to write.
 *
 * The service normalizes the name already, and this normalizes it again, because a file name is text a sender chose and
 * a value crossing into a `download` attribute is crossing into a place the operating system acts on. A separator, a
 * traversal segment, a control character, a bidirectional override, or a leading dot each mean something to a file
 * system or to whatever draws its listing that they do not mean in a message, so none of them survives — and a name
 * reduced to nothing is answered by the position the message gave the part rather than by a name invented for it.
 *
 * The override is the one worth naming. A sender who writes `invoice`, U+202E, then `fdp.exe` has written an
 * executable whose name a file listing draws as `invoiceexe.pdf`, because the override reverses everything after it —
 * so a reader deciding from what they can see decides about a file that is not the one they will open. Removing the
 * character is what keeps the drawn name and the real name one name.
 */
export function savedAs(fileName: string | null, position: number): string {
    const reduced = (fileName ?? '')
        .replaceAll(refusedInAFileName, '')
        .slice(0, longestSavedName)
        // Last, so that cutting a long name to length cannot put back the trailing dot or space the trim removes.
        .replaceAll(hiddenOrDropped, '');

    return reduced === '' ? `attachment-${position.toFixed(0)}` : reduced;
}
