// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What this client holds between starts, and the one place it is written down and read back. ADR 0023 decides where it
// is kept; this decides what shape the kept value has.
//
// It is a session rather than a password, which is what changed: the deployment derives the password once, at sign-in,
// and hands back a token that expires and that signing out revokes. So a value read out of the store by a script that
// reached the origin is worth what is left of one session rather than a password somebody may have reused elsewhere.
//
// Three fields rather than one, and each earns its place against the rule the credential used to be kept under. The
// header value is the credential, exactly as before. The instant is what the client renews against, and a session
// without one would be renewed either constantly or never. The person's name is here because it is no longer inside
// the credential: a Basic header carried it and a token does not, and the client needs it to say who is signed in and
// to key what this machine remembers per person. It is not a secret, and it is not the credential's other half.

import { isPresentableSessionCredential, longestCredentialPart } from './credentialEntry';

/** The finished header value, the instant it stops working, and who it belongs to. */
export interface KeptSession {
    /** The `Authorization` header value to present, which nothing outside `credentialEntry` composes or takes apart. */
    readonly authorization: string;

    /** When the deployment said the session stops working, as it wrote it. */
    readonly expiresAt: string;

    /** The name the deployment knows the person by, which is what a screen renders and what a per-person setting is written under. */
    readonly person: string;
}

/** The most a stored document may be before it is refused unread, which is far past what this client writes. */
const longestKeptSession = 2048;

/**
 * The stored form of a session, as one value the store keeps under one entry.
 *
 * @param session The session to keep.
 * @returns What to hand the credential store.
 */
export function writeKeptSession(session: KeptSession): string {
    return JSON.stringify({
        authorization: session.authorization,
        expiresAt: session.expiresAt,
        person: session.person,
    });
}

/**
 * What a stored value says, or `null` where it is not one this client wrote.
 *
 * A store that answers with something unreadable is a store holding nothing usable, so it is answered as nothing kept
 * and the person signs in again. That covers a value written by a release whose shape differed, a keychain entry
 * somebody edited, storage that returned a truncated string, and a header value a script on the origin wrote — none
 * of which is a credential worth presenting to a deployment. The credential is checked for the header it becomes
 * rather than for being a string, because that is what this read shares with the exchange: both are entry points into
 * a value the transport hands to `Headers` verbatim, and only one of them was written by this client.
 *
 * A session whose instant has passed is nothing kept either, and that is the ordinary case rather than a damaged
 * store: a twelve-hour session opened the next morning is over, and answering it as something kept would mount the
 * whole frame, start a read, meet a refusal, and drop somebody onto the sign-in screen under a notice about a
 * deployment that changed nothing. What is left of it is a sign-in screen rendered first, which is what this read
 * happens before anything is drawn for.
 *
 * @param stored What the credential store answered with.
 * @param readAt The instant to judge the expiry against, which is this machine's clock unless a caller states one.
 * @returns The session, or `null`.
 */
export function readKeptSession(stored: string | null, readAt: number = Date.now()): KeptSession | null {
    if (stored === null || stored.length === 0 || stored.length > longestKeptSession) {
        return null;
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(stored);
    } catch {
        return null;
    }

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return null;
    }

    const kept = parsed as Record<string, unknown>;
    const authorization = kept['authorization'];
    const expiresAt = kept['expiresAt'];
    const person = kept['person'];

    if (typeof authorization !== 'string' || !isPresentableSessionCredential(authorization)) {
        return null;
    }

    if (typeof expiresAt !== 'string' || !(Date.parse(expiresAt) > readAt)) {
        return null;
    }

    return typeof person === 'string' && person.length > 0 && person.length <= longestCredentialPart
        ? { authorization, expiresAt, person }
        : null;
}
