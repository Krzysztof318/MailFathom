// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { resolveDeploymentEntry, type DeploymentEntryRefusal } from './deployment';

function refusal(entry: string, clearTextPermitted = false): DeploymentEntryRefusal | 'resolved' {
    const result = resolveDeploymentEntry(entry, clearTextPermitted);

    return result.outcome === 'resolved' ? 'resolved' : result.refusal;
}

function resolved(entry: string, clearTextPermitted = false): string | null {
    const result = resolveDeploymentEntry(entry, clearTextPermitted);

    return result.outcome === 'resolved' ? result.deployment.baseAddress : null;
}

describe('resolveDeploymentEntry', () => {
    it('supplies the scheme a person did not write, because a password travels on every request', () => {
        expect(resolved('mail.example.test')).toBe('https://mail.example.test');
    });

    it('keeps the port a deployment was named with', () => {
        expect(resolved('mail.example.test:8443')).toBe('https://mail.example.test:8443');
    });

    it('drops the port a scheme already implies, so one deployment has one address', () => {
        expect(resolved('mail.example.test:443')).toBe('https://mail.example.test');
    });

    it('takes an address somebody pasted with the secure scheme already on it', () => {
        expect(resolved('https://mail.example.test:8443')).toBe('https://mail.example.test:8443');
    });

    it('ignores the whitespace around what was typed', () => {
        expect(resolved('  mail.example.test  ')).toBe('https://mail.example.test');
    });

    it.each(['', '   '])('refuses an entry naming nothing: %j', (entry) => {
        expect(refusal(entry)).toBe('blank');
    });

    it.each([
        ['a scheme this client does not speak', 'tauri://localhost'],
        ['a link to a screen rather than a deployment', 'mail.example.test/inbox'],
        ['a query somebody carried over from a browser', 'mail.example.test?owner=me'],
        ['a fragment', 'mail.example.test#inbox'],
        ['a password written into the address', 'https://owner:secret@mail.example.test'],
        ['a host that is not one', 'https://'],
        ['a sentence', 'my mail server'],
    ])('refuses %s: %j', (_, entry) => {
        expect(refusal(entry)).toBe('malformed');
    });

    it('refuses a clear-text address nobody declared, rather than sending a password over it', () => {
        expect(refusal('http://mail.example.test')).toBe('clearTextRefused');
    });

    it('takes a clear-text address once it has been declared', () => {
        expect(resolved('mail.example.test', true)).toBe('http://mail.example.test');
    });

    it('leaves the secure scheme alone where clear text was declared but not written', () => {
        expect(resolved('https://mail.example.test', true)).toBe('https://mail.example.test');
    });

    it.each(['http://localhost:8080', 'http://127.0.0.1:8080', 'http://[::1]:8080'])(
        'takes %j undeclared, because clear text to this machine crosses no network',
        (entry) => {
            expect(resolved(entry)).toBe(entry);
        },
    );

    it('refuses a host that merely ends in the loopback name, which is a name somebody chose', () => {
        expect(refusal('http://tauri.localhost')).toBe('clearTextRefused');
    });

    it('takes a host carrying separators inside its labels', () => {
        expect(resolved('mail-fathom.example-host.test')).toBe('https://mail-fathom.example-host.test');
    });

    it.each(['-mail.example.test', 'mail-.example.test', 'mail..example.test'])(
        'refuses %j, where a label does not begin and end in a character a resolver would accept',
        (entry) => {
            expect(refusal(entry)).toBe('malformed');
        },
    );

    // The refusal has to arrive rather than the work being proportional to what can be pasted. Written the obvious
    // way, the host pattern walks an astronomical number of attempts before refusing an entry of this shape, and this
    // test reports that as the runner's own timeout rather than as a wrong answer.
    it('refuses a long entry that nearly matches, without walking every way it could have matched', () => {
        expect(refusal(`${'mailfathom.'.repeat(24)}!`)).toBe('malformed');
    });

    it('refuses an entry longer than any address could be, rather than reading it', () => {
        expect(refusal(`${'a'.repeat(400)}.example.test`)).toBe('malformed');
    });
});
