// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { beginAuthorizationAttempt, type AuthorizationAttempt } from './pkce';

/** The alphabet RFC 7636 fixes for the verifier and the challenge, with no padding in it. */
const base64Url = /^[A-Za-z0-9\-_]+$/u;

/** The `S256` transform as the specification states it, so the assertion is against RFC 7636 and not against itself. */
async function statedChallengeFor(codeVerifier: string): Promise<string> {
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier)));
    let characters = '';

    for (const octet of digest) {
        characters += String.fromCharCode(octet);
    }

    return btoa(characters).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

/** One attempt, refused as a test failure where the platform offered no digest — which is its own test below. */
async function attemptHere(): Promise<AuthorizationAttempt> {
    const attempt = await beginAuthorizationAttempt();

    if (attempt === null) {
        throw new Error('This platform offered no digest, which is the one test below rather than every test here.');
    }

    return attempt;
}

describe('beginAuthorizationAttempt', () => {
    it('writes every secret in the alphabet the specification fixes, unpadded', async () => {
        const attempt = await attemptHere();

        for (const secret of [attempt.state, attempt.codeVerifier, attempt.codeChallenge, attempt.nonce]) {
            expect(secret).toMatch(base64Url);
        }
    });

    // 32 octets encode to 43 characters, which is the floor RFC 7636 states for a verifier and the size every
    // recommendation on the other two agrees on.
    it('puts 256 bits behind each of the three it generates', async () => {
        const attempt = await attemptHere();

        expect(attempt.state).toHaveLength(43);
        expect(attempt.codeVerifier).toHaveLength(43);
        expect(attempt.nonce).toHaveLength(43);
    });

    // Three values answering three different attacks, and one reused for another would defeat the one it was reused
    // from: a state anybody can read off an address bar is not a secret to redeem a code with.
    it('generates each of the three independently of the others', async () => {
        const attempt = await attemptHere();

        expect(new Set([attempt.state, attempt.codeVerifier, attempt.nonce]).size).toBe(3);
    });

    it('generates a different attempt every time it is asked', async () => {
        const first = await attemptHere();
        const second = await attemptHere();

        expect(second.codeVerifier).not.toBe(first.codeVerifier);
        expect(second.state).not.toBe(first.state);
    });

    it('states the verifier nowhere in the challenge, which is the whole of what the challenge is for', async () => {
        const attempt = await attemptHere();

        expect(attempt.codeChallenge).not.toBe(attempt.codeVerifier);
        expect(attempt.codeChallenge).toHaveLength(43);
    });

    it('challenges with the S256 transform of the verifier it generated, which is what the server checks', async () => {
        const attempt = await attemptHere();

        expect(attempt.codeChallenge).toBe(await statedChallengeFor(attempt.codeVerifier));
    });

    // A document served over plain HTTP is an insecure context, which a deployment somebody permitted clear text for
    // produces — and there `crypto.subtle` is gone while `getRandomValues` is not. Answering nothing is what lets the
    // screen say the sign-in could not be started instead of standing behind a spinner a rejected promise never clears.
    it('answers nothing where the platform offers no digest, rather than throwing out of the caller', async () => {
        const digests = crypto.subtle;

        Object.defineProperty(crypto, 'subtle', { configurable: true, value: undefined });

        try {
            await expect(beginAuthorizationAttempt()).resolves.toBeNull();
        } finally {
            Object.defineProperty(crypto, 'subtle', { configurable: true, value: digests });
        }
    });
});
