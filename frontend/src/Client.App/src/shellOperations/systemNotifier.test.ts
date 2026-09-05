// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { systemNotifierForThisApplication } from './systemNotifier';

// The second branch on a head in this tree, so the same three things are worth proving about it as about the first:
// which operation the composition root resolves, that a head offering no shell says so rather than failing, and that
// the operating system is asked once however many notifications arrive.
//
// The shell is stood up the way it actually reaches the bundle, which is now two bridges rather than one. Permission
// is still the plugin's, and its binding does not invoke a command directly — the shell installs a replacement for
// `window.Notification` before the bundle runs and the binding goes through that. Raising is this shell's own command
// and the click it reports is this shell's own event, so both go through the global Tauri API instead. A fake of
// either one alone would prove nothing about what this module calls, so what stands in below is both.

const tauriBridge = '__TAURI_INTERNALS__';

interface Shell {
    /** Every sentence the shell was asked to raise, as the command would have received it. */
    readonly raised: string[];

    /** How many times permission was asked for, which is what "asked once" is measured as. */
    asked: number;

    /** What the shell was subscribed to, so the one name this module and the shell share is asserted rather than assumed. */
    subscribedTo: string | null;

    /** Somebody acting on the notification the operating system showed, or `null` where nothing is subscribed. */
    act: (() => void) | null;

    /** How many subscriptions stand, which is what proves that stopping one stops it. */
    listening: number;
}

/**
 * A shell announcing itself and answering the permission question the way the operating system would.
 *
 * @param answer What the operating system says when it is asked, `'default'` standing for a question not yet put.
 */
function shellAnswering(answer: 'granted' | 'denied'): Shell {
    const shell: Shell = { raised: [], asked: 0, subscribedTo: null, act: null, listening: 0 };

    Reflect.set(globalThis, tauriBridge, { invoke: () => Promise.resolve(null) });

    Reflect.set(window, '__TAURI__', {
        core: {
            invoke: (command: string, argument?: Readonly<Record<string, unknown>>) => {
                if (command === 'raise_notification') {
                    shell.raised.push(String(argument?.['said']));
                }

                return Promise.resolve(null);
            },
        },
        event: {
            listen: (event: string, handler: () => void) => {
                shell.subscribedTo = event;
                shell.act = handler;
                shell.listening += 1;

                return Promise.resolve(() => {
                    shell.act = null;
                    shell.listening -= 1;
                });
            },
        },
    });

    const notification = function () {
        throw new Error(
            "The plugin's own notification binding was called, which this client no longer raises through.",
        );
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
    Reflect.deleteProperty(window, '__TAURI__');
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

    it('subscribes to nothing where no shell offered the operation, and stopping that subscription is safe', () => {
        const notifier = systemNotifierForThisApplication();
        const acted = vi.fn();

        const stop = notifier.whenActedOn(acted);
        stop();

        expect(acted).not.toHaveBeenCalled();
    });

    it('hands the sentence to the shell where one offered the command', async () => {
        const shell = shellAnswering('granted');

        const raised = await systemNotifierForThisApplication().raise('2 new messages');

        expect(raised).toBe('raised');
        expect(shell.raised).toEqual(['2 new messages']);
    });

    it('reports somebody acting on one, and stops reporting when the subscription is stopped', async () => {
        const shell = shellAnswering('granted');
        const acted = vi.fn();

        const stop = systemNotifierForThisApplication().whenActedOn(acted);

        await Promise.resolve();

        expect(shell.subscribedTo).toBe('system-notification-acted-on');
        expect(shell.listening).toBe(1);

        shell.act?.();

        expect(acted).toHaveBeenCalledTimes(1);

        stop();
        await Promise.resolve();

        expect(shell.listening).toBe(0);
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
