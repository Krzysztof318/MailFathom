// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a downloaded file is named on somebody's machine. It sits apart from the row that offers the download because a
// module Vite hot-reloads may export components alone, which is the same reason `localization/useLocalization.ts` sits
// apart from the provider that fills it.

// The most of a sender's own file name this client will write to somebody's machine.
const longestSavedName = 128;

/**
 * The name a downloaded file is offered under, reduced to something this client is willing to write.
 *
 * The service normalizes the name already, and this normalizes it again, because a file name is text a sender chose and
 * a value crossing into a `download` attribute is crossing into a place the operating system acts on. A separator, a
 * traversal segment, a control character, or a leading dot each mean something to a file system that they do not mean in
 * a message, so none of them survives — and a name reduced to nothing is answered by the position the message gave the
 * part rather than by a name invented for it.
 */
export function savedAs(fileName: string | null, position: number): string {
    const reduced = (fileName ?? '')
        // A control character in a file name is exactly what has to go, so naming that range is the point of the
        // pattern rather than an accident of it.
        // eslint-disable-next-line no-control-regex
        .replaceAll(/[\u0000-\u001F\u007F/\\:*?"<>|]/gu, '')
        .replaceAll(/^[.\s]+|[.\s]+$/gu, '')
        .slice(0, longestSavedName);

    return reduced === '' ? `attachment-${position.toFixed(0)}` : reduced;
}
