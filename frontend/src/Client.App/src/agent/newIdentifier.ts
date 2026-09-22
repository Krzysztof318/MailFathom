// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/**
 * A new random UUID, which is what the client names a conversation and a message with before the deployment has seen
 * either — so a post retried after a dropped answer is the same post rather than a second one.
 *
 * Built from `getRandomValues` rather than `randomUUID`, because the second exists only in a secure context and a
 * deployment reached over plain HTTP on a private network is not one.
 */
export function newIdentifier(): string {
    const octets = crypto.getRandomValues(new Uint8Array(16));

    // The version and the variant, which is what makes sixteen random octets a version 4 UUID.
    octets[6] = ((octets[6] ?? 0) & 0x0f) | 0x40;
    octets[8] = ((octets[8] ?? 0) & 0x3f) | 0x80;

    const hex = Array.from(octets, (octet) => octet.toString(16).padStart(2, '0')).join('');

    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
