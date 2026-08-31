// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { send, type ClientRequest, type ClientResponse } from './transport';

const request: ClientRequest = {
    method: 'GET',
    path: 'https://mail.example.invalid/api/client/session',
    headers: { Accept: 'application/json' },
};

describe('send', () => {
    it('answers what came back, unread and undecided', async () => {
        const answer: ClientResponse = { status: 200, body: '{}', headers: { 'content-type': 'application/json' } };

        expect(await send(() => Promise.resolve(answer), request)).toEqual(answer);
    });

    it('hands the request to the transport as it was given', async () => {
        const asked: ClientRequest[] = [];

        await send((given) => {
            asked.push(given);

            return Promise.resolve({ status: 200, body: '', headers: {} });
        }, request);

        expect(asked).toEqual([request]);
    });

    // Every operation reports an expected failure as a value, so nothing above this may throw. A connection refused, a
    // name that does not resolve, and a certificate the client will not accept all arrive here as a rejection.
    it('answers nothing where the connection never produced an answer', async () => {
        expect(await send(() => Promise.reject(new TypeError('Failed to fetch')), request)).toBeNull();
    });

    it('answers nothing where the transport threw before it returned a promise at all', async () => {
        expect(
            await send(() => {
                throw new TypeError('Failed to fetch');
            }, request),
        ).toBeNull();
    });
});
