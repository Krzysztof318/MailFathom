// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/**
 * A new version 7 UUID, which is what the client names a conversation and a message with before the deployment has
 * seen either — so a post retried after a dropped answer is the same post rather than a second one. Version 7 because
 * every identifier MailFathom mints is one, which ADR 0036 records.
 *
 * Built by hand from `getRandomValues` rather than `randomUUID`, because the second mints only version 4 and exists only
 * in a secure context, and a deployment reached over plain HTTP on a private network is not one.
 */
export function newIdentifier(mintedAt: number = Date.now()): string {
    const octets = crypto.getRandomValues(new Uint8Array(16));

    // The leading 48 bits are the millisecond, big-endian. Division rather than shifts, which JavaScript truncates to 32 bits.
    let milliseconds = mintedAt;
    for (let index = 5; index >= 0; index--) {
        octets[index] = milliseconds % 256;
        milliseconds = Math.floor(milliseconds / 256);
    }

    octets[6] = ((octets[6] ?? 0) & 0x0f) | 0x70;
    octets[8] = ((octets[8] ?? 0) & 0x3f) | 0x80;

    const hex = Array.from(octets, (octet) => octet.toString(16).padStart(2, '0')).join('');

    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
