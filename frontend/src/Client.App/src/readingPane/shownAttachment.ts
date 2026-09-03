// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAttachment } from '@mailfathom/client-backend';
import type { ShownAs } from '../deployment/attachmentExchange';

// What the client will show a reader inside itself, and what it hands them to keep instead. It is decided from the
// description the message published rather than from anything fetched, so a file too large or of a kind this client
// does not draw says so before a single octet crosses the wire.
//
// **Two shapes are drawn and everything else is downloaded**, which is the whole of the decision:
//
// - **A picture, of a raster kind this client names.** An `img` element draws octets and does nothing else with them:
//   it runs no script, resolves no reference, and fetches nothing, whatever the octets turn out to hold. That property
//   belongs to the element rather than to a setting, which is the distinction
//   [ADR 0024](../../../../docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) draws between
//   what is load-bearing and what is defence in depth, and it is why the list below is closed.
// - **Text, under the character set the message declared.** React escapes it, so the most a file of markup can be here
//   is its own source read as words.
//
// `image/svg+xml` is deliberately not in the list although a browser would draw it in an `img`. What makes that safe is
// the *secure static mode* an engine applies to an SVG document loaded as an image — scripts and external references
// suppressed — which is a mode two rendering engines implement rather than a property of the markup. ADR 0024's own
// reading is that a tracking pixel is defeated by absence rather than by a setting, and an SVG a sender wrote is markup
// this client would be trusting an engine not to run. It downloads, like every other kind.
//
// **A PDF downloads as well, and that is the one refusal worth saying out loud**, because a mail client that previewed
// PDFs is what a reader expects. Nothing in this tree can draw one for both heads: the web head and the Windows desktop
// head embed a browser that renders PDFs, and the Linux desktop head runs WebKitGTK, which carries no PDF viewer at
// all — so an embedded viewer would be a feature that exists on some of the platforms this client ships to, which is
// exactly the divergence `frontend/src/AGENTS.md` § *The two heads* refuses. Rendering one in the client instead means
// a PDF engine as a dependency, which is a permanent patch obligation taken on for the most hostile input this
// application handles, and ADR 0024 admits such a package only where something it does is load-bearing.

/** Why a file is not drawn inside the client, in the two ways that can be true. */
export type NotShown = 'kindNotShown' | 'largerThanShown';

/** What the viewer does with one file: the form it draws it in, or why it draws none and offers the download instead. */
export type ShownAttachment = ShownAs | NotShown;

// The raster kinds an `img` is given, by the media type the message declared. It is a list rather than an `image/`
// prefix so that a kind nobody weighed is downloaded rather than drawn: what admits a new one is somebody deciding it,
// which is the same shape the document contract has in ADR 0024.
const picturesDrawn: readonly string[] = [
    'image/avif',
    'image/bmp',
    'image/gif',
    'image/jpeg',
    'image/png',
    'image/webp',
];

/**
 * The most a file of each shape may hold and still be drawn rather than downloaded.
 *
 * Two numbers rather than one because they bound two different costs. A picture is held as an address, which is the
 * octets again as text, and drawn as one element the browser scales; a few megabytes of that is a photograph somebody
 * sent. Text is held as a string and laid out word by word, so the same number there would be a screen that stops
 * responding while it measures a file nobody meant to read in a pane.
 *
 * Neither is a limit on what a person may have: a file over its number is offered as the download it has always been.
 */
const shownAtMost = { picture: 8 * 1024 * 1024, text: 1024 * 1024 };

/** What a file declares itself to be, without the parameters the sender wrote after it. */
export function mediaTypeOf(declared: string): string {
    return (declared.split(';')[0] ?? '').trim().toLowerCase();
}

/**
 * The character set the message declared for a file, or UTF-8 where it declared none.
 *
 * The parameter is read rather than parsed properly: it is handed to `TextDecoder`, which answers for a label it does
 * not know, so a value this reads wrongly costs a fallback to UTF-8 rather than a wrong decoding.
 */
export function charsetOf(declared: string): string {
    const written = declared
        .split(';')
        .slice(1)
        .map((parameter) => parameter.trim())
        .find((parameter) => parameter.toLowerCase().startsWith('charset='));

    return written === undefined ? 'utf-8' : written.slice('charset='.length).trim().replace(/^"|"$/gu, '');
}

/**
 * What the viewer does with one file a message carries.
 *
 * @param attachment What the message said about it, which is everything this decision reads.
 * @returns The form it is drawn in, or why it is downloaded instead.
 */
export function shownAttachment(attachment: MailAttachment): ShownAttachment {
    const mediaType = mediaTypeOf(attachment.mediaType);

    if (picturesDrawn.includes(mediaType)) {
        return attachment.sizeOctets > shownAtMost.picture ? 'largerThanShown' : { as: 'picture' };
    }

    if (mediaType.startsWith('text/')) {
        return attachment.sizeOctets > shownAtMost.text
            ? 'largerThanShown'
            : { as: 'text', charset: charsetOf(attachment.mediaType) };
    }

    return 'kindNotShown';
}
