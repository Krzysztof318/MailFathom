// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { deviceKeys, deviceStore, listWidthKey } from './deviceStore';

// What a system without origin storage looks like from inside the client: reaching the property throws, which is what
// a browser configured to refuse storage and a WebView started without it both do. Defined over the one the test
// environment installs, and taken off again afterwards, so the store the other cases read is the document's own.
function withoutStorage(): void {
    Object.defineProperty(window, 'localStorage', {
        configurable: true,
        get: () => {
            throw new Error('This browser is configured to refuse storage.');
        },
    });
}

const jsdomStorage = window.localStorage;

afterEach(() => {
    Object.defineProperty(window, 'localStorage', { configurable: true, value: jsdomStorage, writable: false });
    window.localStorage.clear();
});

describe('deviceStore', () => {
    it('reads back what was written, which is how the next start of either head opens on it', () => {
        deviceStore().write(deviceKeys.locale, 'pl');

        expect(deviceStore().read(deviceKeys.locale)).toBe('pl');
    });

    it('reads nothing under a key nothing was written to', () => {
        expect(deviceStore().read(deviceKeys.themeChoice)).toBeNull();
    });

    it('reads nothing back once the value is removed', () => {
        deviceStore().write(deviceKeys.themeChoice, 'dark');
        deviceStore().remove(deviceKeys.themeChoice);

        expect(deviceStore().read(deviceKeys.themeChoice)).toBeNull();
    });

    it('keeps a value for the run where the system refuses storage, rather than failing to answer at all', () => {
        withoutStorage();

        deviceStore().write(deviceKeys.themeChoice, 'light');

        expect(deviceStore().read(deviceKeys.themeChoice)).toBe('light');
    });

    it('stays on its feet when storage that answered a read then refuses a write, which a full quota is', () => {
        Object.defineProperty(window, 'localStorage', {
            configurable: true,
            value: {
                getItem: () => null,
                setItem: () => {
                    throw new Error('The quota for this origin is exhausted.');
                },
                removeItem: () => undefined,
            },
        });

        // The probe found storage, so this is the device store rather than the run's, and a write it refuses is a
        // preference lost rather than a screen that fails: what matters is that the caller is answered at all.
        expect(() => {
            deviceStore().write(deviceKeys.themeChoice, 'dark');
        }).not.toThrow();
        expect(deviceStore().read(deviceKeys.themeChoice)).toBeNull();
    });

    it('forgets a value kept for the run, so a removal means the same thing on either system', () => {
        withoutStorage();

        deviceStore().write(deviceKeys.locale, 'pl');
        deviceStore().remove(deviceKeys.locale);

        expect(deviceStore().read(deviceKeys.locale)).toBeNull();
    });

    it('leaves nothing behind on the device where the system refused to hold it', () => {
        withoutStorage();
        deviceStore().write(deviceKeys.themeChoice, 'dark');

        Object.defineProperty(window, 'localStorage', { configurable: true, value: jsdomStorage, writable: false });

        expect(deviceStore().read(deviceKeys.themeChoice)).toBeNull();
    });
});

describe('listWidthKey', () => {
    it('answers the same key for one person, which is what lets their width survive signing out', () => {
        expect(listWidthKey('karolina')).toBe(listWidthKey('karolina'));
    });

    it('answers a different key for somebody else, so two people sharing a machine keep their own split', () => {
        expect(listWidthKey('karolina')).not.toBe(listWidthKey('marta'));
    });

    it('names nobody in the key itself, so the store carries no list of who reads mail on this machine', () => {
        expect(listWidthKey('karolina')).not.toContain('karolina');
    });
});
