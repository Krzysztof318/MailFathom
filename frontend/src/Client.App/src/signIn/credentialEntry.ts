// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one place in the client that turns a user name and a password into a credential, and nothing outside this module
// composes, inspects, or takes apart the value it produces. `Client.Backend` receives it finished and sends it; a
// screen holds it only long enough to hand it on. Both of those are the rule rather than this module's own choice —
// what is this module's is that there is exactly one implementation of RFC 7617 in the client to review.

/** Why what somebody typed is not a credential this client will present. */
export type CredentialEntryRefusal = 'incomplete' | 'userNameHasColon' | 'tooLong';

/**
 * The most of either half this client will present.
 *
 * The bound is here rather than on the two inputs alone, because an input's `maxLength` truncates a paste without
 * saying so — and a password silently shortened is a credential different from the one somebody was given, refused by
 * the deployment and reported as a wrong password. That is the failure `userNameHasColon` refuses by name, and this
 * refuses it the same way. A credential is a name and a password rather than a document, and the value composed from
 * them travels on every request this client makes.
 */
export const longestCredentialPart = 256;

/** The finished header value for what somebody typed, or why there is none. */
export type CredentialEntryResult =
    | { readonly outcome: 'resolved'; readonly authorization: string }
    | { readonly outcome: 'refused'; readonly refusal: CredentialEntryRefusal };

/**
 * Turns a user name and a password into the `Authorization` header value they are presented as.
 *
 * The encoding is UTF-8 before base64, which is the one `charset` RFC 7617 defines and the one every MailFathom
 * surface challenges with — so a password carrying anything outside US-ASCII survives the round trip rather than
 * depending on what a client guessed.
 *
 * @param userName The owner's user name, which the scheme's own grammar refuses a colon inside.
 * @param password The password beside it, which may carry anything including a colon, up to the bound above.
 * @returns The finished header value, or the refusal naming why there is none.
 */
export function resolveCredentialEntry(userName: string, password: string): CredentialEntryResult {
    if (userName.length === 0 || password.length === 0) {
        return { outcome: 'refused', refusal: 'incomplete' };
    }

    if (userName.length > longestCredentialPart || password.length > longestCredentialPart) {
        return { outcome: 'refused', refusal: 'tooLong' };
    }

    // The separator is a colon and the scheme has no way to escape one, so a user name carrying it would be split by
    // the deployment at the wrong place and read as a shorter name and a longer password. Refusing it here says so;
    // sending it would be a credential silently different from the one that was typed.
    if (userName.includes(':')) {
        return { outcome: 'refused', refusal: 'userNameHasColon' };
    }

    return { outcome: 'resolved', authorization: `Basic ${base64(`${userName}:${password}`)}` };
}

/** What `btoa` needs: one octet of the UTF-8 encoding per character, rather than the string's own code points. */
function base64(text: string): string {
    const encoded = new TextEncoder().encode(text);
    let octets = '';

    for (const octet of encoded) {
        octets += String.fromCharCode(octet);
    }

    return btoa(octets);
}
