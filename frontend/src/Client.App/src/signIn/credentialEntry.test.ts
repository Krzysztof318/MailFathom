// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    longestCredentialPart,
    resolveCredentialEntry,
    userNameIn,
    type CredentialEntryRefusal,
} from './credentialEntry';

function composed(userName: string, password: string): string {
    const result = resolveCredentialEntry(userName, password);

    return result.outcome === 'resolved' ? result.authorization : '';
}

function refusal(userName: string, password: string): CredentialEntryRefusal | 'resolved' {
    const result = resolveCredentialEntry(userName, password);

    return result.outcome === 'resolved' ? 'resolved' : result.refusal;
}

function authorization(userName: string, password: string): string | null {
    const result = resolveCredentialEntry(userName, password);

    return result.outcome === 'resolved' ? result.authorization : null;
}

describe('resolveCredentialEntry', () => {
    // RFC 7617's own worked example, so what this asserts is the specification rather than this implementation's
    // agreement with itself.
    it('composes the header value the specification writes for a user name and a password', () => {
        expect(authorization('Aladdin', 'open sesame')).toBe('Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==');
    });

    it('encodes a password as UTF-8, which is the one encoding the challenge names', () => {
        expect(authorization('właściciel', 'hasło')).toBe('Basic d8WCYcWbY2ljaWVsOmhhc8WCbw==');
    });

    it('keeps a colon inside a password, which the scheme reads as part of it', () => {
        expect(authorization('owner', 'a:b')).toBe('Basic b3duZXI6YTpi');
    });

    it.each([
        ['neither half', '', ''],
        ['no user name', '', 'open sesame'],
        ['no password', 'Aladdin', ''],
    ])('refuses %s, rather than presenting half a credential', (_, userName, password) => {
        expect(refusal(userName, password)).toBe('incomplete');
    });

    it('refuses a colon in the user name, which the deployment would split at the wrong place', () => {
        expect(refusal('own:er', 'open sesame')).toBe('userNameHasColon');
    });

    it.each([
        ['a user name', 'u'.repeat(longestCredentialPart + 1), 'open sesame'],
        ['a password', 'owner', 'p'.repeat(longestCredentialPart + 1)],
    ])('refuses %s past the bound rather than presenting one nobody typed', (_, userName, password) => {
        // The bound is here rather than on the input alone, because `maxLength` truncates a paste in silence — and a
        // password one character shorter than the one somebody was given is refused by the deployment and read back
        // as a wrong password.
        expect(refusal(userName, password)).toBe('tooLong');
    });

    it.each([
        ['a user name', 'u'.repeat(longestCredentialPart), 'open sesame'],
        ['a password', 'owner', 'p'.repeat(longestCredentialPart)],
    ])(
        'composes %s exactly as long as the bound, which is what makes it a bound rather than a limit',
        (_, userName, password) => {
            expect(refusal(userName, password)).toBe('resolved');
        },
    );
});

describe('userNameIn', () => {
    it('reads back the name a credential was composed from, which is what tells one person on a machine from another', () => {
        expect(userNameIn(composed('karolina', 'open sesame'))).toBe('karolina');
    });

    it('reads a name back through UTF-8, so somebody whose name is not US-ASCII is still themselves', () => {
        expect(userNameIn(composed('zażółć', 'open sesame'))).toBe('zażółć');
    });

    it('reads back only the name, whatever the password beside it carries', () => {
        expect(userNameIn(composed('karolina', 'open:sesame'))).toBe('karolina');
    });

    it.each([
        ['nobody signed in', null],
        ['another scheme', 'Bearer abcdef'],
        ['nothing base64 decodes to', 'Basic not base64 at all'],
        ['a value with no separator in it', `Basic ${btoa('karolina')}`],
        ['a value with an empty name', `Basic ${btoa(':open sesame')}`],
    ])('answers nobody for %s', (_, authorization) => {
        expect(userNameIn(authorization)).toBeNull();
    });
});
