// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { mostRecipientsInOneHeader, type Composition } from './composition';

// Where what somebody is typing survives a reload, which a single-page application makes a cold start rather than a
// way out. It is the local draft the composer keeps continuously, and it is a different thing from the draft in the
// owner's own drafts folder: saving to the deployment is an act somebody asks for, because every revision of that one
// reaches their mail server.
//
// **The session's store rather than the machine's, and this is the reason it is not the device store beside it.** What
// is kept here is mail somebody is writing — words, a subject, and the addresses they are for — which is personal data
// under the same rules as the mail already on the screen. A store that dies with the tab keeps it out of a machine two
// people share and out of anything reading the origin's storage tomorrow, and signing out drops it in the same act that
// empties the workspace. It is the same bound the web head keeps its credential under, for a related reason.
//
// Reached as `window.sessionStorage` rather than as the bare global for the reason `device/deviceStore.ts` gives: Node
// publishes stores of its own that win over the document's under the test runner.
const storageKey = 'mailfathom.composition';

// What a kept composition may carry before it is read as somebody's edit rather than as this client's own writing. The
// address is the longest one the mail standards allow, the subject is a header line, and the words are held to the size
// the client surface accepts a whole draft at — each far above anything the composer itself writes there.
const longestAddress = 320;
const longestSubject = 998;
const longestWords = 2 * 1024 * 1024;
const longestIdentifier = 256;

const answers: readonly MailDraftAnswer[] = ['senderOnly', 'everyone', 'forward'];

/** What this tab was writing, or `null` where it was writing nothing or what was kept is not a composition. */
export function rememberedComposition(): Composition | null {
    let stored: string | null;

    try {
        stored = window.sessionStorage.getItem(storageKey);
    } catch {
        return null;
    }

    if (stored === null) {
        return null;
    }

    try {
        return compositionIn(JSON.parse(stored));
    } catch {
        return null;
    }
}

/** Keeps what this tab is writing, so a reload returns to it rather than to an empty message. */
export function rememberComposition(composition: Composition): void {
    try {
        window.sessionStorage.setItem(storageKey, JSON.stringify(composition));
    } catch {
        // A browser refusing storage still runs the client. What is being written then lasts as long as the screen
        // holding it, which is what a composer that failed over a store would have lost anyway.
    }
}

/** Drops what was being written, which is what sending it, giving it up, and signing out each do. */
export function forgetComposition(): void {
    try {
        window.sessionStorage.removeItem(storageKey);
    } catch {
        // A store that refuses a removal refused the write that would have put something there.
    }
}

// Read back as untrusted input, because a store is a place a person can write. Anything this client did not write is
// answered as nothing kept rather than as a message with a hole in it — which is what would otherwise reach the
// confirmation as a recipient nobody typed.
function compositionIn(value: unknown): Composition | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const account = record['account'];
    const subject = record['subject'];
    const words = record['words'];
    const answering = answeringIn(record['answering'] ?? null);
    const to = addressesIn(record['to']);
    const cc = addressesIn(record['cc']);
    const bcc = addressesIn(record['bcc']);

    if (answering === undefined || to === null || cc === null || bcc === null) {
        return null;
    }

    if (typeof account !== 'string' || account.length > longestIdentifier) {
        return null;
    }

    if (typeof subject !== 'string' || subject.length > longestSubject) {
        return null;
    }

    if (typeof words !== 'string' || words.length > longestWords) {
        return null;
    }

    return { answering, account, subject, to, cc, bcc, words };
}

// Answers `undefined` for a shape it refuses, because `null` is what a message of its own legitimately kept.
function answeringIn(value: unknown): Composition['answering'] | undefined {
    if (value === null) {
        return null;
    }

    if (typeof value !== 'object' || Array.isArray(value)) {
        return undefined;
    }

    const record = value as Record<string, unknown>;
    const storedEmailId = record['storedEmailId'];
    const answered = record['answers'];

    if (typeof storedEmailId !== 'string' || storedEmailId.length > longestIdentifier) {
        return undefined;
    }

    return answers.includes(answered as MailDraftAnswer)
        ? { storedEmailId, answers: answered as MailDraftAnswer }
        : undefined;
}

function addressesIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostRecipientsInOneHeader) {
        return null;
    }

    const addresses: string[] = [];

    for (const address of value) {
        if (typeof address !== 'string' || address.length === 0 || address.length > longestAddress) {
            return null;
        }

        addresses.push(address);
    }

    return addresses;
}
