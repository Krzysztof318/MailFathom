// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createHash } from 'node:crypto';
import { linkScript, measuringScript } from './src/messageBody/frameScripts';

// The content security policy the client is served under, read by Node alone: `vite.config.ts` writes the web head's
// into the bundle, which is how the service attaches it to every file it serves beneath `/app/`, and
// `src-tauri/run-tauri.ts` spreads the directives both heads share into the desktop head's own. So the two scripts the
// markup frame prepends are admitted by hashes computed from their own text at build time, and no digest is ever copied
// by hand into a second place to disagree with the first.
//
// ADR 0024 calls this policy defence in depth, and that is exactly its reach: the reading pane's safety rests on no
// string from a message ever becoming markup. What the policy limits is a defect somewhere else in the client — which
// scripts can run in the page at all, and where anything they read could be sent.

/** A policy as its directives, each naming the sources it admits. */
export type ContentSecurityPolicyDirectives = Readonly<Record<string, string>>;

/** The file the build writes beside the entry document, holding the web head's policy as one header value. */
export const contentSecurityPolicyDocument = 'content-security-policy.txt';

function admittedByHash(script: string): string {
    return `'sha256-${createHash('sha256').update(script, 'utf8').digest('base64')}'`;
}

/** Every directive but `connect-src`, which is the one each head decides for itself. */
export const directivesBothHeadsShare: ContentSecurityPolicyDirectives = {
    'default-src': "'self'",

    // The bundle, and the two scripts `MessageMarkupFrame.tsx` writes into a frame's `srcdoc`. A `srcdoc` document
    // inherits the policy of the page embedding it, so without their hashes the embedded view could never measure its
    // height and a link the sender wrote would report nothing — and neither failure says anything on the screen.
    'script-src': ["'self'", admittedByHash(linkScript), admittedByHash(measuringScript)].join(' '),

    // `'unsafe-inline'` because the sender's own `style` attributes and `<style>` elements are what the full-HTML
    // dialog and the embedded view exist to show, and a framed document inherits this directive with the rest.
    'style-src': "'self' 'unsafe-inline'",

    // Any host, because a message's pictures and backgrounds come from whatever server its sender named: asking to load
    // them re-reads that one message with its remote addresses left in, so this cannot be narrowed to the page's own
    // origin. `data:` is a picture the message carried inline and one an attachment is drawn as.
    'img-src': "'self' data: https: http:",

    // The same hosts for the same reason: the ask restores the sender's web font beside the pictures, and a framed
    // document inherits this directive. The client's own typeface is committed into the bundle and is `'self'`.
    'font-src': "'self' data: https: http:",

    // An attached PDF is drawn by the engine's own viewer from an object URL this document minted.
    'frame-src': "'self' blob:",

    'object-src': "'none'",
    'base-uri': "'none'",
    'form-action': "'none'",
    'frame-ancestors': "'none'",
};

/**
 * The web head, whose page is served by the deployment it calls and so reaches that origin and no other.
 *
 * `'self'` is the whole of `connect-src` on purpose, and it is the directive that bounds where anything a script read
 * could be sent. A page a deployment served resolves to that deployment and offers no control for naming another, so
 * nothing this head does legitimately leaves its origin; the one case that asks for an address is a page whose own
 * origin the client could not address at all, and a page there that sent a credential to a second host is the
 * exfiltration this directive exists to refuse.
 */
export const webHeadDirectives: ContentSecurityPolicyDirectives = {
    ...directivesBothHeadsShare,
    'connect-src': "'self'",
};

/** The directives written as one `Content-Security-Policy` header value. */
export function asHeaderValue(directives: ContentSecurityPolicyDirectives): string {
    return Object.entries(directives)
        .map(([directive, sources]) => `${directive} ${sources}`)
        .join('; ');
}
