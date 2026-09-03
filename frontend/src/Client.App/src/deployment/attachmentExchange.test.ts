// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { attachmentOctetsOf, deliveryFailureOf, drawnFrom, showingFailureOf } from './attachmentExchange';

// Three things are proven here. What an answer to the attachment route amounts to, asked of an answer this file
// constructed; what a screen draws those octets as, asked of the octets themselves; and what each outcome becomes in
// the record `Client.Backend` keeps of the request — asserted for every outcome rather than for the interesting ones,
// because that reading is what an operator's own dimension is built from, and an outcome mapped to the wrong reason is
// a dashboard saying a deployment is refusing what it delivered.
//
// The `fetch` that produces an answer is not called here and nothing patches it, which `frontend/tests/AGENTS.md`
// § *What is faked, and where* holds as a rule and `attachmentUpload.ts` beside this module already follows. A real
// exchange over the wire belongs to the browser suite, which drives the built bundle against a served deployment.

/** An answer whose octets arrive over a stream, which is the shape a ceiling has to hold during rather than after. */
function answering(status: number, body: Uint8Array = new Uint8Array(0)): Response {
    return new Response(
        new ReadableStream<Uint8Array>({
            start(controller) {
                controller.enqueue(body);
                controller.close();
            },
        }),
        { status },
    );
}

/** What the octets of an answer read back as, which is what a caller of `attachmentOctetsOf` receives on success. */
function octetsIn(answer: readonly Uint8Array<ArrayBuffer>[] | string): number[] {
    return typeof answer === 'string' ? [] : [...answer].flatMap((chunk) => [...chunk]);
}

describe('attachmentOctetsOf', () => {
    it('hands back the octets the answer carried, which is what a file is made of', async () => {
        expect(octetsIn(await attachmentOctetsOf(answering(200, new Uint8Array([1, 2, 3])), 1_024))).toStrictEqual([
            1, 2, 3,
        ]);
    });

    it('says how much has arrived as it arrives, so a wait can be drawn rather than guessed at', async () => {
        const reported: number[] = [];

        await attachmentOctetsOf(answering(200, new Uint8Array([1, 2, 3])), 1_024, (octets) => reported.push(octets));

        expect(reported).toStrictEqual([3]);
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ] as const)('refuses a %s with the reason that status stands for', async (status, refusal) => {
        expect(await attachmentOctetsOf(answering(status), 1_024)).toBe(refusal);
    });

    // The bound is the size the message described the part at, and it is held during the walk rather than after it:
    // what is being proven is that an answer larger than it never becomes a value this client holds.
    it('refuses an answer larger than the message said the file holds', async () => {
        expect(await attachmentOctetsOf(answering(200, new Uint8Array(1_025)), 1_024)).toBe('largerThanDescribed');
    });
});

describe('drawnFrom', () => {
    it('answers text decoded under the character set the message declared', async () => {
        const octets = [new Uint8Array([0x7a, 0x61, 0xbf, 0xf3, 0xb3, 0xe6])];

        expect(await drawnFrom(octets, { as: 'text', charset: 'iso-8859-2' })).toBe('zażółć');
    });

    it('answers text decoded as UTF-8 where the message named a character set nothing knows', async () => {
        const octets = [new TextEncoder().encode('zażółć')];

        expect(await drawnFrom(octets, { as: 'text', charset: 'nothing-anybody-ships' })).toBe('zażółć');
    });

    // A character split across two chunks is what one decoder for the whole read exists for: a decoder built per chunk
    // would emit a replacement character in the place of the letter the second chunk completes.
    it('joins a letter the answer split across two chunks rather than drawing it as damage', async () => {
        const written = new TextEncoder().encode('zażółć');
        const octets = [written.slice(0, 3), written.slice(3)];

        expect(await drawnFrom(octets, { as: 'text', charset: 'utf-8' })).toBe('zażółć');
    });

    it('answers a picture as an address the client may draw it at, under the general binary type', async () => {
        expect(await drawnFrom([new Uint8Array([1, 2, 3])], { as: 'picture' })).toBe(
            'data:application/octet-stream;base64,AQID',
        );
    });
});

describe('deliveryFailureOf', () => {
    it.each(['delivered', 'abandoned'] as const)('reports %s as an answer the client acted on', (outcome) => {
        expect(deliveryFailureOf(outcome)).toBeNull();
    });

    it('reports a file larger than the message described as a body this client refused', () => {
        expect(deliveryFailureOf('largerThanDescribed')).toBe('unreadable');
    });

    it.each(['unauthenticated', 'unauthorized', 'unavailable'] as const)(
        'reports %s as the failure that word already names on this surface',
        (outcome) => {
            expect(deliveryFailureOf(outcome)).toBe(outcome);
        },
    );
});

describe('showingFailureOf', () => {
    it('reports a file that was shown as no failure at all', () => {
        expect(showingFailureOf({ outcome: 'shown', content: 'anything' })).toBeNull();
    });

    it('reports octets nothing could be drawn from as a body this client could not read', () => {
        expect(showingFailureOf({ outcome: 'refused', refusal: 'unreadable' })).toBe('unreadable');
    });

    it('reports a read the screen gave up on as an answer the client acted on rather than as a failure', () => {
        expect(showingFailureOf({ outcome: 'refused', refusal: 'abandoned' })).toBeNull();
    });

    // Every refusal a download can answer with, by the reading that download is given: this function is the callback
    // an operator's own dimension is built from, so a value delegated wrongly is a dashboard saying a deployment is
    // refusing what it delivered — which is why the coverage is exhaustive here as it is above.
    it.each([
        ['unauthenticated', 'unauthenticated'],
        ['unauthorized', 'unauthorized'],
        ['unavailable', 'unavailable'],
        ['largerThanDescribed', 'unreadable'],
    ] as const)('reports a %s refusal as the failure a download reports it as', (refusal, reported) => {
        expect(showingFailureOf({ outcome: 'refused', refusal })).toBe(reported);
    });
});
