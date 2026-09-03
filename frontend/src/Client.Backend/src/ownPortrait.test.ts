// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    isPortraitImageType,
    largestPortraitOctets,
    readOwnPortraitRequest,
    removeOwnPortraitRequest,
    replaceOwnPortraitRequest,
} from './ownPortrait';
import type { ClientSession } from './session';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

describe('readOwnPortraitRequest', () => {
    it('asks for the portrait route on the client surface with the session it was given', () => {
        const request = readOwnPortraitRequest(session);

        expect(request.method).toBe('GET');
        expect(request.path).toBe('https://mail.example.invalid/api/client/portrait');
        expect(request.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('asks for the two kinds this surface serves rather than for a document', () => {
        expect(readOwnPortraitRequest(session).headers['Accept']).toBe('image/jpeg, image/png');
    });

    it('reads the answer under the bound a stored portrait cannot exceed', () => {
        expect(readOwnPortraitRequest(session).longestAnswer).toBe(largestPortraitOctets);
    });
});

describe('replaceOwnPortraitRequest', () => {
    it('states the kind the chosen file was found to be', () => {
        const request = replaceOwnPortraitRequest(session, 'image/png');

        expect(request.method).toBe('POST');
        expect(request.path).toBe('https://mail.example.invalid/api/client/portrait');
        expect(request.headers['Content-Type']).toBe('image/png');
    });

    it('carries no body, because the octets travel with the adapter that puts it on the wire', () => {
        expect(replaceOwnPortraitRequest(session, 'image/jpeg').body).toBeUndefined();
    });
});

describe('removeOwnPortraitRequest', () => {
    it('removes the portrait at the same route it is read at', () => {
        const request = removeOwnPortraitRequest(session);

        expect(request.method).toBe('DELETE');
        expect(request.path).toBe('https://mail.example.invalid/api/client/portrait');
        expect(request.headers['Authorization']).toBe('Basic dGVzdA==');
    });
});

describe('isPortraitImageType', () => {
    it.each(['image/jpeg', 'image/png'])('accepts %s as a kind this surface stores', (type) => {
        expect(isPortraitImageType(type)).toBe(true);
    });

    it.each(['image/gif', 'image/svg+xml', 'application/pdf', '', 'image/jpeg; charset=utf-8'])(
        'refuses %s as a kind this surface does not store',
        (type) => {
            expect(isPortraitImageType(type)).toBe(false);
        },
    );
});
