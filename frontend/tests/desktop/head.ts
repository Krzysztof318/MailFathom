// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { spawn, type ChildProcess } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync } from 'node:fs';
import { createServer } from 'node:net';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';

// How the desktop head is driven, which is the one thing neither of the other two browser suites can do.
// `frontend/tests/AGENTS.md` § *The desktop suite* is where the boundary is drawn and where the two rules this proves
// are named; what is here is the mechanism.
//
// `tauri-driver` is the mechanism, and it is Tauri's own rather than anything invented here: it speaks the W3C
// WebDriver protocol, proxies to the platform's native driver — `WebKitWebDriver` on Linux, Microsoft Edge Driver on
// Windows — and launches the application the session names. macOS has no such driver and is therefore not a head this
// can reach, which is no loss: this repository builds no macOS bundle.
//
// The WebDriver client is written here rather than taken from a package, and the reason is proportion rather than
// taste. Four endpoints are used — open a session, find an element, act on it, evaluate a script — out of a frozen
// standard, which is about sixty lines; a driver package would be a pinned dependency, a row in
// `THIRD_PARTY_LICENSES.md`, and a re-counted census on the closure it lands in, for the same four requests. Tauri's
// own examples reach for WebdriverIO or Selenium because they are writing a first test rather than adding a third
// suite to a workspace that already has a runner.

/**
 * The port the fixture client is served on, derived from this directory exactly as `playwright.config.ts` derives the
 * browser suite's preview port and for the same reason: several worktrees run on one machine, and a run that attached
 * to another worktree's server would report on another worktree's client.
 *
 * It is read by two things that must agree — the server this suite starts, and the shell binary, which bakes the
 * address in at build time — so it is derived once here and never written down twice.
 */
export const fixturePort =
    20000 +
    (createHash('sha256')
        .update(import.meta.dirname)
        .digest()
        .readUInt16BE(0) %
        12768);

export const fixtureOrigin = `http://127.0.0.1:${String(fixturePort)}`;

/**
 * The shell binary this suite drives, which `scripts/build-desktop-head.sh` writes.
 *
 * A debug build without bundles: what is under test is the WebView the shell opens, and a `deb` takes minutes to
 * produce and adds nothing this suite reads.
 */
export const desktopBinary = resolve(import.meta.dirname, '../../src-tauri/target/debug/mailfathom-desktop');

/** What a WebDriver element reference is called in the protocol, which is a constant rather than anything readable. */
const elementKey = 'element-6066-11e4-a52e-4f735466cecf';

/** How long a condition is given to become true, in milliseconds. A WebView is started per case, so this is generous. */
const settleWithin = 30_000;

/** A head that has been started, signed in to or not, and the handful of things this suite asks of one. */
export interface DesktopHead {
    /** What the expression answers in the WebView, as JSON the protocol could carry. */
    evaluate: <Answer>(expression: string) => Promise<Answer>;

    /** The same, polled until it answers something other than `null`, which is how this suite waits for a screen. */
    settled: <Answer>(expression: string) => Promise<Answer>;

    /** Types into the element the selector names, after waiting for it. */
    fill: (selector: string, text: string) => Promise<void>;

    /** Clicks the element the selector names, after waiting for it. */
    click: (selector: string) => Promise<void>;

    /** Ends the session and the driver behind it. Always called, so a failing case leaves no window behind. */
    close: () => Promise<void>;
}

/**
 * A directory of its own for a shell to keep what it keeps, which is how two cases are isolated from each other.
 *
 * The WebView writes the chosen language into its origin storage under the application's data directory, and that
 * outlives the process it was written by — so without this a case asserting what a first run resolves would be reading
 * back whatever the case before it chose, and the suite would pass or fail by the order it happened to run in.
 */
export function freshProfile(): string {
    return mkdtempSync(resolve(tmpdir(), 'mailfathom-desktop-'));
}

/**
 * Opens the desktop head under an environment of its own.
 *
 * The environment is the whole point: neither the timezone nor the language preference is something a WebDriver session
 * can be asked for, because a WebView reads both from the process it was started in. `tauri:options` carries no `env`,
 * and the application inherits the driver's environment — so a case that needs a different zone or a different
 * preference needs a driver of its own, which is why one is started per case rather than one per run.
 *
 * @param environment What is added to this process's environment for the shell being started.
 * @param profile Where the shell keeps what it keeps. A fresh one unless a case is deliberately restarting a shell.
 */
export async function openDesktopHead(
    environment: Readonly<Record<string, string>>,
    profile: string = freshProfile(),
): Promise<DesktopHead> {
    const driverPort = await reserveFreePort();
    const nativePort = await reserveFreePort();

    const driver = spawn('tauri-driver', ['--port', String(driverPort), '--native-port', String(nativePort)], {
        env: {
            ...process.env,
            XDG_DATA_HOME: resolve(profile, 'data'),
            XDG_CONFIG_HOME: resolve(profile, 'config'),
            XDG_CACHE_HOME: resolve(profile, 'cache'),
            ...environment,
        },
        stdio: ['ignore', 'inherit', 'inherit'],
    });

    const driverAddress = `http://127.0.0.1:${String(driverPort)}`;

    try {
        await answeringOn(driverAddress, driver);

        // `wry` is the name Tauri's WebView stands behind, and `tauri:options` is how the driver is told which
        // application to launch. Nothing else is asked for: the window the configuration states is the window under
        // test.
        const opened = await speak<{ sessionId: string }>(driverAddress, 'POST', '/session', {
            capabilities: {
                alwaysMatch: { 'tauri:options': { application: desktopBinary }, browserName: 'wry' },
            },
        });

        const session = `/session/${opened.sessionId}`;

        const head: DesktopHead = {
            evaluate: async <Answer>(expression: string) =>
                speak<Answer>(driverAddress, 'POST', `${session}/execute/sync`, {
                    script: `return (${expression});`,
                    args: [],
                }),

            settled: async <Answer>(expression: string) =>
                until(`the expression ${expression}`, () => head.evaluate<Answer | null>(expression)),

            fill: async (selector: string, text: string) => {
                const element = await elementFor(driverAddress, session, selector);
                await speak(driverAddress, 'POST', `${session}/element/${element}/value`, { text });
            },

            click: async (selector: string) => {
                const element = await elementFor(driverAddress, session, selector);
                await speak(driverAddress, 'POST', `${session}/element/${element}/click`, {});
            },

            close: async () => {
                try {
                    await speak(driverAddress, 'DELETE', session, undefined);
                } finally {
                    driver.kill();
                }
            },
        };

        return head;
    } catch (failure) {
        driver.kill();

        throw failure;
    }
}

/**
 * One WebDriver request, with the protocol's own failure shape read rather than a status code reported.
 *
 * Every answer is `{ "value": … }` and every failure is a `value` carrying `error` and `message`, so the message a
 * driver wrote is what a failing case says — `element not interactable` names its own diagnosis, and an HTTP status
 * would name none.
 */
async function speak<Answer>(
    driverAddress: string,
    method: 'POST' | 'DELETE',
    route: string,
    body: unknown,
): Promise<Answer> {
    const answer = await fetch(`${driverAddress}${route}`, {
        method,
        headers: { 'content-type': 'application/json' },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });

    const read = (await answer.json()) as { value: unknown };
    const value = read.value;

    if (value !== null && typeof value === 'object' && 'error' in value) {
        const stated = value as { error: string; message?: string };

        throw new Error(`The desktop head refused ${method} ${route}: ${stated.error} — ${stated.message ?? ''}`);
    }

    return value as Answer;
}

/** The element a CSS selector names, waited for, because a screen is rendered rather than served. */
async function elementFor(driverAddress: string, session: string, selector: string): Promise<string> {
    return until(`the element ${selector}`, async () => {
        try {
            const found = await speak<Record<string, string>>(driverAddress, 'POST', `${session}/element`, {
                using: 'css selector',
                value: selector,
            });

            return found[elementKey] ?? null;
        } catch {
            // A selector that matches nothing yet is a screen that has not finished rendering, which is the ordinary
            // case here rather than a failure: the driver answers `no such element` and this asks again.
            return null;
        }
    });
}

/**
 * Polls until the answer is something other than `null`, or says what it was still waiting for.
 *
 * Written so that an error means *keep waiting* and only the timeout ends it, which is the opposite of a loop whose
 * probe failing reads as the condition having been met. What is being waited for is carried rather than derived,
 * because a probe that swallows a failure to keep waiting has also swallowed the only diagnosis of it: the timeout
 * message is the whole of what a failing case says.
 */
async function until<Answer>(waitingFor: string, read: () => Promise<Answer | null>): Promise<Answer> {
    const giveUpAt = Date.now() + settleWithin;

    for (;;) {
        const answer = await read();

        if (answer !== null) {
            return answer;
        }

        if (Date.now() > giveUpAt) {
            throw new Error(`The desktop head never answered ${waitingFor} within ${String(settleWithin)}ms.`);
        }

        await new Promise((carryOn) => setTimeout(carryOn, 250));
    }
}

/** Waits for the driver to be listening, and says so rather than timing out where it has already died. */
async function answeringOn(driverAddress: string, driver: ChildProcess): Promise<void> {
    await until('a WebDriver status', async () => {
        if (driver.exitCode !== null) {
            throw new Error(
                `tauri-driver exited with ${String(driver.exitCode)} before it answered. On Linux it needs WebKitWebDriver on the path, which the webkitgtk-webdriver package provides.`,
            );
        }

        try {
            const answer = await fetch(`${driverAddress}/status`);

            return answer.ok ? true : null;
        } catch {
            return null;
        }
    });
}

/**
 * A port the operating system says is free.
 *
 * Asked for rather than fixed, for the reason `src-tauri/run-tauri.ts` asks for one: two drivers on one machine would
 * otherwise contend, and the second would attach to the first.
 */
async function reserveFreePort(): Promise<number> {
    const probe = createServer();

    try {
        await new Promise<void>((listening, failed) => {
            probe.once('error', failed);
            probe.listen(0, '127.0.0.1', listening);
        });

        const reserved = probe.address();

        if (reserved === null || typeof reserved === 'string') {
            throw new Error('The operating system reported no port for the driver.');
        }

        return reserved.port;
    } finally {
        probe.close();
    }
}
