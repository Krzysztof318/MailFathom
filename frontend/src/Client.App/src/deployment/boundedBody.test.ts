// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readBoundedContent } from './boundedBody';

/** An answer whose octets arrive in pieces, which is the shape the ceiling has to hold during rather than after. */
function answering(...pieces: readonly number[]): Response {
    return new Response(
        new ReadableStream<Uint8Array>({
            start(controller) {
                for (const octets of pieces) {
                    controller.enqueue(new Uint8Array(octets));
                }

                controller.close();
            },
        }),
    );
}

function octetsOf(chunks: readonly Uint8Array[]): number {
    return chunks.reduce((total, chunk) => total + chunk.byteLength, 0);
}

describe('readBoundedContent', () => {
    it('reads an answer that stays within what it was allowed to hold', async () => {
        const read = await readBoundedContent(answering(4, 4, 2), 16);

        expect(Array.isArray(read) ? octetsOf(read) : read).toBe(10);
    });

    it('reads an answer that is exactly as large as it was allowed to be', async () => {
        const read = await readBoundedContent(answering(8, 8), 16);

        expect(Array.isArray(read) ? octetsOf(read) : read).toBe(16);
    });

    it('refuses an answer larger than it was allowed to be as soon as the octets pass the ceiling', async () => {
        const read = await readBoundedContent(answering(8, 8, 8), 16);

        expect(read).toBe('largerThanDescribed');
    });

    it('reports an answer carrying no body at all as unavailable rather than as empty', async () => {
        const read = await readBoundedContent(new Response(null), 16);

        expect(read).toBe('unavailable');
    });

    it('says how much has arrived as it arrives, which is what a screen showing progress reads', async () => {
        const reported: number[] = [];

        await readBoundedContent(answering(4, 4, 2), 16, (octets) => reported.push(octets));

        expect(reported).toStrictEqual([4, 8, 10]);
    });
});
