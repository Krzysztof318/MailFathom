// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { credentialStore } from './credentialStore';

const deployment = { baseAddress: 'https://mail.example.invalid' };
const elsewhere = { baseAddress: 'https://elsewhere.example.invalid' };
const authorization = 'Basic b3duZXI6b3BlbiBzZXNhbWU=';

/** One command the shell was asked, so a test reads what crossed into it rather than what a store meant to send. */
interface Asked {
    readonly command: string;
    readonly argument: Readonly<Record<string, unknown>> | undefined;
}

const global = window as unknown as Record<string, unknown>;

/** Puts a shell in front of the application, answering each command with what a test named. */
function shellAnswering(answers: Readonly<Record<string, unknown>>): Asked[] {
    const asked: Asked[] = [];

    global['__TAURI__'] = {
        core: {
            invoke: (command: string, argument?: Readonly<Record<string, unknown>>) => {
                asked.push({ command, argument });
                const answer = answers[command];

                return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer);
            },
        },
    };

    return asked;
}

afterEach(() => {
    delete global['__TAURI__'];
    window.sessionStorage.clear();
    vi.restoreAllMocks();
});

describe('credentialStore', () => {
    it('keeps a credential for the tab where no shell is hosting the client', async () => {
        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheTabCloses');
    });

    it('keeps a credential until sign-out where the shell reaches a keychain', async () => {
        shellAnswering({ keychain_reachable: true });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilSignedOut');
    });

    it('keeps a credential for the run where the shell reaches no keychain, rather than writing it to a file', async () => {
        shellAnswering({ keychain_reachable: false });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheClientCloses');
    });

    it('keeps a credential for the run where the shell will not answer at all', async () => {
        shellAnswering({ keychain_reachable: new Error('no keychain here') });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheClientCloses');
    });
});

describe('a credential kept for the run', () => {
    it('reads back what was kept for the deployment it was given for', async () => {
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(await store.read(deployment)).toBe(authorization);
    });

    it('reads back nothing for a deployment the credential was not given for', async () => {
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(await store.read(elsewhere)).toBeNull();
    });

    it('reads back nothing once the credential has been forgotten', async () => {
        const store = await credentialStore();

        await store.keep(deployment, authorization);
        await store.forget(deployment);

        expect(await store.read(deployment)).toBeNull();
    });

    it('leaves nothing behind that outlives the tab', async () => {
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(window.localStorage.length).toBe(0);
    });

    it('signs somebody in anyway where the browser refuses storage', async () => {
        vi.spyOn(window.sessionStorage, 'setItem').mockImplementation(() => {
            throw new DOMException('The storage is full.', 'QuotaExceededError');
        });

        const store = await credentialStore();

        await expect(store.keep(deployment, authorization)).resolves.toBeUndefined();
    });
});

describe('a credential kept in the keychain', () => {
    it('asks the shell for what it kept, naming the deployment the credential was given for', async () => {
        const asked = shellAnswering({ keychain_reachable: true, read_credential: authorization });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBe(authorization);
        expect(asked.at(-1)).toEqual({
            command: 'read_credential',
            argument: { deployment: deployment.baseAddress },
        });
    });

    it('reads back nothing where the shell answered with something that is not a credential', async () => {
        shellAnswering({ keychain_reachable: true, read_credential: null });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBeNull();
    });

    it('hands the shell the finished header value to keep, and nothing else about it', async () => {
        const asked = shellAnswering({ keychain_reachable: true, keep_credential: true });
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(asked.at(-1)).toEqual({
            command: 'keep_credential',
            argument: { deployment: deployment.baseAddress, authorization },
        });
    });

    it('asks the shell to delete the entry when the credential is forgotten', async () => {
        const asked = shellAnswering({ keychain_reachable: true, forget_credential: true });
        const store = await credentialStore();

        await store.forget(deployment);

        expect(asked.at(-1)).toEqual({
            command: 'forget_credential',
            argument: { deployment: deployment.baseAddress },
        });
    });

    it('asks for the credential again where the keychain refuses, rather than failing the screen', async () => {
        shellAnswering({ keychain_reachable: true, read_credential: new Error('the keychain is locked') });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBeNull();
    });

    it('writes nothing anywhere a reader could find the credential when the keychain refuses', async () => {
        const written: unknown[] = [];
        for (const level of ['debug', 'error', 'info', 'log', 'warn'] as const) {
            vi.spyOn(console, level).mockImplementation((...reported: unknown[]) => written.push(...reported));
        }

        shellAnswering({
            keychain_reachable: true,
            keep_credential: new Error('the keychain is locked'),
            read_credential: new Error('the keychain is locked'),
        });
        const store = await credentialStore();

        await store.keep(deployment, authorization);
        await store.read(deployment);

        expect(written).toEqual([]);
    });
});
