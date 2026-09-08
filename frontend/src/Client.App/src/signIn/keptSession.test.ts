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

// The instant every read below is judged against, stated rather than taken from the machine: what this module decides
// is whether a session has ended, so a suite reading the system clock would start answering differently on the day the
// fixture's own instant passed.
const readAt = Date.parse('2026-09-08T09:00:00+00:00');

describe('keptSession', () => {
    // The pair is one property rather than two: what the store holds is only ever what this wrote, so a shape either
    // of them changed alone would leave a client reading nothing back and asking for a password at every start.
    it('reads back exactly what it wrote, which is what makes a later start open already signed in', () => {
        expect(readKeptSession(writeKeptSession(session), readAt)).toEqual(session);
    });

    it('reads a name back through UTF-8, so somebody whose name is not US-ASCII is still themselves', () => {
        const zaza = { ...session, person: 'zażółć' };

        expect(readKeptSession(writeKeptSession(zaza), readAt)).toEqual(zaza);
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

        // The ordinary morning after: a twelve-hour session opened the next day is over, and answering it as something
        // kept would mount the whole frame, meet a refusal, and drop somebody onto the sign-in screen under a notice
        // about a deployment that changed nothing.
        ['a session that has already ended', stored({ expiresAt: '2026-09-08T08:59:59+00:00' })],
    ])('answers nothing kept for %s', (_, held) => {
        expect(readKeptSession(held, readAt)).toBeNull();
    });

    // The second entry point into the credential, and the one nothing composed: the store is a place any script on the
    // origin can write to, and what is read back becomes an `Authorization` header verbatim. A value the `Headers`
    // constructor refuses would turn every later read into a deployment that cannot be reached, with the unusable
    // session persisted across reloads and no way out but signing out again.
    it.each([
        ['a header break', 'Bearer mfs_abc.def\r\nX-Injected: yes'],
        ['a second space', 'Bearer mfs_abc def'],
        ['a character outside the bearer alphabet', 'Bearer mfs_abc.déf'],
        ['a scheme this client never composes', 'Basic YWJjOmRlZg=='],
        ['no scheme at all', 'mfs_abcdefghijklmnop.cXVpY2stYnJvd24tZm94'],
    ])('answers nothing kept for a stored credential carrying %s', (_, authorization) => {
        expect(readKeptSession(stored({ authorization }), readAt)).toBeNull();
    });

    // A store answering with something this size is a store that has been tampered with or has failed, and either way
    // it is refused before it is expanded rather than after.
    it('refuses a stored value too long to be one this client wrote', () => {
        expect(readKeptSession(`"${'a'.repeat(3000)}"`, readAt)).toBeNull();
    });
});
