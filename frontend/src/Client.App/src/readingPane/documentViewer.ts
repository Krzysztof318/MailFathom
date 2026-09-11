// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Whether the engine this client is running in draws a PDF itself.
//
// **It is a question about the platform rather than about the head**, which is the whole of why it may be asked at all:
// `frontend/src/AGENTS.md` § *The two heads* refuses a branch on which head is underneath and grants exactly this shape
// beside it — an operation a head does not offer is a question the application asks rather than a failure it meets.
// Nothing here says desktop or web, and nothing here would change if a third head appeared; what it reads is a standard
// property the HTML Standard defines for this purpose, and a head that answers `false` gets the download the viewer has
// always offered rather than an empty frame.
//
// The answer happens to fall along the heads today — the web head and the Windows desktop head embed engines that
// carry a viewer, and WebKitGTK on the Linux desktop head carries none — and that is a fact about those engines rather
// than a rule this module encodes. It is also why the refusal that stood in `shownAttachment.ts` is gone: what made an
// embedded viewer a divergence was the client deciding per target, and a client that asks instead has one behaviour
// with two honest answers.
//
// Read at the moment a file is opened rather than once at the start, because a WebView's own settings can change under
// a running desktop application, and because a value captured at start-up is one a test cannot vary per case.

/**
 * Whether this engine renders a PDF inside the page, which decides whether a document is drawn or offered to keep.
 *
 * @returns `true` where the engine has a viewer of its own, and `false` where it has none.
 */
export function drawsDocumentsInline(): boolean {
    return navigator.pdfViewerEnabled;
}
