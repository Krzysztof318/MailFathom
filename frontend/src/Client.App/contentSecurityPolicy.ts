// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createHash } from 'node:crypto';
import { linkScript, measuringScript } from './src/messageBody/frameScripts';

// The content security policy both heads are served under, stated once for both and read by Node alone:
// `vite.config.ts` writes the web head's into the bundle, which is how the service attaches it to every file it serves
// beneath `/app/`, and `src-tauri/run-tauri.ts` merges the desktop head's into the shell's configuration. So the two
// scripts the markup frame prepends are admitted by hashes computed from their own text at build time, and no digest is
// ever copied by hand into a second place to disagree with the first.
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

const bothHeads = {
    'default-src': "'self'",

    // The bundle, and the two scripts `MessageMarkupFrame.tsx` writes into a frame's `srcdoc`. A `srcdoc` document
    // inherits the policy of the page embedding it, so without their hashes the embedded view could never measure its
    // height and a link the sender wrote would report nothing — and neither failure says anything on the screen.
    'script-src': ["'self'", admittedByHash(linkScript), admittedByHash(measuringScript)].join(' '),

    // `'unsafe-inline'` because the sender's own `style` attributes and `<style>` elements are what the full-HTML
    // dialog and the embedded view exist to show, and a framed document inherits this directive with the rest.
    'style-src': "'self' 'unsafe-inline'",

    // Any host, because a message's pictures come from whatever server its sender named: asking to load them re-reads
    // that one message with its remote addresses left in, so this cannot be narrowed to the page's own origin. `data:`
    // is a picture the message carried inline and one an attachment is drawn as.
    'img-src': "'self' data: https: http:",

    // The typeface is committed into the bundle, and nothing a screen draws reaches an external origin for one.
    'font-src': "'self'",

    // An attached PDF is drawn by the engine's own viewer from an object URL this document minted.
    'frame-src': "'self' blob:",

    'object-src': "'none'",
    'base-uri': "'none'",
    'form-action': "'none'",
    'frame-ancestors': "'none'",
} as const;

/** The web head, whose page is served by the deployment it calls and so reaches that origin and no other. */
export const webHeadDirectives: ContentSecurityPolicyDirectives = { ...bothHeads, 'connect-src': "'self'" };

/**
 * The desktop head, which loads its page from a scheme of its own and calls whatever deployment its user names.
 *
 * `connect-src` stays wide on purpose, and it is the one directive the two heads differ in. Which deployment this head
 * belongs to is decided at run time — configured, or typed on the sign-in screen — while the policy is fixed when the
 * shell is built, so no host can be named here. What it narrows is the scheme: `https:` and `wss:` for the requests and
 * the signal channel, `http:` and `ws:` only because a deployment may be addressed in clear text where somebody
 * declared that permission, and `ipc:` and `http://ipc.localhost`, which are how the page reaches the shell's own
 * commands.
 */
export const desktopHeadDirectives: ContentSecurityPolicyDirectives = {
    ...bothHeads,
    'connect-src': "'self' ipc: http://ipc.localhost https: wss: http: ws:",
};

/** The directives written as one `Content-Security-Policy` header value. */
export function asHeaderValue(directives: ContentSecurityPolicyDirectives): string {
    return Object.entries(directives)
        .map(([directive, sources]) => `${directive} ${sources}`)
        .join('; ');
}
