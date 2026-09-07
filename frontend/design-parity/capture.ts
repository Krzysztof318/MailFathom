// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createServer, type Server } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve as resolvePath, sep } from 'node:path';
import { chromium, type Browser, type BrowserContext, type Locator, type Page } from '@playwright/test';
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
 * does — and by its text where it does not, which is how the design's own artboards are reached. A name matches as a
 * substring unless `exact` says otherwise, which is what a name that is also the tail of another control's name needs:
 * *Ustawienia* is a menu item, and also the end of the control that opens the menu it is in.
 */
interface Step {
    click?: Target;
    expect?: Target;
}

interface Target {
    role?: string;
    name?: string;
    exact?: boolean;
    text?: string;
    nth?: number;
}

/** What every capture states, whichever side it is of. */
interface Capture {
    screen: string;
    composition: Composition;
    steps: Step[];
}

/** The design side: the mirrored artboard to open, and the component properties to set on it before anything else. */
interface DesignCapture extends Capture {
    file: string;
    properties: Record<string, unknown>;
}

/** The client side: the address to open, and whether the credential has to be given first. */
interface ClientCapture extends Capture {
    route: string;
    signIn: boolean;
}

/** What both plans state. */
interface PlannedRun {
    /** Where the captures are written. `scripts/` confines this; nothing here invents a path. */
    outputDirectory: string;

    /** Light or dark, applied to both sides the same way — as the browser's own `prefers-color-scheme`. */
    theme: 'light' | 'dark';
}

// The two sides are one type discriminated by `side` rather than one type with four optional fields, because the
// fields are not optional: a design plan without a mirror directory and a client plan without an origin are each a
// plan that cannot run. Reading them apart at the boundary is what lets everything below take what it needs without a
// fallback standing in for a value that was never allowed to be absent.
type Plan =
    | (PlannedRun & { side: 'design'; designRoot: string; pairs: DesignCapture[] })
    | (PlannedRun & { side: 'client'; clientOrigin: string; pairs: ClientCapture[] });

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

// The roles `getByRole` accepts, written out because Playwright states them as a type and publishes no list of them at
// run time. A role here comes out of a manifest rather than out of this file, so it is checked against this rather than
// asserted into the union — and the check is what names the manifest entry that is wrong instead of leaving Playwright
// to fail on a locator nobody wrote. A role missing from this list fails to compile at the call below, which is how it
// stays the same set Playwright accepts.
const ariaRoles = [
    'alert',
    'alertdialog',
    'application',
    'article',
    'banner',
    'blockquote',
    'button',
    'caption',
    'cell',
    'checkbox',
    'code',
    'columnheader',
    'combobox',
    'complementary',
    'contentinfo',
    'definition',
    'deletion',
    'dialog',
    'directory',
    'document',
    'emphasis',
    'feed',
    'figure',
    'form',
    'generic',
    'grid',
    'gridcell',
    'group',
    'heading',
    'img',
    'insertion',
    'link',
    'list',
    'listbox',
    'listitem',
    'log',
    'main',
    'marquee',
    'math',
    'meter',
    'menu',
    'menubar',
    'menuitem',
    'menuitemcheckbox',
    'menuitemradio',
    'navigation',
    'none',
    'note',
    'option',
    'paragraph',
    'presentation',
    'progressbar',
    'radio',
    'radiogroup',
    'region',
    'row',
    'rowgroup',
    'rowheader',
    'scrollbar',
    'search',
    'searchbox',
    'separator',
    'slider',
    'spinbutton',
    'status',
    'strong',
    'subscript',
    'superscript',
    'switch',
    'tab',
    'table',
    'tablist',
    'tabpanel',
    'term',
    'textbox',
    'time',
    'timer',
    'toolbar',
    'tooltip',
    'tree',
    'treegrid',
    'treeitem',
] as const;

type AriaRole = (typeof ariaRoles)[number];

function isAriaRole(value: string): value is AriaRole {
    return (ariaRoles as readonly string[]).includes(value);
}

function locate(page: Page, target: Target): Locator {
    let found: Locator;

    if (target.role === undefined) {
        found = page.getByText(target.text ?? '', { exact: true });
    } else {
        if (!isAriaRole(target.role)) {
            throw new Error(`A step names the role ${target.role}, which getByRole does not accept.`);
        }

        found = page.getByRole(
            target.role,
            target.name === undefined ? {} : { name: target.name, exact: target.exact ?? false },
        );
    }

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

/** A context whose size and pointer are the composition's, so neither side of a pair can be given one of its own. */
function contextFor(browser: Browser, plan: PlannedRun, composition: Composition): Promise<BrowserContext> {
    return browser.newContext({
        viewport: { width: composition.width, height: composition.height },
        deviceScaleFactor: 1,
        hasTouch: composition.touch,
        isMobile: composition.touch,
        colorScheme: plan.theme,

        // The design project is written in Polish and reads no locale, so the client is asked for the same language:
        // a pair captured in two languages differs in every word and says nothing about the screen.
        locale: 'pl-PL',
    });
}

async function captureDesign(
    browser: Browser,
    plan: PlannedRun & { pairs: DesignCapture[] },
    origin: string,
): Promise<string[]> {
    const written: string[] = [];

    for (const pair of plan.pairs) {
        const context = await contextFor(browser, plan, pair.composition);
        const page = await context.newPage();

        await page.goto(`${origin}/${encodeURIComponent(pair.file)}`);

        // The artboard is a component the design runtime boots after fetching React, so the page being loaded says
        // nothing about the screen being drawn. The runtime publishes the mounted component's name when it is.
        await page.waitForFunction(artboardIsMounted);
        await page.addStyleTag({ content: stillness });
        await page.evaluate(`window.__dcSetProps(window.__dcRootName(), ${JSON.stringify(pair.properties)})`);

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

async function captureClient(
    browser: Browser,
    plan: PlannedRun & { clientOrigin: string; pairs: ClientCapture[] },
): Promise<string[]> {
    const written: string[] = [];

    for (const pair of plan.pairs) {
        const context = await contextFor(browser, plan, pair.composition);
        const page = await context.newPage();

        await page.goto(`${plan.clientOrigin}${pair.route}`);
        await page.addStyleTag({ content: stillness });

        if (pair.signIn) {
            // The credential is the corpus's own rather than a second copy of it written here, which is the same rule
            // the browser suite follows: the example mail and the credential that reaches it are one file. The names
            // are the client's Polish ones, because that is the language the context above asked it for.
            await page.getByRole('textbox', { name: 'Login' }).fill(userName);
            await page.getByLabel('Hasło', { exact: true }).fill(password);
            await page.getByRole('button', { name: 'Połącz' }).click();
            await page.getByRole('navigation', { name: 'Przestrzenie' }).waitFor({ state: 'visible' });

            // Signing in lands on the client's default space, so an address naming another one is asked for again —
            // the fragment was read before the credential was accepted.
            if (pair.route !== '/') {
                await page.goto(`${plan.clientOrigin}${pair.route}`);
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

// What arrives on standard input is a document a shell script composed, which is a boundary rather than a value this
// module already knows the shape of. So it is read as `unknown` and narrowed a field at a time: a plan that is wrong
// says which field is wrong, here, instead of failing several steps later inside browser automation with an error
// about something else. Each reader names the path it is at, because "a pair is missing its composition" is a
// different thing to fix from "the third pair's composition has no width".

function fields(value: unknown, at: string): Record<string, unknown> {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        throw new Error(`${at} is not an object.`);
    }

    return value as Record<string, unknown>;
}

function text(value: unknown, at: string): string {
    if (typeof value !== 'string') {
        throw new Error(`${at} is not a string.`);
    }

    return value;
}

function whole(value: unknown, at: string): number {
    if (typeof value !== 'number' || !Number.isInteger(value)) {
        throw new Error(`${at} is not a whole number.`);
    }

    return value;
}

function readTarget(value: unknown, at: string): Target {
    const given = fields(value, at);
    const target: Target = {};

    if (given['role'] !== undefined) {
        target.role = text(given['role'], `${at}.role`);
    }

    if (given['name'] !== undefined) {
        target.name = text(given['name'], `${at}.name`);
    }

    if (given['text'] !== undefined) {
        target.text = text(given['text'], `${at}.text`);
    }

    if (given['exact'] !== undefined) {
        if (typeof given['exact'] !== 'boolean') {
            throw new Error(`${at}.exact is not a boolean.`);
        }

        target.exact = given['exact'];
    }

    if (given['nth'] !== undefined) {
        target.nth = whole(given['nth'], `${at}.nth`);
    }

    if (target.role === undefined && target.text === undefined) {
        throw new Error(`${at} names neither a role nor a text to find an element by.`);
    }

    return target;
}

function readSteps(value: unknown, at: string): Step[] {
    if (!Array.isArray(value)) {
        throw new Error(`${at} is not a list of steps.`);
    }

    return value.map((entry, index) => {
        const given = fields(entry, `${at}[${String(index)}]`);
        const step: Step = {};

        if (given['click'] !== undefined) {
            step.click = readTarget(given['click'], `${at}[${String(index)}].click`);
        }

        if (given['expect'] !== undefined) {
            step.expect = readTarget(given['expect'], `${at}[${String(index)}].expect`);
        }

        if (step.click === undefined && step.expect === undefined) {
            throw new Error(`${at}[${String(index)}] is neither a click nor an expect.`);
        }

        return step;
    });
}

function readCapture(value: unknown, at: string): Capture {
    const given = fields(value, at);
    const composition = fields(given['composition'], `${at}.composition`);

    return {
        screen: text(given['screen'], `${at}.screen`),
        composition: {
            name: text(composition['name'], `${at}.composition.name`),
            width: whole(composition['width'], `${at}.composition.width`),
            height: whole(composition['height'], `${at}.composition.height`),
            touch: composition['touch'] === true,
        },
        steps: readSteps(given['steps'], `${at}.steps`),
    };
}

function readDesignCapture(value: unknown, at: string): DesignCapture {
    const given = fields(value, at);

    return {
        ...readCapture(value, at),
        file: text(given['file'], `${at}.file`),
        properties: given['properties'] === undefined ? {} : fields(given['properties'], `${at}.properties`),
    };
}

function readClientCapture(value: unknown, at: string): ClientCapture {
    const given = fields(value, at);

    return {
        ...readCapture(value, at),
        route: text(given['route'], `${at}.route`),
        signIn: given['signIn'] === true,
    };
}

function readPlan(value: unknown): Plan {
    const given = fields(value, 'the plan');
    const side = text(given['side'], 'the plan’s side');
    const theme = text(given['theme'], 'the plan’s theme');

    if (side !== 'design' && side !== 'client') {
        throw new Error(`A plan captures the design side or the client side, not ${side}.`);
    }

    if (theme !== 'light' && theme !== 'dark') {
        throw new Error(`A plan is captured under light or dark, not ${theme}.`);
    }

    const pairs = given['pairs'];

    if (!Array.isArray(pairs)) {
        throw new Error('The plan states no list of pairs.');
    }

    const run: PlannedRun = {
        theme,
        outputDirectory: text(given['outputDirectory'], 'the plan’s outputDirectory'),
    };

    return side === 'design'
        ? {
              ...run,
              side,
              designRoot: text(given['designRoot'], 'the plan’s designRoot'),
              pairs: pairs.map((entry, index) => readDesignCapture(entry, `pair ${String(index)}`)),
          }
        : {
              ...run,
              side,
              clientOrigin: text(given['clientOrigin'], 'the plan’s clientOrigin'),
              pairs: pairs.map((entry, index) => readClientCapture(entry, `pair ${String(index)}`)),
          };
}

async function planFromStandardInput(): Promise<Plan> {
    const chunks: Buffer[] = [];

    for await (const chunk of process.stdin) {
        chunks.push(chunk as Buffer);
    }

    return readPlan(JSON.parse(Buffer.concat(chunks).toString('utf8')));
}

async function main(): Promise<void> {
    const plan = await planFromStandardInput();

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
            served = serveDirectory(plan.designRoot);
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
