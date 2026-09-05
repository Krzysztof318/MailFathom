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
//
// The browser is stood up out of the platform's own three parts — the constructor, the record, and the request — since
// that is exactly what the plugin's replacement is a replacement *for*. What separates the two heads in every test
// below is therefore whether a shell announced itself, which is the question the module actually asks.

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

/** One notification as this module holds it, which is the two parts it sets on what the constructor returned. */
interface Raised {
    onclick?: () => void;
    close?: () => void;
}

/** A browser's own Notifications API, as a page reaches it where no shell replaced the binding. */
interface Browser {
    /** Every notification the browser was asked to construct, as it would have received it. */
    readonly raised: { title: string | undefined; body: string | undefined }[];

    /** How many times permission was asked for, which is what "asked from the gesture, once" is measured as. */
    asked: number;

    /** The last one constructed, so what a click on it does can be provoked. */
    last: Raised | null;
}

/**
 * Installs the browser's own `Notification`, answering the permission question the way one would.
 *
 * @param standing What the record reads before anything asks, which is the browser's own `permission`.
 * @param answer What is answered when somebody is asked, or `null` where the call itself fails.
 */
function browserAnswering(standing: NotificationPermission, answer: NotificationPermission | null): Browser {
    const browser: Browser = { raised: [], asked: 0, last: null };

    const binding = function (this: Raised, title: string, options?: { body?: string }) {
        browser.raised.push({ title, body: options?.body });
        this.close = () => undefined;
        browser.last = this;
    } as unknown as typeof window.Notification;

    Reflect.set(binding, 'permission', standing);
    Reflect.set(binding, 'requestPermission', () => {
        browser.asked += 1;

        if (answer === null) {
            return Promise.reject(new Error('The browser answered nothing at all.'));
        }

        Reflect.set(binding, 'permission', answer);

        return Promise.resolve(answer);
    });

    Reflect.set(window, 'Notification', binding);

    return browser;
}

/** Whether the page is in a secure context, which is the one condition a browser withholds the whole API outside of. */
function servedSecurely(secure: boolean): void {
    Object.defineProperty(globalThis, 'isSecureContext', { configurable: true, value: secure });
}

afterEach(() => {
    Reflect.deleteProperty(globalThis, tauriBridge);
    Reflect.deleteProperty(window, '__TAURI__');
    Reflect.deleteProperty(window, 'Notification');
    servedSecurely(false);
});

describe('systemNotifierForThisApplication', () => {
    it('offers nothing where a shell announced itself but linked no notification plugin, as the Android head does', async () => {
        Reflect.set(globalThis, tauriBridge, { invoke: () => Promise.resolve(null) });

        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(false);
        await expect(notifier.raise('2 new messages')).resolves.toBe('unavailable');
    });

    it('offers nothing where neither a shell nor a browser carries the operation at all', async () => {
        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(false);
        expect(notifier.standing).toBe('unasked');
        await expect(notifier.raise('2 new messages')).resolves.toBe('unavailable');
        await expect(notifier.permit()).resolves.toBe('unasked');
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

    it('stands permitted on a shell head, which grants on demand rather than putting a dialog in front of anybody', () => {
        shellAnswering('granted');

        expect(systemNotifierForThisApplication().standing).toBe('permitted');
    });

    it('asks the shell once between the settings switch and an arrival', async () => {
        const shell = shellAnswering('granted');
        const notifier = systemNotifierForThisApplication();

        await expect(notifier.permit()).resolves.toBe('permitted');
        await notifier.raise('2 new messages');

        expect(shell.asked).toBe(1);
    });

    it('reports the shell refusing to the switch that asked, which is the Android head under #1616', async () => {
        shellAnswering('denied');

        await expect(systemNotifierForThisApplication().permit()).resolves.toBe('refused');
    });
});

describe('systemNotifierForThisApplication in a browser', () => {
    it('offers the operation where the page is served securely and the browser carries the API', () => {
        servedSecurely(true);
        browserAnswering('default', 'granted');

        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(true);
        expect(notifier.standing).toBe('unasked');
    });

    it('offers nothing outside a secure context, which is a client endpoint served over plain http', async () => {
        servedSecurely(false);
        browserAnswering('granted', 'granted');

        const notifier = systemNotifierForThisApplication();

        expect(notifier.offered).toBe(false);
        await expect(notifier.raise('2 new messages')).resolves.toBe('unavailable');
    });

    it('stands refused where the browser has already been told to block them, and puts no second question to it', async () => {
        servedSecurely(true);
        const browser = browserAnswering('denied', 'granted');

        const notifier = systemNotifierForThisApplication();

        expect(notifier.standing).toBe('refused');
        await expect(notifier.permit()).resolves.toBe('refused');
        expect(browser.asked).toBe(0);
    });

    it('asks the browser from the gesture, and once however many gestures are made', async () => {
        servedSecurely(true);
        const browser = browserAnswering('default', 'granted');
        const notifier = systemNotifierForThisApplication();

        const answers = await Promise.all([notifier.permit(), notifier.permit()]);

        expect(answers).toEqual(['permitted', 'permitted']);
        expect(browser.asked).toBe(1);
    });

    it('reads a prompt somebody closed without answering as unasked rather than as a refusal', async () => {
        servedSecurely(true);
        browserAnswering('default', 'default');

        await expect(systemNotifierForThisApplication().permit()).resolves.toBe('unasked');
    });

    it('asks again on a second gesture where the first prompt was closed without an answer', async () => {
        servedSecurely(true);
        const browser = browserAnswering('default', 'default');
        const notifier = systemNotifierForThisApplication();

        await notifier.permit();
        await notifier.permit();

        expect(browser.asked).toBe(2);
    });

    it('reads the standing at each ask, so a dialog reopened after a grant draws what the browser now holds', async () => {
        servedSecurely(true);
        browserAnswering('default', 'granted');
        const notifier = systemNotifierForThisApplication();

        expect(notifier.standing).toBe('unasked');
        await notifier.permit();

        expect(notifier.standing).toBe('permitted');
    });

    it('holds no grant, so permission taken back from the address bar is answered rather than the old yes', async () => {
        servedSecurely(true);
        browserAnswering('default', 'granted');
        const notifier = systemNotifierForThisApplication();

        await expect(notifier.permit()).resolves.toBe('permitted');
        Reflect.set(window.Notification, 'permission', 'denied');

        await expect(notifier.permit()).resolves.toBe('refused');
        expect(notifier.standing).toBe('refused');
    });

    it('keeps a browser that answered nothing unasked, so nothing is written on the device for it', async () => {
        servedSecurely(true);
        browserAnswering('default', null);

        await expect(systemNotifierForThisApplication().permit()).resolves.toBe('unasked');
    });

    it('raises nothing and asks nobody where an arrival lands before anybody was asked', async () => {
        servedSecurely(true);
        const browser = browserAnswering('default', 'granted');

        const raised = await systemNotifierForThisApplication().raise('2 new messages');

        expect(raised).toBe('unavailable');
        expect(browser.asked).toBe(0);
        expect(browser.raised).toEqual([]);
    });

    it('raises one carrying the sentence and no second line, once the browser has allowed it', async () => {
        servedSecurely(true);
        const browser = browserAnswering('granted', 'granted');

        const raised = await systemNotifierForThisApplication().raise('2 new messages');

        expect(raised).toBe('raised');
        expect(browser.raised).toEqual([{ title: '2 new messages', body: undefined }]);
    });

    it('reports somebody acting on one by bringing the window to the front, and stops when the subscription does', async () => {
        servedSecurely(true);
        const browser = browserAnswering('granted', 'granted');
        const front = vi.spyOn(window, 'focus').mockImplementation(() => undefined);
        const acted = vi.fn();
        const notifier = systemNotifierForThisApplication();

        const stop = notifier.whenActedOn(acted);
        await notifier.raise('2 new messages');
        browser.last?.onclick?.();

        expect(front).toHaveBeenCalledOnce();
        expect(acted).toHaveBeenCalledOnce();

        stop();
        browser.last?.onclick?.();

        expect(acted).toHaveBeenCalledOnce();

        front.mockRestore();
    });

    it('reports a browser that exposes the constructor and refuses to construct one as carrying no answer', async () => {
        servedSecurely(true);
        const refusing = function () {
            throw new Error('A service worker has to show one here.');
        } as unknown as typeof window.Notification;

        Reflect.set(refusing, 'permission', 'granted');
        Reflect.set(refusing, 'requestPermission', () => Promise.resolve('granted'));
        Reflect.set(window, 'Notification', refusing);

        await expect(systemNotifierForThisApplication().raise('2 new messages')).resolves.toBe('unavailable');
    });

    it('reads the browser record at each arrival, so permission withdrawn mid-run stops raising them', async () => {
        servedSecurely(true);
        const browser = browserAnswering('granted', 'granted');
        const notifier = systemNotifierForThisApplication();

        await notifier.raise('2 new messages');
        Reflect.set(window.Notification, 'permission', 'denied');

        await expect(notifier.raise('1 task reminder')).resolves.toBe('refused');
        expect(browser.raised).toHaveLength(1);
    });
});
