// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { readKeptSession, writeKeptSession, type KeptSession } from './keptSession';

const session: KeptSession = {
    authorization: 'Bearer mfs_abcdefghijklmnop.cXVpY2stYnJvd24tZm94',
    expiresAt: '2026-09-09T09:00:00+00:00',
    person: 'karolina',
};

function stored(fields: Record<string, unknown>): string {
    return JSON.stringify({ ...session, ...fields });
}

describe('keptSession', () => {
    // The pair is one property rather than two: what the store holds is only ever what this wrote, so a shape either
    // of them changed alone would leave a client reading nothing back and asking for a password at every start.
    it('reads back exactly what it wrote, which is what makes a later start open already signed in', () => {
        expect(readKeptSession(writeKeptSession(session))).toEqual(session);
    });

    it('reads a name back through UTF-8, so somebody whose name is not US-ASCII is still themselves', () => {
        const zaza = { ...session, person: 'zażółć' };

        expect(readKeptSession(writeKeptSession(zaza))).toEqual(zaza);
    });

    it.each([
        ['a store holding nothing', null],
        ['an entry somebody emptied', ''],
        ['a value that is not JSON at all', 'not json'],
        ['a value that is not an object', '"Basic abcdef"'],
        ['an array, which JSON calls an object and this does not', '[]'],
        ['a credential this client never wrote', JSON.stringify({ authorization: 'Basic abcdef' })],
        ['an empty credential, which is a header naming nothing', stored({ authorization: '' })],
        ['a credential that is not a string', stored({ authorization: 7 })],
        [
            'no instant, which is a session nothing could renew',
            JSON.stringify({ authorization: session.authorization }),
        ],
        ['an instant no clock reads', stored({ expiresAt: 'whenever' })],
        ['nobody named', stored({ person: '' })],
        ['a name that is not a string', stored({ person: 7 })],
        ['a name past the length the sign-in screen accepts', stored({ person: 'a'.repeat(2000) })],
    ])('answers nothing kept for %s', (_, held) => {
        expect(readKeptSession(held)).toBeNull();
    });

    // A store answering with something this size is a store that has been tampered with or has failed, and either way
    // it is refused before it is expanded rather than after.
    it('refuses a stored value too long to be one this client wrote', () => {
        expect(readKeptSession(`"${'a'.repeat(3000)}"`)).toBeNull();
    });
});
