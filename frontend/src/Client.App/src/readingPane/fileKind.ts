// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What kind of file a badge names, so that a reader tells a document from a spreadsheet without reading a file name.
//
// It is answered from the media type the message declares the part under before anything else, because that is what
// MailFathom recorded the part as; a name is text a sender wrote, and a spreadsheet called `report.pdf` would otherwise
// be drawn as a document. The name is what answers it where the declared type names no family — a sender whose client
// declared every attachment `application/octet-stream` still has an extension worth showing.

/** The families a badge names, by the media type a message declares one under. */
const families: Readonly<Record<string, string | undefined>> = {
    'application/pdf': 'pdf',
    'application/msword': 'doc',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document': 'docx',
    'application/vnd.ms-excel': 'xls',
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': 'xlsx',
    'application/vnd.ms-powerpoint': 'ppt',
    'application/vnd.openxmlformats-officedocument.presentationml.presentation': 'pptx',
};

// The most of a kind the badge shows. A kind that falls through to the name or the subtype can be long enough to be a
// sentence; the badge is a glance rather than a description.
const longestKind = 8;

/**
 * The kind of file a badge names: the family the declared media type belongs to, and the name's extension or the
 * declared subtype for anything no family covers.
 *
 * @param fileName The name the message carries for the part, already normalized, or nothing where it carries none.
 * @param mediaType What the message declares the part to be, parameters and casing as the sender wrote them.
 */
export function kindOf(fileName: string | null, mediaType: string): string {
    const declared = mediaType.split(';')[0]?.trim().toLowerCase() ?? '';
    const family = families[declared] ?? (declared.startsWith('image/') ? 'image' : undefined);

    if (family !== undefined) {
        return family;
    }

    const extension = /\.([A-Za-z0-9]{1,8})$/u.exec(fileName ?? '')?.[1];
    const subtype = declared.split('/')[1]?.split(/[+.]/u)[0] ?? '';

    return (extension ?? subtype).slice(0, longestKind);
}
