// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { portForPermission, portOf, resolveConnection } from './connection';

describe('resolveConnection', () => {
    it('reads a name with no scheme as the encrypted address it resolves to', () => {
        expect(resolveConnection('mail.example.test', false)).toEqual({
            secure: true,
            authority: 'mail.example.test',
            port: null,
        });
    });

    it('keeps the port somebody wrote, so a deployment on a port of its own reads as the one it is on', () => {
        expect(resolveConnection('mail.example.test:8443', false)).toEqual({
            secure: true,
            authority: 'mail.example.test:8443',
            port: '8443',
        });
    });

    // The disclosure exists to tell somebody whether a password is about to travel in the clear, so this is the
    // assertion the whole module is for: it says `secure: false` only where the entry actually resolved to `http`.
    it('says a permitted clear-text address is not secure', () => {
        expect(resolveConnection('http://mail.example.test', true)).toEqual({
            secure: false,
            authority: 'mail.example.test',
            port: null,
        });
    });

    it('resolves nothing at all for an address the wire refuses, rather than describing one it would not reach', () => {
        expect(resolveConnection('http://mail.example.test', false)).toBeNull();
        expect(resolveConnection('   ', false)).toBeNull();
        expect(resolveConnection('not a host', false)).toBeNull();
    });

    // Splitting on the first colon would read `[2001:db8::1]` as a host of `[2001` on port nothing, and splitting on
    // any colon would read `db8::1]` as a port. Only a colon after the closing bracket can start one.
    it('reads a bracketed IPv6 authority as a host rather than as a host and a port', () => {
        expect(resolveConnection('[2001:db8::1]', false)).toEqual({
            secure: true,
            authority: '[2001:db8::1]',
            port: null,
        });
    });

    it('reads the port after a bracketed IPv6 authority as the port', () => {
        expect(resolveConnection('[2001:db8::1]:8443', false)).toEqual({
            secure: true,
            authority: '[2001:db8::1]:8443',
            port: '8443',
        });
    });
});

describe('portOf', () => {
    it('answers the port the address named', () => {
        expect(portOf({ secure: true, authority: 'mail.example.test:8443', port: '8443' })).toBe('8443');
    });

    it('answers the scheme own port where the address named none, so the hint says what will be reached', () => {
        expect(portOf({ secure: true, authority: 'mail.example.test', port: null })).toBe('443');
        expect(portOf({ secure: false, authority: 'mail.example.test', port: null })).toBe('80');
    });
});

describe('portForPermission', () => {
    // The hint is drawn before anything is typed, so there is no resolved connection to read a port off and the
    // permission is the whole of what decides which scheme the client would reach under.
    it('answers the secured port while nothing has permitted clear text', () => {
        expect(portForPermission(false)).toBe('443');
    });

    it('answers the unsecured port once clear text is what is permitted', () => {
        expect(portForPermission(true)).toBe('80');
    });
});
