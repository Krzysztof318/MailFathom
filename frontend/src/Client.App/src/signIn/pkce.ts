// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The three secrets one authorization request is started with, and the one place in the client that generates any of
// them. They are here rather than in `Client.Backend` because every one of them comes out of the platform's
// cryptographically secure generator and its digest, and that package declares no browser API at all — so what it
// composes is the address, and what generates what the address carries is this.
//
// Each of the three answers a different attack and none of them substitutes for another. The verifier is what proves
// that whoever redeems the code is whoever asked for it, which is the whole of RFC 7636 and the reason a page needs no
// client secret. The state is what makes a redirect somebody else started unusable, because an answer carrying a state
// this tab did not generate is discarded unread. The nonce is what an OpenID provider binds its identity token to; this
// client asks for no identity token, and sends one anyway where the server is a provider, because a provider is
// entitled to expect it and a request without one is a request some of them refuse.

/**
 * The number of random octets behind each value.
 *
 * RFC 7636 puts the verifier between 43 and 128 characters of its own alphabet; 32 octets encode to 43 and are 256 bits,
 * which is the floor every recommendation on this agrees on. The state and the nonce are the same size for the same
 * reason and because there is no argument for making either of them smaller.
 */
const secretOctets = 32;

/** What one authorization request is started with, and what the answer to it is checked against. */
export interface AuthorizationAttempt {
    /** The single-use value the answer must echo, which is what makes a redirect this tab did not start unusable. */
    readonly state: string;

    /** The secret the code is redeemed with, which never leaves this client until the token request carries it. */
    readonly codeVerifier: string;

    /** The `S256` transform of the verifier, which is the only thing about it the authorization request carries. */
    readonly codeChallenge: string;

    /** The value an OpenID provider binds its identity token to, sent only where the server is one. */
    readonly nonce: string;
}

/**
 * Generates the secrets one authorization request is started with.
 *
 * @returns The attempt, whose verifier the caller keeps until the code it started is redeemed or discarded.
 */
export async function beginAuthorizationAttempt(): Promise<AuthorizationAttempt> {
    const codeVerifier = randomValue();

    return {
        state: randomValue(),
        codeVerifier,
        codeChallenge: await challengeFor(codeVerifier),
        nonce: randomValue(),
    };
}

/** A value with {@link secretOctets} octets of entropy behind it, written in the unpadded alphabet RFC 7636 fixes. */
function randomValue(): string {
    return base64Url(crypto.getRandomValues(new Uint8Array(secretOctets)));
}

/** The `S256` challenge: the SHA-256 of the verifier's ASCII octets, in the same alphabet. */
async function challengeFor(codeVerifier: string): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier));

    return base64Url(new Uint8Array(digest));
}

/**
 * Base64 in the URL alphabet with no padding, which is what RFC 7636 states the challenge is written in.
 *
 * Written out because the platform offers no encoder for this alphabet, and `btoa` reads one octet per character,
 * which is why the octets are widened into a string first.
 */
function base64Url(octets: Uint8Array): string {
    let characters = '';

    for (const octet of octets) {
        characters += String.fromCharCode(octet);
    }

    return btoa(characters).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}
