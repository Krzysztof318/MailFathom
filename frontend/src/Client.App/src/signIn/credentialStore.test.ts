// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    it('keeps a credential until sign-out where the shell offers a protected store', async () => {
        shellAnswering({ credential_arrangement: 'keptInTheStore' });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilSignedOut');
    });

    it('keeps a credential for the run where the shell offers no store, rather than writing it to a file', async () => {
        shellAnswering({ credential_arrangement: 'keptForTheRun' });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheClientCloses');
    });

    it('keeps a credential for the run where the shell will not answer at all', async () => {
        shellAnswering({ credential_arrangement: new Error('no store here') });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheClientCloses');
    });

    it('keeps a credential for the run where the shell answers with an arrangement this client does not know', async () => {
        shellAnswering({ credential_arrangement: 'keptSomewhereNewer' });

        const store = await credentialStore();

        expect(store.lifetime).toBe('untilTheClientCloses');
    });

    it.each([
        ['protected storage it could not reach', 'notKeptStorageUnreachable'],
        ['a key the device discarded', 'notKeptKeyInvalidated'],
    ])('keeps a credential nowhere where the shell reports %s', async (_, arrangement) => {
        shellAnswering({ credential_arrangement: arrangement });

        const store = await credentialStore();

        expect(store.lifetime).toBe(arrangement);
    });
});

describe('a credential kept nowhere', () => {
    // ADR 0027's amendment, and the one place a store deliberately keeps nothing: a head whose protected storage was
    // there and could not be reached never falls back to the page, because a device that kills the client all day
    // would leave that password readable by anything reaching the origin for far longer than a tab ever does.
    it.each(['notKeptStorageUnreachable', 'notKeptKeyInvalidated'])(
        'reads back nothing and writes nothing anywhere the page can see, reporting %s',
        async (arrangement) => {
            shellAnswering({ credential_arrangement: arrangement });
            const store = await credentialStore();

            expect(await store.keep(deployment, authorization)).toBe(false);
            expect(await store.read(deployment)).toBeNull();
            expect(window.sessionStorage.length).toBe(0);
            expect(window.localStorage.length).toBe(0);
        },
    );

    // A store that could not be reached this run is not a store that holds nothing: an earlier run whose store opened
    // normally may have written a credential that is still on the device, and removing it needs no key.
    it('asks the shell to remove what an earlier run may have kept, rather than assuming there is nothing there', async () => {
        const asked = shellAnswering({
            credential_arrangement: 'notKeptStorageUnreachable',
            forget_credential: true,
        });
        const store = await credentialStore();

        expect(await store.forget(deployment)).toBe(true);
        expect(asked).toContainEqual({
            command: 'forget_credential',
            argument: { deployment: deployment.baseAddress },
        });
    });

    it('reports the credential as still there where the shell would not remove it', async () => {
        shellAnswering({ credential_arrangement: 'notKeptKeyInvalidated', forget_credential: false });
        const store = await credentialStore();

        expect(await store.forget(deployment)).toBe(false);
    });

    it('asks the shell for nothing beyond the arrangement when it keeps or reads', async () => {
        const asked = shellAnswering({ credential_arrangement: 'notKeptKeyInvalidated' });
        const store = await credentialStore();

        await store.keep(deployment, authorization);
        await store.read(deployment);

        expect(asked).toEqual([{ command: 'credential_arrangement', argument: undefined }]);
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

        expect(await store.forget(deployment)).toBe(true);
        expect(await store.read(deployment)).toBeNull();
    });

    it('leaves nothing behind that outlives the tab', async () => {
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(window.localStorage.length).toBe(0);
    });

    it('signs somebody in anyway where the browser refuses storage, and says nothing was kept', async () => {
        // On the prototype rather than on the store itself: jsdom hands out `sessionStorage` behind a proxy, so an own
        // property defined on the instance is not what a call goes through.
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new DOMException('The storage is full.', 'QuotaExceededError');
        });

        const store = await credentialStore();

        // Signing in worked and only the keeping failed, so this answers rather than throws — and it answers `false`,
        // because the screen has already said how long the password would last.
        await expect(store.keep(deployment, authorization)).resolves.toBe(false);
    });
});

describe('a credential kept in the shell’s protected store', () => {
    it('asks the shell for what it kept, naming the deployment the credential was given for', async () => {
        const asked = shellAnswering({ credential_arrangement: 'keptInTheStore', read_credential: authorization });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBe(authorization);
        expect(asked.at(-1)).toEqual({
            command: 'read_credential',
            argument: { deployment: deployment.baseAddress },
        });
    });

    it('reads back nothing where the shell answered with something that is not a credential', async () => {
        shellAnswering({ credential_arrangement: 'keptInTheStore', read_credential: null });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBeNull();
    });

    it('hands the shell the finished header value to keep, and nothing else about it', async () => {
        const asked = shellAnswering({ credential_arrangement: 'keptInTheStore', keep_credential: true });
        const store = await credentialStore();

        await store.keep(deployment, authorization);

        expect(asked.at(-1)).toEqual({
            command: 'keep_credential',
            argument: { deployment: deployment.baseAddress, authorization },
        });
    });

    it.each([
        ['a keychain that would not write the entry', false],
        ['a keychain that could not be reached at all', new Error('the keychain is locked')],
    ])('reports the credential as not kept where the shell answered with %s', async (_, answer) => {
        shellAnswering({ credential_arrangement: 'keptInTheStore', keep_credential: answer });
        const store = await credentialStore();

        // A keychain found at startup can be locked by the time it is written to, and the screen has already said the
        // password will last until sign-out — so a refused write is answered rather than left to be discovered at the
        // next start.
        expect(await store.keep(deployment, authorization)).toBe(false);
    });

    it('asks the shell to delete the entry when the credential is forgotten', async () => {
        const asked = shellAnswering({ credential_arrangement: 'keptInTheStore', forget_credential: true });
        const store = await credentialStore();

        expect(await store.forget(deployment)).toBe(true);
        expect(asked.at(-1)).toEqual({
            command: 'forget_credential',
            argument: { deployment: deployment.baseAddress },
        });
    });

    it.each([
        ['a keychain that would not delete the entry', false],
        ['a keychain that could not be reached at all', new Error('the keychain is locked')],
    ])('reports the credential as still kept where the shell answered with %s', async (_, answer) => {
        shellAnswering({ credential_arrangement: 'keptInTheStore', forget_credential: answer });
        const store = await credentialStore();

        // The screen has already said that signing out is what removes the password, so a deletion nobody performed
        // has to be reported: the entry outlives uninstalling the application, and the next start reads it back.
        expect(await store.forget(deployment)).toBe(false);
    });

    it('asks for the credential again where the keychain refuses, rather than failing the screen', async () => {
        shellAnswering({
            credential_arrangement: 'keptInTheStore',
            read_credential: new Error('the keychain is locked'),
        });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBeNull();
    });

    it('writes nothing anywhere a reader could find the credential when the keychain refuses', async () => {
        const written: unknown[] = [];
        for (const level of ['debug', 'error', 'info', 'log', 'warn'] as const) {
            vi.spyOn(console, level).mockImplementation((...reported: unknown[]) => written.push(...reported));
        }

        shellAnswering({
            credential_arrangement: 'keptInTheStore',
            keep_credential: new Error('the keychain is locked'),
            read_credential: new Error('the keychain is locked'),
        });
        const store = await credentialStore();

        await store.keep(deployment, authorization);
        await store.read(deployment);

        expect(written).toEqual([]);
    });
});
