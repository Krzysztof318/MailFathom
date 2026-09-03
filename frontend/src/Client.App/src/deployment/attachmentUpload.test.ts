// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { answerOf } from './attachmentUpload';

// What the client decides about an answer to an upload, asked of an answer this file constructed. The request that
// produces one calls `fetch`, which nothing here patches — `frontend/tests/AGENTS.md` § *What is faked, and where*
// holds that rule, and it is why the deciding half is a function of its own.

/** An answer whose octets arrive over a stream, which is the shape a ceiling has to hold during rather than after. */
function answering(body: string, status = 200): Response {
    const octets = new TextEncoder().encode(body);

    return new Response(
        new ReadableStream<Uint8Array>({
            start(controller) {
                controller.enqueue(octets);
                controller.close();
            },
        }),
        { status, headers: { 'Content-Type': 'application/json' } },
    );
}

describe('answerOf', () => {
    it('hands back what came, under the status and the headers it came under', async () => {
        expect(await answerOf(answering('{"attachmentId":"a1"}'), 64)).toStrictEqual({
            status: 200,
            body: '{"attachmentId":"a1"}',
            headers: { 'content-type': 'application/json' },
        });
    });

    it('keeps a refusal its status, the body being what the caller reads the reason out of', async () => {
        expect(await answerOf(answering('{"errorCode":57002}', 402), 64)).toMatchObject({
            status: 402,
            body: '{"errorCode":57002}',
        });
    });

    it('answers nothing for a body past what the request said it would read, which is refused as unreadable', async () => {
        expect(await answerOf(answering('x'.repeat(65)), 64)).toMatchObject({ body: '' });
    });

    it('counts the answer in the octets the bound is stated in rather than in the characters they decode to', async () => {
        // Thirty-three characters, two octets each: under the bound read as a string, and over it read as what the
        // bound actually counts.
        const past = 'ą'.repeat(33);

        expect(past.length).toBeLessThan(64);
        expect(await answerOf(answering(past), 64)).toMatchObject({ body: '' });
    });

    it('answers nothing for an answer carrying no body at all, rather than reading absence as empty', async () => {
        expect(await answerOf(new Response(null, { status: 204 }), 64)).toMatchObject({ status: 204, body: '' });
    });
});
