// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { systemNotifierForThisApplication } from './systemNotifier';

// The second branch on a head in this tree, so the same three things are worth proving about it as about the first:
// which operation the composition root resolves, that a head offering no shell says so rather than failing, and that
// the operating system is asked once however many notifications arrive.
//
// The shell is stood up the way it actually reaches the bundle. The plugin's own binding does not invoke a command
// directly — the shell installs a replacement for `window.Notification` before the bundle runs and the binding goes
// through that — so a test that faked the command bridge alone would prove nothing about what this module calls. What
// stands in below is that replacement, with the same three parts the shell's own script installs.

const tauriBridge = '__TAURI_INTERNALS__';

interface Shell {
    /** Every notification the shell was handed, as the operating system would have received it. */
    readonly raised: { title: string | undefined; body: string | undefined }[];

    /** How many times permission was asked for, which is what "asked once" is measured as. */
    asked: number;
}

/**
 * A shell announcing itself and answering the permission question the way the operating system would.
 *
 * @param answer What the operating system says when it is asked, `'default'` standing for a question not yet put.
 */
function shellAnswering(answer: 'granted' | 'denied'): Shell {
    const shell: Shell = { raised: [], asked: 0 };

    Reflect.set(globalThis, tauriBridge, { invoke: () => Promise.resolve(null) });

    const notification = function (title: string, options?: { body?: string }) {
        shell.raised.push({ title, body: options?.body });
    } as unknown as typeof window.Notification;

    Reflect.set(notification, 'permission', 'default');
    Reflect.set(notification, 'requestPermission', () => {
        shell.asked += 1;
        Reflect.set(notification, 'permission', answer);

        return Promise.resolve(answer);
    });

    Reflect.set(window, 'Notification', notification);

    return shell;
}

afterEach(() => {
    Reflect.deleteProperty(globalThis, tauriBridge);
    Reflect.deleteProperty(window, 'Notification');
});

describe('systemNotifierForThisApplication', () => {
    it('offers nothing where a shell announced itself but linked no notification plugin, as the Android head does', async () => {
        Reflect.set(globalThis, tauriBridge, { invoke: () => Promise.resolve(null) });

        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(false);
        await expect(notifier.raise('2 new messages')).resolves.toBe('unavailable');
    });

    it('offers nothing where no shell announced itself, so the web head raises nothing and asks nothing', async () => {
        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(false);
        await expect(notifier.raise('2 new messages')).resolves.toBe('unavailable');
    });

    it('hands the sentence to the shell where one offered the command', async () => {
        const shell = shellAnswering('granted');

        const raised = await systemNotifierForThisApplication().raise('2 new messages');

        expect(raised).toBe('raised');
        expect(shell.raised).toEqual([{ title: '2 new messages', body: undefined }]);
    });

    it('asks the operating system once however many notifications are raised', async () => {
        const shell = shellAnswering('granted');
        const notifier = systemNotifierForThisApplication();

        await Promise.all([notifier.raise('2 new messages'), notifier.raise('1 task reminder')]);
        await notifier.raise('1 calendar reminder');

        expect(shell.asked).toBe(1);
        expect(shell.raised).toHaveLength(3);
    });

    it('raises nothing for the rest of the run once the operating system has refused, and asks no second time', async () => {
        const shell = shellAnswering('denied');
        const notifier = systemNotifierForThisApplication();

        const first = await notifier.raise('2 new messages');
        const second = await notifier.raise('1 task reminder');

        expect([first, second]).toEqual(['refused', 'refused']);
        expect(shell.asked).toBe(1);
        expect(shell.raised).toEqual([]);
    });
});
