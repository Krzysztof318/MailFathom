// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    it('composes a route for a deployment nobody has signed in to, which is what an address is asked with', () => {
        expect(routeFor({ baseAddress: 'https://mail.example.invalid' }, '/session')).toBe(
            'https://mail.example.invalid/api/client/session',
        );
    });
});

describe('headersFor', () => {
    it('asks for JSON and carries the credential the session holds, and nothing else', () => {
        expect(headersFor(session)).toEqual({
            Accept: 'application/json',
            Authorization: 'Basic dGVzdA==',
        });
    });

    // The assertion above is the whole of it while no pipeline is registered, which is what a run that has not signed
    // in looks like: there is no span to name, so the propagator writes no trace context and the request begins a
    // trace at the deployment. `exporting.test.ts` in `Client.App` is where the composed client's own span is shown
    // reaching the header, because registering the propagator and the context manager is that package's act.
    it('names no trace this client did not open', () => {
        expect(headersFor(session)).not.toHaveProperty('traceparent');
    });
});
