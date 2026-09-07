// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createServer, type Server } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve as resolvePath, sep } from 'node:path';
import { chromium, type Browser, type Locator, type Page } from '@playwright/test';
import { password, userName } from '../tests/fixtures/deployment';

// The browser half of the design-parity loop. `scripts/capture-design.sh` and `scripts/capture-client.sh` are the two
// entry points; each reads a manifest, decides which pairs an invocation is producing, and hands the whole plan to this
// module on standard input. Nothing here reads a manifest or chooses a screen, which is what keeps the two sides
// captured by one piece of browser code: a pair that differed in viewport, pointer, device pixel ratio or settling
// would be a comparison of two rasterisations rather than of two designs, and that is the failure this whole
// arrangement exists to remove.
//
// It is tooling rather than client source: it never reaches a bundle, `src/Client.App` does not import it, and it sits
// beside `src-tauri/run-tauri.ts` in the workspace's own TypeScript that `tsconfig.json` type-checks and `jiti` runs.

/** One of the design project's four framed sizes, and whether the pointer at it is coarse. */
interface Composition {
    name: string;
    width: number;
    height: number;
    touch: boolean;
}

/**
 * One thing done to a screen before it is captured.
 *
 * Two verbs, because two is what reaching a resting state takes: press something, or wait until something is there.
 * A target is named by its role and accessible name where the side has an accessibility tree — which the client always
 * does — and by its text where it does not, which is how the design's own artboards are reached.
 */
interface Step {
    click?: Target;
    expect?: Target;
}

interface Target {
    role?: string;
    name?: string;
    text?: string;
    nth?: number;
}

/** One capture: a screen, at a composition, reached by these steps. */
interface Pair {
    screen: string;
    composition: Composition;

    /** The design side: the mirrored file to open, and the component properties to set on it before anything else. */
    file?: string;
    properties?: Record<string, unknown>;

    /** The client side: the address to open, and whether the credential has to be given first. */
    route?: string;
    signIn?: boolean;

    steps: Step[];
}

interface Plan {
    side: 'design' | 'client';

    /** Where the captures are written. `scripts/` confines this; nothing here invents a path. */
    outputDirectory: string;

    /** Light or dark, applied to both sides the same way — as the browser's own `prefers-color-scheme`. */
    theme: 'light' | 'dark';

    /** The design side: the mirror directory this module serves the artboards out of. */
    designRoot?: string;

    /** The client side: the origin the development server serving the fixture corpus is already answering on. */
    clientOrigin?: string;

    pairs: Pair[];
}

/** How long a screen is given to settle before it is captured, in milliseconds. */
const settleFor = 400;

// Every animation and transition stopped, and the caret hidden. Both sides get it, and both need it for the same
// reason: a capture taken mid-transition differs from the same capture taken again, which would report the renderer's
// timing as a difference between the design and the client.
const stillness = `*, *::before, *::after {
    animation-duration: 0s !important;
    animation-delay: 0s !important;
    transition-duration: 0s !important;
    transition-delay: 0s !important;
}
* { caret-color: transparent !important; }`;

const contentTypes: Readonly<Record<string, string>> = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.woff2': 'font/woff2',
};

/**
 * Serves one directory over the loopback, and remembers every path it had nothing for.
 *
 * The design mirror carries the project's screen sources and its runtime and none of its binary assets, because a
 * `read_file` result is text and an image is not. So a design capture legitimately asks for files that are not there,
 * and the honest thing is to name them: an unanswered asset is a region that will differ for a reason that is not a
 * defect in the client, and a report that did not say so would send somebody to fix the wrong thing.
 */
function serveDirectory(root: string): { server: Server; unanswered: string[] } {
    const unanswered: string[] = [];
    const base = resolvePath(root);

    const server = createServer((request, response) => {
        const asked = decodeURIComponent(new URL(request.url ?? '/', 'http://served').pathname);
        const file = resolvePath(join(base, normalize(asked)));

        // The paths come out of a design file rather than out of this repository, so one that walks out of the mirror
        // is refused here rather than trusted to be well behaved.
        if (file !== base && !file.startsWith(base + sep)) {
            response.writeHead(403).end();

            return;
        }

        readFile(file).then(
            (body) => {
                response.writeHead(200, { 'content-type': contentTypes[extname(file)] ?? 'application/octet-stream' });
                response.end(body);
            },
            () => {
                unanswered.push(asked);
                response.writeHead(404).end();
            },
        );
    });

    return { server, unanswered };
}

function listening(server: Server): Promise<number> {
    return new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();

            if (address === null || typeof address === 'string') {
                reject(new Error('The static server bound to something that is not a TCP port.'));

                return;
            }

            resolve(address.port);
        });
    });
}

/**
 * The proxy the browser has to go through, where this machine has one.
 *
 * An agent session runs behind an authenticating proxy, and Chromium fails every outbound request without it — which
 * arrives as a design artboard that never boots, because its runtime is fetched rather than mirrored. The bypass is
 * the environment's own, so the loopback servers above are still reached directly.
 */
function proxyFromEnvironment(): { server: string; username: string; password: string; bypass: string } | null {
    const configured = process.env['HTTPS_PROXY'] ?? process.env['HTTP_PROXY'];

    if (configured === undefined || configured === '') {
        return null;
    }

    const address = new URL(configured);

    return {
        server: `${address.protocol}//${address.host}`,
        username: decodeURIComponent(address.username),
        password: decodeURIComponent(address.password),
        bypass: process.env['NO_PROXY'] ?? 'localhost,127.0.0.1',
    };
}

function locate(page: Page, target: Target): Locator {
    const found =
        target.role === undefined
            ? page.getByText(target.text ?? '', { exact: true })
            : page.getByRole(
                  target.role as Parameters<Page['getByRole']>[0],
                  target.name === undefined ? {} : { name: target.name },
              );

    return target.nth === undefined ? found.first() : found.nth(target.nth);
}

async function walk(page: Page, steps: Step[]): Promise<void> {
    for (const step of steps) {
        if (step.expect !== undefined) {
            await locate(page, step.expect).waitFor({ state: 'visible' });
        }

        if (step.click !== undefined) {
            await locate(page, step.click).click();
        }
    }
}

// Page-side code is written as an expression rather than as a closure, which is the same rule `tests/client.spec.ts`
// follows and for the same reason: this workspace's own TypeScript is compiled without a DOM declaration, so a
// function naming `document` or `window` here would be the one thing that changed about it.

/** Whether the design runtime has finished booting the artboard, which is a different thing from the page having loaded. */
const artboardIsMounted = "typeof window.__dcRootName === 'function' && !!window.__dcRootName()";

/** Every web font the page asked for having arrived, resolved to nothing so it can cross the wire. */
const fontsHaveArrived = 'document.fonts.ready.then(() => undefined)';

/** Everything a page has to have finished before it is worth a picture. */
async function settle(page: Page): Promise<void> {
    await page.waitForLoadState('networkidle');
    await page.evaluate(fontsHaveArrived);
    await page.waitForTimeout(settleFor);
}

async function captureDesign(browser: Browser, plan: Plan, origin: string): Promise<string[]> {
    const written: string[] = [];

    for (const pair of plan.pairs) {
        const context = await browser.newContext({
            viewport: { width: pair.composition.width, height: pair.composition.height },
            deviceScaleFactor: 1,
            hasTouch: pair.composition.touch,
            isMobile: pair.composition.touch,
            colorScheme: plan.theme,
        });
        const page = await context.newPage();

        await page.goto(`${origin}/${encodeURIComponent(pair.file ?? '')}`);

        // The artboard is a component the design runtime boots after fetching React, so the page being loaded says
        // nothing about the screen being drawn. The runtime publishes the mounted component's name when it is.
        await page.waitForFunction(artboardIsMounted);
        await page.addStyleTag({ content: stillness });

        if (pair.properties !== undefined) {
            await page.evaluate(`window.__dcSetProps(window.__dcRootName(), ${JSON.stringify(pair.properties)})`);
        }

        await settle(page);
        await walk(page, pair.steps);
        await settle(page);

        const file = join(plan.outputDirectory, `${pair.screen}-${pair.composition.name}.design.png`);

        await page.screenshot({ path: file });
        written.push(file);
        await context.close();
    }

    return written;
}

async function captureClient(browser: Browser, plan: Plan): Promise<string[]> {
    const written: string[] = [];

    for (const pair of plan.pairs) {
        const context = await browser.newContext({
            viewport: { width: pair.composition.width, height: pair.composition.height },
            deviceScaleFactor: 1,
            hasTouch: pair.composition.touch,
            isMobile: pair.composition.touch,
            colorScheme: plan.theme,
        });
        const page = await context.newPage();

        await page.goto(`${plan.clientOrigin ?? ''}${pair.route ?? '/'}`);
        await page.addStyleTag({ content: stillness });

        if (pair.signIn === true) {
            // The credential is the corpus's own rather than a second copy of it written here, which is the same rule
            // the browser suite follows: the example mail and the credential that reaches it are one file.
            await page.getByRole('textbox', { name: 'Login' }).fill(userName);
            await page.getByLabel('Password', { exact: true }).fill(password);
            await page.getByRole('button', { name: 'Connect' }).click();
            await page.getByRole('navigation', { name: 'Spaces' }).waitFor({ state: 'visible' });

            // Signing in lands on the client's default space, so an address naming another one is asked for again —
            // the fragment was read before the credential was accepted.
            if (pair.route !== undefined && pair.route !== '/') {
                await page.goto(`${plan.clientOrigin ?? ''}${pair.route}`);
                await page.addStyleTag({ content: stillness });
            }
        }

        await settle(page);
        await walk(page, pair.steps);
        await settle(page);

        const file = join(plan.outputDirectory, `${pair.screen}-${pair.composition.name}.client.png`);

        await page.screenshot({ path: file });
        written.push(file);
        await context.close();
    }

    return written;
}

async function readPlan(): Promise<Plan> {
    const chunks: Buffer[] = [];

    for await (const chunk of process.stdin) {
        chunks.push(chunk as Buffer);
    }

    return JSON.parse(Buffer.concat(chunks).toString('utf8')) as Plan;
}

async function main(): Promise<void> {
    const plan = await readPlan();

    // How many pairs one invocation produces is bounded by `scripts/capture-plan.sh`, which is where both entry
    // points read the manifest and decide what they were asked for. It is stated there rather than here so the two
    // sides cannot be bounded differently.
    if (plan.pairs.length === 0) {
        throw new Error('The plan names no pair to capture.');
    }

    const proxy = proxyFromEnvironment();
    const browser = await chromium.launch(proxy === null ? {} : { proxy });
    let served: { server: Server; unanswered: string[] } | null = null;

    try {
        let written: string[];

        if (plan.side === 'design') {
            served = serveDirectory(plan.designRoot ?? '');
            const port = await listening(served.server);

            written = await captureDesign(browser, plan, `http://127.0.0.1:${String(port)}`);
        } else {
            written = await captureClient(browser, plan);
        }

        for (const file of written) {
            process.stdout.write(`captured ${file}\n`);
        }

        for (const asked of new Set(served?.unanswered ?? [])) {
            process.stdout.write(`unanswered ${asked}\n`);
        }
    } finally {
        await browser.close();
        served?.server.close();
    }
}

await main();
