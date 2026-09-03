// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { configuredConnection, configuredNothing } from './configuredConnection';

// The shell is the seam, so a test supplies one rather than replacing this module's own reading of it: what is being
// proven is which of the three sources wins and what is refused before either is read, and both are this module's.

type Stated = Readonly<Record<string, Readonly<Record<string, unknown>>>>;

function shellAnswering(answer: unknown): void {
    Object.defineProperty(window, '__TAURI__', {
        configurable: true,
        value: { core: { invoke: () => Promise.resolve(answer) } },
    });
}

function shellRefusing(): void {
    Object.defineProperty(window, '__TAURI__', {
        configurable: true,
        value: { core: { invoke: () => Promise.reject(new Error('no such command')) } },
    });
}

function stating(sources: Stated): void {
    shellAnswering({ commandLine: {}, environment: {}, configurationFile: {}, ...sources });
}

describe('configuredConnection', () => {
    afterEach(() => {
        Reflect.deleteProperty(window, '__TAURI__');
    });

    it('states nothing where no shell is hosting the page, which is every web head', async () => {
        await expect(configuredConnection()).resolves.toEqual(configuredNothing);
    });

    it('states nothing where the shell would not answer at all', async () => {
        shellRefusing();

        await expect(configuredConnection()).resolves.toEqual(configuredNothing);
    });

    it('reads the address a configuration file stated, where it is the only source that stated one', async () => {
        stating({ configurationFile: { serviceAddress: 'mail.example.invalid' } });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'mail.example.invalid',
            permitClearText: null,
        });
    });

    it('takes the environment over the configuration file', async () => {
        stating({
            environment: { serviceAddress: 'environment.example.invalid' },
            configurationFile: { serviceAddress: 'file.example.invalid' },
        });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'environment.example.invalid',
            permitClearText: null,
        });
    });

    it('takes the command line over both of the others', async () => {
        stating({
            commandLine: { serviceAddress: 'argument.example.invalid' },
            environment: { serviceAddress: 'environment.example.invalid' },
            configurationFile: { serviceAddress: 'file.example.invalid' },
        });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'argument.example.invalid',
            permitClearText: null,
        });
    });

    // An operator putting the address in an installer's arguments and the permission in a file is configuring one
    // deployment rather than making a mistake, so each setting is folded on its own.
    it('folds each setting on its own, so two sources may each answer one of them', async () => {
        stating({
            commandLine: { serviceAddress: 'argument.example.invalid' },
            configurationFile: { permitClearText: 'true' },
        });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'argument.example.invalid',
            permitClearText: 'true',
        });
    });

    // Templating an installer's arguments routinely emits an empty string for a setting nobody set, and a source that
    // said nothing has to fall through to the one beneath it rather than blanking it.
    it('reads a blank value as unset, and falls through to the source under it', async () => {
        stating({
            environment: { serviceAddress: '   ' },
            configurationFile: { serviceAddress: 'file.example.invalid' },
        });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'file.example.invalid',
            permitClearText: null,
        });
    });

    it('trims what surrounds a value, a trailing space being invisible in the file it was written in', async () => {
        stating({ environment: { serviceAddress: ' mail.example.invalid ' } });

        await expect(configuredConnection()).resolves.toEqual({
            serviceAddress: 'mail.example.invalid',
            permitClearText: null,
        });
    });

    it.each([null, 'a string', 42, { commandLine: 'not a source' }, { environment: { serviceAddress: 7 } }])(
        'states nothing rather than reading %s, which is not the shape a shell answers with',
        async (answer) => {
            shellAnswering(answer);

            await expect(configuredConnection()).resolves.toEqual(configuredNothing);
        },
    );
});
