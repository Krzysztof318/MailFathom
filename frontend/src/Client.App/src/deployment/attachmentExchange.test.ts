// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest } from '@mailfathom/client-backend';
import { attachmentExchange, deliveryFailureOf, showingFailureOf } from './attachmentExchange';

// Two things are proven here. What a download and a read become in the record `Client.Backend` keeps of them, asserted
// for every outcome rather than for the interesting ones, because that reading is what an operator's own dimension is
// built from — an outcome mapped to the wrong reason is a dashboard saying a deployment is refusing what it delivered.
// And what a read answers, which is the one place in the client octets a sender composed become a value a screen holds.
//
// The read is proven against a real `Response` handed to a `fetch` this file supplies, because this module *is* the
// call to `fetch`: there is no seam underneath it, and a test that replaced the module would assert nothing about the
// status mapping, the bound, or the decoding that are the whole of what it does.

const request: ClientRequest = {
    method: 'GET',
    path: 'https://mail.example.invalid/api/client/messages/m/attachments/0',
    headers: { Accept: 'application/octet-stream' },
    longestAnswer: 1_024,
};

function answering(status: number, body?: BodyInit): void {
    vi.stubGlobal('fetch', () => Promise.resolve(new Response(body ?? new Uint8Array(0), { status })));
}

afterEach(() => {
    vi.unstubAllGlobals();
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

    it('reports a refusal a download shares by the same reading that download is given', () => {
        expect(showingFailureOf({ outcome: 'refused', refusal: 'unauthorized' })).toBe('unauthorized');
    });
});

describe('attachmentExchange.read', () => {
    it('answers text decoded under the character set the message declared', async () => {
        answering(200, new Uint8Array([0x7a, 0x61, 0xbf, 0xf3, 0xb3, 0xe6]));

        const read = await attachmentExchange.read(
            request,
            { as: 'text', charset: 'iso-8859-2' },
            new AbortController().signal,
        );

        expect(read).toEqual({ outcome: 'shown', content: 'zażółć' });
    });

    it('answers text decoded as UTF-8 where the message named a character set nothing knows', async () => {
        answering(200, new TextEncoder().encode('zażółć'));

        const read = await attachmentExchange.read(
            request,
            { as: 'text', charset: 'nothing-anybody-ships' },
            new AbortController().signal,
        );

        expect(read).toEqual({ outcome: 'shown', content: 'zażółć' });
    });

    it('answers a picture as an address the client may draw it at, under the general binary type', async () => {
        answering(200, new Uint8Array([1, 2, 3]));

        const read = await attachmentExchange.read(request, { as: 'picture' }, new AbortController().signal);

        expect(read).toEqual({ outcome: 'shown', content: 'data:application/octet-stream;base64,AQID' });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ] as const)('refuses a %s with the reason that status stands for', async (status, refusal) => {
        answering(status);

        const read = await attachmentExchange.read(request, { as: 'picture' }, new AbortController().signal);

        expect(read).toEqual({ outcome: 'refused', refusal });
    });

    it('refuses an answer larger than the message said the file holds', async () => {
        answering(200, new Uint8Array(1_025));

        const read = await attachmentExchange.read(request, { as: 'picture' }, new AbortController().signal);

        expect(read).toEqual({ outcome: 'refused', refusal: 'largerThanDescribed' });
    });

    it('refuses a deployment that did not answer at all', async () => {
        vi.stubGlobal('fetch', () => Promise.reject(new Error('nothing there')));

        const read = await attachmentExchange.read(request, { as: 'picture' }, new AbortController().signal);

        expect(read).toEqual({ outcome: 'refused', refusal: 'unavailable' });
    });

    it('reports a read the screen gave up on as abandoned rather than as a deployment that failed', async () => {
        const abandoning = new AbortController();
        abandoning.abort();
        vi.stubGlobal('fetch', () => Promise.reject(new Error('aborted')));

        const read = await attachmentExchange.read(request, { as: 'picture' }, abandoning.signal);

        expect(read).toEqual({ outcome: 'refused', refusal: 'abandoned' });
    });
});
