// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { reachDeployment, resolveDeploymentEntry, type DeploymentEntryRefusal } from './deployment';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const deployment = { baseAddress: 'https://mail.example.invalid' };

function answering(response: Partial<ClientResponse>): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: '', headers: {}, ...response });
}

function sessionBody(service: unknown, version: unknown): string {
    return JSON.stringify({ service, version, permissions: [] });
}

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

describe('reachDeployment', () => {
    it('asks the session route, which is the one a client holding no credential may reach', async () => {
        const asked: ClientRequest[] = [];

        await reachDeployment(deployment, (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body: sessionBody('MailFathom', '0.8.0'), headers: {} });
        });

        expect(asked).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/session',
                headers: { Accept: 'application/json' },
            },
        ]);
    });

    it('reports the release a deployment answering in full names', async () => {
        const result = await reachDeployment(deployment, answering({ body: sessionBody('MailFathom', '0.8.0') }));

        expect(result).toEqual({ outcome: 'read', value: { version: '0.8.0' } });
    });

    it('takes a refusal challenging under MailFathom as the deployment being there and wanting a credential', async () => {
        const result = await reachDeployment(
            deployment,
            answering({ status: 401, headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } }),
        );

        expect(result).toEqual({ outcome: 'read', value: { version: null } });
    });

    it('refuses a refusal challenging under somebody else, which is another product answering the port', async () => {
        const result = await reachDeployment(
            deployment,
            answering({ status: 401, headers: { 'www-authenticate': 'Basic realm="Router"' } }),
        );

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 401 } });
    });

    it('refuses a refusal carrying no challenge at all', async () => {
        const result = await reachDeployment(deployment, answering({ status: 401 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 401 } });
    });

    it.each([
        ['another product answering in JSON', sessionBody('Something else', '0.8.0')],
        ['a session body naming no release', sessionBody('MailFathom', null)],
        ['a release longer than a release is', sessionBody('MailFathom', 'v'.repeat(65))],
        ['a page rather than an answer', '<!doctype html><title>Sign in</title>'],
        ['an array', '[]'],
        ['nothing', ''],
    ])('refuses %s as a deployment', async (_, body) => {
        const result = await reachDeployment(deployment, answering({ body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a route that is not served as something other than MailFathom answering', async () => {
        const result = await reachDeployment(deployment, answering({ status: 404 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 404 } });
    });

    it('reads a deployment that is failing as one to try again rather than as the wrong address', async () => {
        const result = await reachDeployment(deployment, answering({ status: 503 }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await reachDeployment(deployment, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('makes one attempt and never a second one without the transport security the first asked for', async () => {
        const asked: string[] = [];

        await reachDeployment({ baseAddress: 'https://mail.example.invalid' }, (request) => {
            asked.push(request.path);

            return Promise.reject(new TypeError('Failed to fetch'));
        });

        expect(asked).toEqual(['https://mail.example.invalid/api/client/session']);
    });
});
