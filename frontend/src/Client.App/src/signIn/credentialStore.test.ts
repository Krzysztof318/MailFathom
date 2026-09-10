// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { credentialStore } from './credentialStore';

const deployment = { baseAddress: 'https://mail.example.invalid' };
const elsewhere = { baseAddress: 'https://elsewhere.example.invalid' };
// One kept session as the store sees it: an opaque string it writes and reads back without looking inside.
const credential = JSON.stringify({
    authorization: 'Bearer mfs_abcdef.ghijkl',
    expiresAt: '2026-09-09T09:00:00+00:00',
    person: 'karolina',
});

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
    window.localStorage.clear();
    vi.restoreAllMocks();
});

describe('credentialStore', () => {
    it('offers this browser as the place beyond the tab where no shell is hosting the client', async () => {
        const store = await credentialStore();

        expect(store.beyondTheTab).toBe('inThisBrowser');
    });

    it('offers the device’s own store where the shell has a protected one', async () => {
        shellAnswering({ credential_arrangement: 'keptInTheStore' });

        const store = await credentialStore();

        expect(store.beyondTheTab).toBe('inTheDeviceStore');
    });

    // The shell asked for the run and not beyond it, which is ADR 0027's answer for a device that kills the client all
    // day. `localStorage` is exactly what that refuses, so the checkbox that would reach it is not offered here.
    it('offers nothing beyond the tab where the shell keeps only the run', async () => {
        shellAnswering({ credential_arrangement: 'keptForTheRun' });

        const store = await credentialStore();

        expect(store.beyondTheTab).toBe('nowhereTheShellKeepsTheRun');
    });

    // An answer this client cannot read says nothing about which head it is on, and the page is the wrong guess on one
    // of the two: keeping nothing is the only resolution that is safe wherever the shell turns out to be running.
    it.each([
        ['will not answer at all', new Error('no store here')],
        ['answers with an arrangement this client does not know', 'keptSomewhereNewer'],
    ])('keeps a credential nowhere where the shell %s', async (_, answer) => {
        shellAnswering({ credential_arrangement: answer });

        const store = await credentialStore();

        expect(store.beyondTheTab).toBe('nowhereStorageUnreachable');
    });

    it('writes nothing the page can see where the shell answers with an arrangement this client does not know', async () => {
        shellAnswering({ credential_arrangement: 'keptSomewhereNewer' });
        const store = await credentialStore();

        expect(await store.keep(deployment, credential, true)).toBe(false);
        expect(window.sessionStorage.length).toBe(0);
        expect(window.localStorage.length).toBe(0);
    });

    it.each([
        ['protected storage it could not reach', 'notKeptStorageUnreachable', 'nowhereStorageUnreachable'],
        ['a key the device discarded', 'notKeptKeyInvalidated', 'nowhereKeyInvalidated'],
    ])('keeps a credential nowhere where the shell reports %s', async (_, arrangement, beyondTheTab) => {
        shellAnswering({ credential_arrangement: arrangement });

        const store = await credentialStore();

        expect(store.beyondTheTab).toBe(beyondTheTab);
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

            expect(await store.keep(deployment, credential, true)).toBe(false);
            expect(await store.keep(deployment, credential, false)).toBe(false);
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

        await store.keep(deployment, credential, false);
        await store.read(deployment);

        expect(asked).toEqual([{ command: 'credential_arrangement', argument: undefined }]);
    });
});

describe('a credential kept beyond the tab in this browser', () => {
    it('writes the session where the browser keeps it beyond the tab, and nowhere the tab alone would read it', async () => {
        const store = await credentialStore();

        expect(await store.keep(deployment, credential, true)).toBe(true);
        expect(window.localStorage.length).toBe(1);
        expect(window.sessionStorage.length).toBe(0);
        expect(await store.read(deployment)).toBe(credential);
    });

    it('removes it on signing out, which is the promise the screen made about the tick', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential, true);

        expect(await store.forget(deployment)).toBe(true);
        expect(window.localStorage.length).toBe(0);
        expect(await store.read(deployment)).toBeNull();
    });

    it('renews into the place the session is already kept, a renewal being told nothing about the choice', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential, true);
        await store.keep(deployment, 'renewed');

        expect(window.localStorage.length).toBe(1);
        expect(window.sessionStorage.length).toBe(0);
        expect(await store.read(deployment)).toBe('renewed');
    });

    it('takes the durable copy away where somebody signs in again without asking to be kept', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential, true);
        await store.keep(deployment, 'a second sign-in', false);

        expect(window.localStorage.length).toBe(0);
        expect(await store.read(deployment)).toBe('a second sign-in');
    });
});

describe('a credential kept for the run', () => {
    it('reads back what was kept for the deployment it was given for', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential);

        expect(await store.read(deployment)).toBe(credential);
    });

    it('reads back nothing for a deployment the credential was not given for', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential);

        expect(await store.read(elsewhere)).toBeNull();
    });

    it('reads back nothing once the credential has been forgotten', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential);

        expect(await store.forget(deployment)).toBe(true);
        expect(await store.read(deployment)).toBeNull();
    });

    it('leaves nothing behind that outlives the tab', async () => {
        const store = await credentialStore();

        await store.keep(deployment, credential);

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
        await expect(store.keep(deployment, credential)).resolves.toBe(false);
    });
});

describe('a credential kept in the shell’s protected store', () => {
    it('asks the shell for what it kept, naming the deployment the credential was given for', async () => {
        const asked = shellAnswering({ credential_arrangement: 'keptInTheStore', read_credential: credential });
        const store = await credentialStore();

        expect(await store.read(deployment)).toBe(credential);
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

        await store.keep(deployment, credential, true);

        expect(asked.at(-1)).toEqual({
            command: 'keep_credential',
            argument: { deployment: deployment.baseAddress, credential },
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
        expect(await store.keep(deployment, credential, true)).toBe(false);
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

        await store.keep(deployment, credential, true);
        await store.read(deployment);

        expect(written).toEqual([]);
    });
});
