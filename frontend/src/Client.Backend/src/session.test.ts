// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { headersFor, routeFor, type ClientSession } from './session';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

describe('routeFor', () => {
    it('puts the client prefix between the base address and the route', () => {
        expect(routeFor(session, '/accounts')).toBe('https://mail.example.invalid/api/client/accounts');
    });
});

describe('headersFor', () => {
    it('asks for JSON and carries the credential the session holds, and nothing else', () => {
        expect(headersFor(session)).toEqual({
            Accept: 'application/json',
            Authorization: 'Basic dGVzdA==',
        });
    });
});
