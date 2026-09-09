// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { readdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect, test, type Browser, type Locator, type Page, type Request, type Route } from '@playwright/test';

import * as deployment from './fixtures/deployment';
import * as mail from './fixtures/mail';
import * as messages from './fixtures/messages';

// What the unit suite structurally cannot answer, asked of the directory of static files `pnpm build` writes, in a
// browser: the bundle is the built one rather than the source, the document is a real one with a history, the window
// has a width that decides a layout, and the requests are the requests a browser actually issued.
// `frontend/tests/AGENTS.md` is where that boundary is decided.

// Read the way `src/Client.App/vite.config.ts` reads it, so the assertion below is against the number the build
// substituted rather than against a second copy of it written here.
const declaredVersion = execFileSync(resolve(import.meta.dirname, '../../scripts/read-declared-version.sh'), {
    encoding: 'utf8',
}).trim();

const wideWindow = { width: 1280, height: 720 };
const narrowWindow = { width: 380, height: 720 };

// The design project's phone frame, which is the composition where the list is the whole screen and the one width at
// which the row and the head are drawn differently. It is the project's own number rather than a rounding of the
// breakpoint, so what is measured against it is what the artboard shows.
const phoneWindow = { width: 390, height: 844 };

// What this suite answers the client with is the corpus under `frontend/tests/fixtures/`, imported rather than
// declared here: it is the same example mail every client check reads, so a shape that drifts from the service is
// wrong in one place rather than in as many places as have written their own. `frontend/tests/AGENTS.md` § *The
// corpus* holds what belongs in it, and this suite importing it is the first proof that it is sufficient.
//
// The session answer is the one value with anything spread over it. Its version is the one field only a build knows,
// so the corpus states a placeholder and this suite substitutes what it read above; everything else is taken as the
// corpus states it, the grant that decides which spaces the client offers included.
const sessionAnswer = { ...deployment.sessionAnswer, version: declaredVersion };

// The heading the corpus message carries. The sender wrote it as their own first-level heading, and two levels above
// it are already taken — the space's own title and the subject the reading pane draws — so the pane draws it two
// deeper, which is the assertion in the level rather than an accident of the fixture.
const messageHeading = { name: messages.newsletterHeading, level: 3 } as const;

/** The subject that message carries, which is what names the region the pane draws it in. */
const messageRegion = { name: messages.newsletterSubject } as const;

interface Box {
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
}

// Playwright answers with nothing for an element that is not laid out, which is a different thing from an element in
// the wrong place — so it is refused by name here rather than asserted through a non-null assertion.
async function boxOf(element: Locator): Promise<Box> {
    const box = await element.boundingBox();

    if (box === null) {
        throw new Error('The element is in the document but has no layout box to read a position off.');
    }

    return box;
}

/**
 * Answers the routes the client reaches on the origin it was served from, as the deployment behind it would.
 *
 * The preview server serves the bundle and nothing else, so without this the client meets a deployment that is not
 * there — and every screen past the sign-in would be unreachable. It is the browser's own routing rather than a
 * package, and what it fakes is one side of a real exchange: the request is composed, sent, and read by the built
 * bundle exactly as it would be against a service.
 */
async function servedByADeployment(page: Page): Promise<void> {
    await page.route('**/api/client/messages/*', (route) => answering(route, messages.newsletterMessage));

    // The one route that answers with octets rather than with JSON, which is what makes a real download something this
    // suite can watch: the built bundle composes the request, sends it with the credential it holds, reads the stream,
    // and hands the browser a file — none of which jsdom has any of.
    await page.route('**/api/client/messages/*/attachments/*', (route) =>
        route.fulfill({
            status: 200,
            contentType: 'text/csv',
            body: messages.attachedOctets,
        }),
    );

    // The one route this suite answers from state rather than from the corpus. What the client is asked to prove
    // about the two preferences held on the deployment is that a choice made in one session is in force in the next,
    // and a route answering a fixed document would prove the read alone while quietly passing a client that wrote
    // nothing at all — so the corpus states what a deployment holds before anything was chosen, and this holds what
    // was chosen since.
    let held = { ...deployment.clientPreferences };

    await page.route('**/api/client/preferences', (route) => {
        if (route.request().method() === 'POST') {
            held = JSON.parse(route.request().postData() ?? '{}') as typeof held;
        }

        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) });
    });

    // Who the signed-in person is, which the frame reads for the account menu and the settings screen. The portrait is
    // answered as none: what a stored one costs this suite is a second binary fixture, and every assertion below is
    // about the name and the screen around it rather than about the octets.
    await page.route('**/api/client/display-name', (route) => answering(route, deployment.ownDisplayName));

    await page.route('**/api/client/portrait', (route) => route.fulfill({ status: 204 }));

    await page.route('**/api/client/session', (route) => answering(route, sessionAnswer));

    // The exchange and the revocation beside it, which is what signing in and signing out actually reach. The glob
    // above ends at `session`, so neither is answered by it.
    await page.route('**/api/client/session/token', (route) => answering(route, deployment.mintedSession));

    await page.route('**/api/client/session/token/revocation', (route) => route.fulfill({ status: 204 }));

    await page.route('**/api/client/accounts', (route) => answering(route, deployment.mailAccounts));

    await page.route('**/api/client/folders', (route) => answering(route, deployment.mailFolders));

    // Where in the folder the client asked to read is in the query and nowhere else, so this is where it is read: the
    // corpus states a page of the mailbox given the row it starts at, and reading a request is this suite's half.
    await page.route('**/api/client/emails*', (route) => {
        const asked = new URL(route.request().url()).searchParams;
        const cursor = Number(asked.get('cursor') ?? '0');
        const from = asked.get('direction') === 'backward' ? cursor - mail.rowsPerPage : cursor;

        return answering(route, mail.timelinePage(from));
    });

    // Following a mark's evidence: the passages are in the request body rather than in the query, so the corpus is
    // asked for a resolution per passage the client named and in the order it named them.
    await page.route('**/api/client/citations/resolution', (route) => {
        const asked: unknown = JSON.parse(route.request().postData() ?? '{}');
        const citations = (asked as { citations?: { fragment?: string }[] }).citations ?? [];

        return answering(route, mail.citationResolutions(citations.map((citation) => citation.fragment ?? '')));
    });

    // The other route whose answer depends on what the client asked for: the reader's ask for the sender's pictures is
    // in the query too, so answering it here is what lets this suite watch a request leave for the sender's host — and
    // watch it not leave before the ask.
    await page.route('**/api/client/messages/*/body*', (route) => {
        const asked = new URL(route.request().url()).searchParams;

        return answering(
            route,
            messages.newsletterBody({
                remoteImages: asked.get('remoteImages') === 'true',
                fullHtml: asked.get('fullHtml') === 'true',
            }),
        );
    });
}

/** One value of the corpus put on the wire as the deployment behind the preview server would answer with it. */
function answering(route: Route, answered: unknown): Promise<void> {
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answered) });
}

/**
 * Reads onward the way a reader does, and answers once the list has moved.
 *
 * A wheel over the rows rather than a scroll offset written into the scroller: the scroller carries no role of its
 * own, and this suite is compiled without a DOM declaration on purpose — `tsconfig.json` says why — so a closure
 * naming an element would be the one thing that changes. A gesture needs neither.
 */
/**
 * The row at the top of the list once it has stopped moving.
 *
 * A wheel gesture goes on arriving after the row under it has changed, and where the reader is is written down only
 * once the list has rested — so the row read the instant it changes is not the row the client will remember. Two reads
 * that agree across longer than that rest is what says the list has settled on one.
 */
async function restingFirstRow(list: Locator): Promise<string> {
    const row = list.getByRole('option').first();
    let previous = '';

    await expect
        .poll(
            async () => {
                const now = (await row.textContent()) ?? '';
                const rested = now !== '' && now === previous;

                previous = now;

                return rested;
            },
            { intervals: [600, 600, 600, 600] },
        )
        .toBe(true);

    return (await row.textContent()) ?? '';
}

async function readOnward(page: Page, list: Locator): Promise<void> {
    const before = await list.getByRole('option').first().textContent();

    await list.getByRole('option').first().hover();
    await page.mouse.wheel(0, 6_000);

    await expect.poll(() => list.getByRole('option').first().textContent()).not.toBe(before);
}

async function signIn(page: Page): Promise<void> {
    await page.getByRole('textbox', { name: 'Login' }).fill(deployment.userName);
    await page.getByLabel('Password', { exact: true }).fill(deployment.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
}

/** The account menu at the foot of the navigation opened, which is where the preferences and signing out live. */
async function openAccountMenu(page: Page): Promise<void> {
    const control = page.getByRole('button', { name: 'Account and preferences' });

    // The rail carries the control itself. The bottom bar has five places and spends them on three spaces, the bell,
    // and the overflow, so in a narrow window what is about the person stands one press further in — which is where
    // the design project puts it and what this helper has to know to reach it at either width.
    if (!(await control.isVisible())) {
        await page.getByRole('button', { name: 'More' }).click();
    }

    await control.click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
}

/** The settings screen opened from the row the account menu carries, which is the only way in. */
async function openSettings(page: Page): Promise<void> {
    await openAccountMenu(page);
    await page.getByRole('button', { name: 'Settings', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Settings' })).toBeVisible();
}

/** The settings surface opened on its second tab, which is where everything about the client rather than the person is. */
async function openApplicationSettings(page: Page): Promise<void> {
    await openSettings(page);
    await page.getByRole('tab', { name: 'Application' }).click();
}

/**
 * Every preferences document the page states, collected as it is sent.
 *
 * A control reports a choice and the write leaves afterwards, so anything asserted about it — its content, or merely
 * that it happened before a reload — waits on this list rather than on the click having returned.
 */
function statedPreferences(page: Page): string[] {
    const stated: string[] = [];

    page.on('request', (request) => {
        if (new URL(request.url()).pathname === '/api/client/preferences' && request.method() === 'POST') {
            stated.push(request.postData() ?? '');
        }
    });

    return stated;
}

/**
 * One segment of the theme chooser, as the label a pointer actually lands on.
 *
 * The input each segment is built around is hidden from sight rather than from the accessibility tree, so it is a
 * clipped pixel its own label covers — which is right for a person and wrong for a locator aimed at the input. What
 * receives the click in a browser is the label, so that is what this returns; the input is still what carries the role
 * and the name, and an assertion about either reaches it through the role.
 */
function themeSegment(page: Page, name: string): Locator {
    return page.getByRole('group', { name: 'Theme' }).getByText(name, { exact: true });
}

/** The client opened at an address and signed in, which is where every test about the frame starts. */
async function openSignedIn(page: Page, address = '/'): Promise<void> {
    await servedByADeployment(page);
    await page.goto(address);
    await signIn(page);
}

test('asks for a credential before any mail, and opens the frame once one is accepted', async ({ page }) => {
    await servedByADeployment(page);
    await page.goto('/');

    // The origin serving the bundle is the deployment, so the only thing missing is who is asking — which is why the
    // address is not on this screen and the credential is.
    await expect(page.getByRole('heading', { name: 'Connect your mailbox' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Server' })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);

    await signIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

test('reads its mail exactly as it always did against a deployment serving no signal channel', async ({ page }) => {
    const asked: string[] = [];
    const logged: string[] = [];

    page.on('request', (request) => {
        asked.push(new URL(request.url()).pathname);
    });
    page.on('console', (message) => {
        logged.push(message.text());
    });

    // Nothing routes the ticket, so this deployment answers it the way one built before the channel existed does: the
    // preview server has no such route and refuses it. That is the whole arrangement — what follows is every screen
    // behaving as it does in every other test in this file.
    await openSignedIn(page);
    await page.getByRole('link', { name: 'Mail' }).click();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
    await expect(page.getByRole('option', { name: /Message 0$/ })).toBeVisible();

    // The client did try, which is what makes the assertion above about a channel rather than about a client that
    // never reached for one — and it said nothing anywhere about having failed, because a channel that is down is not
    // a sentence a person reading their mail is owed.
    expect(asked).toContain('/api/client/signals/ticket');
    expect(logged.filter((line) => line.toLowerCase().includes('signal'))).toStrictEqual([]);
});

test('sends the password once to the exchange, and the session it was given on every request after', async ({
    page,
}) => {
    const presented: [string, string | undefined][] = [];

    page.on('request', (request) => {
        const address = new URL(request.url());

        if (address.pathname.startsWith('/api/client/')) {
            presented.push([address.pathname, request.headers()['authorization']]);
        }
    });

    await openSignedIn(page);

    // Every request on the client surface rather than the set of distinct values: a read that stopped carrying a
    // credential would leave the set unchanged, because the requests before it already put each value in it. Only the
    // built bundle answers this at all — the encoding runs through the browser's own `TextEncoder` and `btoa` after
    // the bundler has been over it, and what a screen sends is not what a component was handed in jsdom.
    expect(presented.length).toBeGreaterThan(0);
    for (const [route, authorization] of presented) {
        const expected =
            route === '/api/client/session/token'
                ? deployment.expectedAuthorization
                : deployment.expectedSessionAuthorization;

        expect(authorization, `wrong credential on ${route}`).toBe(expected);
    }

    // Stated as its own assertion rather than left to the loop above, because it is the property the exchange exists
    // for: the password reaches exactly one route, and no read of anybody's mail costs the deployment a derivation.
    expect(presented.filter(([, authorization]) => authorization === deployment.expectedAuthorization)).toStrictEqual([
        ['/api/client/session/token', deployment.expectedAuthorization],
    ]);
});

test('stays signed in across a reload, and asks again in a tab that was not signed in', async ({ page, context }) => {
    await openSignedIn(page);

    await page.reload();

    // A reload is a cold start for a single-page application, so surviving one is the whole of what keeping the
    // credential buys — and only a real document reloaded a second time proves it was read back rather than held.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    const secondTab = await context.newPage();
    await servedByADeployment(secondTab);
    await secondTab.goto('/');

    // What the web head keeps is kept for the tab and for nothing wider, which is the bound ADR 0023 puts on it. No
    // unit test can make that claim: a second tab is a second document, and jsdom has one.
    await expect(secondTab.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await secondTab.close();
});

test('asks for the credential again after signing out, including across a reload', async ({ page }) => {
    await openSignedIn(page);

    await openAccountMenu(page);
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();

    await page.reload();

    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);
});

test('reads a refused password itself rather than letting the browser ask for one', async ({
    page,
    context,
    baseURL,
}) => {
    if (baseURL === undefined) {
        throw new Error('The suite is configured with no base address to set a cookie against.');
    }

    const prompted: string[] = [];
    const asked: Request[] = [];

    page.on('dialog', (dialog) => prompted.push(dialog.type()));
    page.on('request', (request) => {
        if (new URL(request.url()).pathname === '/api/client/session/token') {
            asked.push(request);
        }
    });

    // A cookie on the origin the bundle was served from, which is what makes the assertion below about the request's
    // credentials mode rather than about an origin that happened to have nothing to send. The Fetch Standard gates the
    // user agent's own credential prompt on the same flag that decides whether this cookie travels, so a request that
    // carried it is a request the browser would have prompted for.
    await context.addCookies([{ name: 'mailfathom-probe', value: 'set', url: baseURL }]);

    await servedByADeployment(page);

    // The deployment refuses the password and challenges as MailFathom does wherever it accepts one — Basic named
    // first, which is what tells the client a password may be sent at all and what separates this refusal from a
    // deployment offering no password method.
    await page.route('**/api/client/session/token', (route) =>
        route.fulfill({
            status: 401,
            headers: { 'www-authenticate': 'Basic realm="MailFathom", charset="UTF-8"' },
        }),
    );

    await page.goto('/');
    await page.getByRole('textbox', { name: 'Login' }).fill(deployment.userName);
    await page.getByLabel('Password', { exact: true }).fill(deployment.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    // The screen says what the deployment decided, which is the whole point of the client reading the challenge: a
    // dialog standing in front of this sentence is one nobody can get past to the form behind it.
    await expect(page.getByText('The login or the password is not accepted by this deployment.')).toBeVisible();

    expect(asked.length).toBeGreaterThan(0);
    for (const request of asked) {
        expect((await request.allHeaders())['cookie']).toBeUndefined();
    }

    expect(prompted).toStrictEqual([]);
});

test('opens in Discover, under the version it was built from and the one the deployment answered', async ({ page }) => {
    await openSignedIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    // The client's own is substituted into the bundle at build time, which is the half only a built bundle proves; the
    // deployment's arrives over the wire beside it. Both are read at the foot of the settings screen, which is where
    // the design project draws them.
    await openSettings(page);
    await expect(page.getByText(`MailFathom v${declaredVersion} · deployment ${declaredVersion}`)).toBeVisible();
    await page.getByRole('button', { name: 'Close settings' }).click();

    // A first load at the root is written back to the address the space is actually reached at, which is what makes
    // the next assertion — reloading it — mean anything.
    await expect(page).toHaveURL(/#\/discover$/);
});

test('reaches each space by its own address, and reloads there', async ({ page }) => {
    await openSignedIn(page, '/#/cases');

    await expect(page.getByRole('heading', { name: 'Cases', level: 1 })).toBeVisible();

    // The whole reason the address is a fragment: nothing is asked of a server, so the bundle that answers `/` answers
    // this too and the client reads the space back out of the address it was reloaded at. A path would need a fallback
    // mapping every unmatched address onto the entry document, which the service deliberately does not serve.
    await page.reload();

    await expect(page.getByRole('heading', { name: 'Cases', level: 1 })).toBeVisible();
});

test('moves back and forward through its own spaces without leaving the application', async ({ page }) => {
    await openSignedIn(page);
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.getByRole('link', { name: 'Mail' }).click();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.goForward();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();
});

test('carries the question and the scope it is asked under from one space to the next', async ({ page }) => {
    await openSignedIn(page);

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('the renewal Nordwind sent');
    await page.getByRole('combobox', { name: 'What the question is asked about' }).selectOption({ label: 'Work' });

    await page.getByRole('link', { name: 'Cases' }).click();
    await expect(page.getByRole('heading', { name: 'Cases', level: 1 })).toBeVisible();

    await expect(question).toHaveValue('the renewal Nordwind sent');
    await expect(page.getByRole('combobox', { name: 'What the question is asked about' })).toHaveValue('account:work');
});

test('puts the navigation beside the workspace in a wide window and under it in a narrow one', async ({ page }) => {
    await page.setViewportSize(wideWindow);
    await openSignedIn(page);

    const navigation = page.getByRole('navigation', { name: 'Spaces' });
    const space = page.getByRole('main');

    await expect(navigation).toBeVisible();

    // Only a browser answers this: jsdom computes no geometry, so where the two regions sit relative to each other is
    // outside what the unit suite may claim. It is asked of the width alone — nothing in the client reads which head
    // it is running on, and this same tree produces both shapes.
    const rail = await boxOf(navigation);
    const wideSpace = await boxOf(space);
    expect(rail.x + rail.width).toBeLessThanOrEqual(wideSpace.x);

    await page.setViewportSize(narrowWindow);

    // Where the navigation is drawn changes with the width, but what it holds changes with a render: the bar's own
    // five places are what `useWideWorkspace` answers a `matchMedia` change with, so for one task afterwards the
    // navigation is still the rail's contents laid out across the foot of the window — 54 pixels taller than the bar,
    // because the column the rail stacks the bell and the account in is standing in a row. A box read on either side
    // of that render is a measurement of a different screen, and the pair below is read on one side of it by waiting
    // for the control only the bar draws.
    await expect(page.getByRole('button', { name: 'More' })).toBeVisible();

    const bottomBar = await boxOf(navigation);
    const narrowSpace = await boxOf(space);
    expect(bottomBar.y).toBeGreaterThanOrEqual(narrowSpace.y + narrowSpace.height);

    // Nothing is hidden by width alone. The bar has five places and spends them on three spaces, the bell, and an
    // overflow, so a space it has no room for is reached through that rather than dropped — which only a browser can
    // say, the platform's own popover being what opens and what closes it.
    await expect(page.getByRole('link', { name: 'Cases' })).toBeHidden();

    await page.getByRole('button', { name: 'More' }).click();
    await expect(page.getByRole('link', { name: 'Cases' })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByRole('link', { name: 'Cases' })).toBeHidden();
});

test('moves a keyboard through a narrow window in the order the window shows', async ({ page }) => {
    await page.setViewportSize(narrowWindow);
    await openSignedIn(page);

    // The narrow composition draws the navigation at the bottom of the screen, and the keyboard follows the document
    // rather than the layout — so a document that put the navigation first would hand a reader the bottom bar before
    // the space at the top of the window. Only a browser answers this: jsdom has no sequential focus navigation.
    // The freshness line is the first thing the space holds, and it is a disclosure onto the account-by-account
    // reading of the same sentence — so it is where a keyboard arrives before any of the controls beneath it.
    //
    // Waited for before the first key rather than after it: until the accounts have answered, that line is a sentence
    // saying the deployment is being reached, which is text rather than a disclosure and takes no focus at all. A Tab
    // pressed then lands on the control below it and stays there, and every assertion after it would be reading a tab
    // order the window was not yet showing.
    await expect(page.getByText('Every account is up to date.')).toBeVisible();

    await page.keyboard.press('Tab');
    await expect(page.getByText('Every account is up to date.')).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeFocused();

    // Past the question's own two controls, the bar's five places in the order the design project draws them: three
    // spaces, then the bell, then the overflow that holds everything else — the account among it.
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Discover' })).toBeFocused();

    await page.getByRole('link', { name: /^Agent/u }).focus();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Notifications' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'More' })).toBeFocused();

    // Nothing in the bar comes after the overflow, so the account is reached by opening it rather than by tabbing past
    // it — which is what a bar of five places costs and what the sheet is for.
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Account and preferences' })).toBeVisible();
});

test('opens the account menu from its control and hands focus back to it on Escape', async ({ page }) => {
    await openSignedIn(page);

    // The menu is the platform's own popover, so what is asserted is the platform's contract rather than any code of
    // this client's: it opens from the control that names it, its controls are reachable once it is open, Escape
    // closes it, and focus returns to the control — none of which jsdom implements, so only a browser proves it.
    const control = page.getByRole('button', { name: 'Account and preferences' });
    await control.click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();

    // The mailbox rows are the first thing under the person's name and the first thing a keyboard reaches, because
    // each is a control that puts that mailbox in scope rather than a line to read.
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Work' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('switch', { name: /Tab mode/u })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('radio', { name: 'Auto' })).toBeFocused();

    // Escape is pressed from the way out rather than from a chooser, which takes the key for its own list.
    await page.getByRole('button', { name: 'Sign out' }).focus();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();
    await expect(control).toBeFocused();
});

test('stays usable at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await openSignedIn(page);

    // 320 CSS pixels is the bar `frontend/src/AGENTS.md` sets, and what is asked of it is that the frame still holds
    // everything rather than that it looks the same: the space, the intent field, its scope, and every destination the
    // design project shows. Nothing is dropped by width, and the window it is measured in is the width alone.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'What the question is asked about' })).toBeVisible();

    // Three of the seven stand in the bar itself and the other four behind its overflow, which is what makes five
    // places enough for seven destinations. Reached rather than dropped is the whole of the claim, so both halves are
    // counted and the sheet is opened to count the second.
    const bar = page.getByRole('navigation', { name: 'Spaces' });

    await expect(bar.getByRole('link')).toHaveCount(3);

    await page.getByRole('button', { name: 'More' }).click();
    await expect(bar.getByRole('link')).toHaveCount(7);
    await expect(page.getByRole('button', { name: 'Account and preferences' })).toBeVisible();
});

test('signs in at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await servedByADeployment(page);
    await page.goto('/');

    // The screen in front of the frame meets the same bar the frame does, and it is the one screen nobody can go
    // around: a form that overflowed at this width would be a client somebody could not sign in to at all.
    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Connect' })).toBeVisible();

    // The heading is asked for here rather than in jsdom because what would take it away is a breakpoint: the brand
    // half drops its claim below the split, and a top-level heading that went with it would leave a screen reader
    // starting at the form's own `h2` at exactly the widths a phone presents. Only a browser lays that out.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // The document's own overflow rather than the body's box: `body` is a block element with no width rule, so its
    // used width is the viewport's whatever a child inside it does, and an assertion on it could not fail.
    // Asked as an expression rather than as a function, because this suite is compiled without a DOM declaration on
    // purpose — `tsconfig.json` says why — and a closure naming `document` would be the one thing that changes.
    const overflowing = await page.evaluate<boolean>(
        'document.documentElement.scrollWidth > document.documentElement.clientWidth',
    );
    expect(overflowing).toBe(false);
});

test('opens again in the theme that was chosen, after the page is loaded afresh', async ({ page }) => {
    const stated = statedPreferences(page);

    await openSignedIn(page);

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

    await openAccountMenu(page);
    await themeSegment(page, 'Dark').click();

    // Waited for rather than assumed: a click returns once it is dispatched, and navigating away from a document can
    // cancel a request the previous one had in flight — so a reload here could outrun the write it is about to prove.
    await expect.poll(() => stated).toHaveLength(1);
    await page.reload();

    // Only a real document loaded a second time proves the choice was written and read back. What it is asserted
    // through is the one attribute the whole token layer is declared against, which is what a screen is painted by.
    // With the deployment answering what it was last told, this reaches past the device store as well: a client that
    // wrote nothing would be handed `system` on the way back and paint whatever the machine prefers.
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('states the whole preferences document to the deployment when one of them is chosen', async ({ page }) => {
    const stated = statedPreferences(page);

    await openSignedIn(page);
    await openAccountMenu(page);
    await themeSegment(page, 'Dark').click();

    // Only a browser proves this: what a screen hands the transport is one thing, and what `fetch` actually puts on
    // the wire is another — an adapter dropping the body would leave every assertion above green while the deployment
    // stored an empty document over somebody's telemetry decision.
    await expect.poll(() => stated).toHaveLength(1);
    expect(JSON.parse(stated[0] ?? '')).toStrictEqual({
        telemetryEnabled: true,
        theme: 'dark',
        openMailInTabs: false,
        markReadOnOpen: true,
        expandWholeThread: false,
        embeddedHtmlMessages: false,
        aiFiltersShown: true,
    });
});

test('opens again in the language that was chosen, after the page is loaded afresh', async ({ page }) => {
    await openSignedIn(page);

    const discover = page.getByRole('heading', { name: 'Discover', level: 1 });
    await expect(discover).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    await openApplicationSettings(page);
    await page.getByRole('group', { name: 'Language' }).getByText('Polski', { exact: true }).click();
    await page.reload();

    // The assertion is the English heading being gone and the document declaring the other language, rather than the
    // Polish one being present — the catalogue is the one file in this repository deliberately not in English, and a
    // second copy of its wording here would be a string to keep in step with it and a word for the spell check to
    // object to.
    await expect(discover).toHaveCount(0);
    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
});

// The two things a modal screen owes a keyboard, and the two only a browser can answer: `dialog.showModal` is what
// puts the page behind it out of reach and closes it on Escape, and jsdom implements neither the top layer nor the
// key. What a unit test can prove about this screen is what the component does — it is proven there — and what is
// left is that the platform does the rest, which is asked here rather than reimplemented anywhere.
test('keeps the keyboard inside the settings screen while it is open', async ({ page }) => {
    await openSignedIn(page);
    await openSettings(page);

    const panel = page.getByRole('dialog', { name: 'Settings' });

    await expect(panel.locator(':focus')).toHaveCount(1);

    // Far enough round to have left a screen that merely looked modal: the panel carries fewer controls than this.
    for (let step = 0; step < 12; step += 1) {
        await page.keyboard.press('Tab');
    }

    await expect(panel.locator(':focus')).toHaveCount(1);
});

test('closes the settings screen on Escape, handing focus back to the row that opened it', async ({ page }) => {
    await openSignedIn(page);
    await openSettings(page);

    await page.keyboard.press('Escape');

    await expect(page.getByRole('dialog', { name: 'Settings' })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeFocused();
});

// The two compositions the design project draws the settings surface in, and the one assertion only a browser can
// make about them: jsdom computes no geometry, so what a unit test can prove is that the surface is one component
// with one set of controls, and what is left is the composition itself.
test('draws the settings surface as a card over the workspace in a wide window', async ({ page }) => {
    await page.setViewportSize(wideWindow);
    await openSignedIn(page);
    await openSettings(page);

    const panel = await page.getByRole('dialog', { name: 'Settings' }).boundingBox();

    // The card's own measurements rather than merely "narrower than the window": a dialog the width utility failed to
    // reach would still be a few pixels off the viewport's width once a scrollbar is counted, so bounding it below the
    // window would pass for exactly the regression this test exists to catch. Both numbers are the tokens the design
    // project's card is drawn at, in pixels at the root size this suite runs under — 28.75rem, and 78% of a 720-pixel
    // window, which is the lower of that token's two terms here.
    expect(panel?.width).toBeCloseTo(460, 0);
    expect(panel?.height).toBeCloseTo(0.78 * wideWindow.height, 0);

    // The workspace is still behind it, which is what makes changing one setting something that opens over what
    // somebody was doing rather than a place they navigated to.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

test('draws the settings surface as the whole screen in a single-pane window', async ({ page }) => {
    await page.setViewportSize(narrowWindow);
    await openSignedIn(page);
    await openSettings(page);

    const panel = await page.getByRole('dialog', { name: 'Settings' }).boundingBox();

    expect(panel?.width).toBe(narrowWindow.width);
    expect(panel?.height).toBe(narrowWindow.height);
});

// What language the client opens in when nobody has chosen one. Only a browser can answer it: `navigator.languages` is
// the machine's own preference list, jsdom reports whatever the process was started with, and what this asks is that a
// real head reads a real preference. Each case gets a context of its own because the preference is a property of the
// browser context rather than of the page.
async function openedPreferring(browser: Browser, languages: readonly string[]): Promise<Page> {
    const context = await browser.newContext({ locale: languages[0] ?? 'en-US' });

    // `locale` sets the first entry alone. The rule being proven walks the whole list, so the list itself is what is
    // put in front of the client — and an empty one is the head that reports no preference at all.
    await context.addInitScript(
        `Object.defineProperty(navigator, 'languages', { get: () => ${JSON.stringify(languages)} })`,
    );

    const page = await context.newPage();
    await servedByADeployment(page);
    await page.goto('/');

    return page;
}

test('opens in Polish on a machine that prefers Polish, with nothing configured', async ({ browser }) => {
    const page = await openedPreferring(browser, ['pl-PL', 'en-US']);

    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
    await expect(page.getByRole('button', { name: 'Connect' })).toHaveCount(0);
});

test('opens in English on a machine preferring a language the client does not carry', async ({ browser }) => {
    const page = await openedPreferring(browser, ['de-DE', 'fr-FR']);

    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('button', { name: 'Connect' })).toBeVisible();
});

test('opens in English on a head that reports no language preference at all', async ({ browser }) => {
    const page = await openedPreferring(browser, []);

    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
});

test('lets a choice outrank the machine preference, and keeps it across a reload', async ({ browser }) => {
    const page = await openedPreferring(browser, ['pl-PL']);
    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');

    // Found by the one string the client deliberately never translates — a language is named in its own language, so
    // somebody who landed in one they cannot read finds their own — rather than by a label this page is showing in
    // Polish, which would put a second copy of the catalogue in this file. The choice is a segmented group of radio
    // buttons whose inputs are hidden from sight rather than from the accessibility tree, so what a person clicks is
    // the label carrying the name, which is what this clicks too.
    const language = page.getByRole('group').filter({ has: page.getByRole('radio', { name: 'Polski' }) });

    await language.locator('label', { has: page.getByRole('radio', { name: 'English' }) }).click();
    await page.reload();

    // The machine still prefers Polish and the client still opens in English, which is the whole of what "explicit
    // outranks detected" means — and the reload is what proves the choice was written rather than held in memory.
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
});

test('keeps the folder tree as it was left, across a reload', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const everyMailbox = tree.getByRole('treeitem', { name: /^All mailboxes/ });

    await everyMailbox.click();
    await page.keyboard.press('ArrowLeft');
    await expect(everyMailbox).toHaveAttribute('aria-expanded', 'false');

    const nested = tree.getByRole('treeitem', { name: /^2024/ });
    await nested.click();
    await expect(nested).toHaveAttribute('aria-selected', 'true');

    await page.reload();

    // A reload is a cold start, so what somebody was looking at is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held: a remount in jsdom re-reads the
    // same process's storage, and what this asks is that a browser wrote it.
    await expect(tree.getByRole('treeitem', { name: /^All mailboxes/ })).toHaveAttribute('aria-expanded', 'false');
    await expect(tree.getByRole('treeitem', { name: /^2024/ })).toHaveAttribute('aria-selected', 'true');
});

// Opening a message is an act rather than a state the client lands in: the reading pane draws what a row of the list
// was opened, so every check about it starts by opening one.
async function openTheFirstMessage(page: Page): Promise<void> {
    await openSignedIn(page, '/#/mail');

    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
}

test('issues every request to the origin it was served from and to no other', async ({ page }) => {
    const origins = new Set<string>();

    page.on('request', (request) => {
        origins.add(new URL(request.url()).origin);
    });

    // Mail rather than the root, because drawing a message is where a request to somebody else's server would come
    // from: reading mail must not be what tells a sender it was read.
    await openTheFirstMessage(page);
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    // A client of one person's own mail reaches the deployment serving it and nothing else, so a font, an analytics
    // beacon, or a stray CDN reference arriving in the bundle is a privacy defect rather than a slow page. Only a
    // browser can answer this: jsdom loads no subresource, and the source says nothing about what a build inlined.
    expect([...origins]).toStrictEqual([new URL(page.url()).origin]);
});
// What only a browser can say about the reading pane, per ADR 0024. Everything else about it — every refusal the
// parser makes, every block, and every sentence — is jsdom's and lives in the unit suite beside the source.

test('draws the message as this document own elements, with nothing a sender wrote becoming one', async ({ page }) => {
    await openTheFirstMessage(page);

    const message = page.getByRole('article', messageRegion);
    await expect(message.getByRole('heading', messageHeading)).toBeVisible();

    // The isolation statement is an absence, so it is asserted as one. A frame, a script, an embedded object, or a
    // form inside the drawn message would each be a construct this path exists never to carry, and only a real
    // document says what the built bundle actually put there.
    for (const element of ['iframe', 'script', 'object', 'embed', 'form']) {
        await expect(message.locator(element)).toHaveCount(0);
    }

    // Markup a sender wrote stays the characters they wrote, in the built bundle rather than only under jsdom.
    await expect(message.getByText('<script>alert(1)</script>')).toBeVisible();
});

test('shows where a link goes, and says so when its words name somewhere else', async ({ page }) => {
    await openTheFirstMessage(page);

    const message = page.getByRole('article', messageRegion);

    await expect(message.getByText('goes to offers.invalid', { exact: true })).toBeVisible();
    await expect(
        message.getByText('This link does not go where its words say. It goes to offers.invalid.'),
    ).toBeVisible();
});

test('leaves the application when a link is followed rather than navigating it', async ({ page }) => {
    await openTheFirstMessage(page);
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    const openedHere = page.url();
    const opening = page.waitForEvent('popup');

    await page.getByRole('link', { name: 'example.invalid' }).click();

    // A new browsing context is what the web head does with a link, and the application is still the application: a
    // WebView that navigated here would have replaced it, which is the whole reason the shell owns the desktop half.
    const opened = await opening;
    await opened.close();

    expect(page.url()).toBe(openedHere);
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    // Nothing says the link failed, which is the assertion jsdom cannot make: a browser answers `window.open` with
    // nothing whenever `noopener` was asked for, so a client reading that answer as a refusal would put this sentence
    // under every link that worked — and every unit test of it would still pass.
    await expect(page.getByText('This link could not be opened.')).toHaveCount(0);
});

test('describes an attached file before it is fetched, and fetches it only when it is asked for', async ({ page }) => {
    const downloads: string[] = [];

    page.on('request', (request) => {
        if (request.url().includes('/attachments/')) {
            downloads.push(request.url());
        }
    });

    await openTheFirstMessage(page);

    const chip = page.getByRole('button', { name: 'Open orders.csv' });
    const download = page.getByRole('button', { name: 'Download orders.csv' });
    await expect(chip).toBeVisible();
    await expect(chip).toHaveAttribute('title', 'text/csv');
    await expect(chip.getByText('csv', { exact: true })).toBeVisible();
    await expect(download).toBeVisible();

    // Nothing about the file has been fetched at this point, which is what keeps opening a message the same cost
    // whether the sender attached a note or a video. Only a browser can say so: the source says what the pane intends
    // and this says what the built bundle actually put on the wire.
    expect(downloads).toStrictEqual([]);

    const offered = page.waitForEvent('download');
    await download.click();

    // The file reaches the person as a file rather than as a page, which is a browser event and nothing jsdom has.
    expect((await offered).suggestedFilename()).toBe('orders.csv');
    await expect(page.getByText('orders.csv was downloaded.')).toBeVisible();
});

test('opens an attached file inside the client rather than handing it to the machine', async ({ page }) => {
    await openTheFirstMessage(page);

    const openTheFile = page.getByRole('button', { name: 'Open orders.csv' });

    await openTheFile.click();

    // The file is drawn under its own name in a window over the message, which is the whole of what opening one means
    // where somebody does not work in tabs: the person reads it where they were reading the message, the message is
    // still there underneath, and nothing was written to their machine to get there.
    const viewer = page.getByRole('region', { name: 'orders.csv' });
    await expect(viewer.getByRole('heading', { name: 'orders.csv' })).toBeVisible();
    await expect(viewer.getByText(/kettle,1/u)).toBeVisible();
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    // Only a browser can say the octets the built bundle read were decoded rather than drawn as a download: jsdom has
    // no download of its own to distinguish it from.
    await expect(page.getByText('orders.csv was downloaded.')).toHaveCount(0);

    await viewer.getByRole('button', { name: 'Close orders.csv' }).click();

    await expect(page.getByRole('article', messageRegion)).toBeVisible();
    await expect(openTheFile).toBeFocused();
});

test('presents the session it holds when it fetches an attached file', async ({ page }) => {
    const presented: (string | undefined)[] = [];

    await openTheFirstMessage(page);

    // Registered after the deployment's own routes so this one wins for the attachment, which is what lets the header
    // the built bundle actually sent be read rather than inferred from the source.
    await page.route('**/api/client/messages/*/attachments/*', async (route) => {
        presented.push(route.request().headers()['authorization']);

        await route.fulfill({ status: 200, contentType: 'text/csv', body: messages.attachedOctets });
    });

    const offered = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download orders.csv' }).click();
    await offered;

    expect(presented).toStrictEqual([deployment.expectedSessionAuthorization]);
});

test('fetches nothing from the sender until the reader asks, and asks again next time', async ({ page }) => {
    const hosts = new Set<string>();

    page.on('request', (request) => {
        hosts.add(new URL(request.url()).hostname);
    });

    await openTheFirstMessage(page);

    const askForPictures = page.getByRole('button', { name: 'Load pictures from the sender' });
    await expect(askForPictures).toBeVisible();
    await expect(page.getByText('References removed: 3')).toBeVisible();
    expect([...hosts]).not.toContain(messages.senderPictureHost);

    await askForPictures.click();

    // Asking is what makes the request, and it is the only thing that does. The address never reached the document
    // before this click, so there was nothing for a rendering defect to fetch.
    await expect(page.getByText('Pictures are being loaded from the sender for this message.')).toBeVisible();
    await expect.poll(() => [...hosts]).toContain(messages.senderPictureHost);

    await page.reload();

    // Nothing on either side wrote the ask down, so the message opens asking again. Only a real document reloaded
    // proves that: browser storage is what a durable answer would have been kept in.
    await expect(page.getByRole('button', { name: 'Load pictures from the sender' })).toBeVisible();
});

// What only a browser can say about the second surface ADR 0024 takes: what the built bundle put in the document, and
// what it did and did not fetch. Everything else about it — the confirmation, the footer, the states, and every
// sentence — is jsdom's and lives in the unit suite beside the source.

/** The frame the sender's markup is drawn in, named by what a reader is told it is. */
const markupFrame = "The sender's own markup, drawn in isolation";

async function showTheSenderMarkup(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Show the full HTML version' }).click();
    await page.getByRole('button', { name: 'Show the HTML' }).click();
}

test('draws the sender own markup in a frame that permits neither script nor an origin', async ({ page }) => {
    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);

    const frame = page.locator(`iframe[title="${markupFrame}"]`);

    await expect(frame).toHaveAttribute('sandbox', '');
    await expect(frame).toHaveAttribute('srcdoc', /This week at Example/);
});

test('runs nothing the markup carries, and reaches no host but its own until the reader asks', async ({ page }) => {
    const hosts = new Set<string>();

    page.on('request', (request) => {
        hosts.add(new URL(request.url()).hostname);
    });

    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toBeVisible();

    // Scoped to the surface, because the message it stands over is still drawn underneath and offers the same ask: the
    // window is what the reader is looking at, and it is that one's promise being asserted.
    const surface = page.getByRole('region', { name: "The sender's own version of this message" });

    // The script inside the frame fetches from a host of its own, so a request to it would be that script having run.
    // The picture's host is the other half: the representation carries no address for it until the reader asks, so a
    // frame that fetched one would be drawing markup this client composed rather than the one it was served. The
    // sentence under the frame is the design project's own wording of that promise, which #1693 brought the surface to.
    await expect(surface.getByText(/scripts and remote content are blocked/)).toBeVisible();
    expect([...hosts]).not.toContain(messages.senderScriptHost);
    expect([...hosts]).not.toContain(messages.senderPictureHost);

    await surface.getByRole('button', { name: 'Load pictures from the sender' }).click();

    // Asking is what makes the request, on this surface exactly as in the pane. The script is unaffected by it: the
    // consent restores addresses and never restores anything that runs.
    await expect(surface.getByText(/their servers can tell it was opened/)).toBeVisible();
    await expect.poll(() => [...hosts]).toContain(messages.senderPictureHost);
    expect([...hosts]).not.toContain(messages.senderScriptHost);
});

// What only a browser can say about the shape the two surfaces take where somebody does not work in tabs: the design
// project draws each as a window over the message, and a window is the platform's own modal — which jsdom carries the
// element of and none of the behaviour of. So the focus handed back as it closes is asserted here and nowhere else.
test('leaves the markup surface for the message it was opened from, and asks again next time', async ({ page }) => {
    await openTheFirstMessage(page);

    const showTheMarkup = page.getByRole('button', { name: 'Show the full HTML version' });

    await showTheSenderMarkup(page);
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toBeVisible();

    // The message is still where it was rather than replaced, which is what a window over it means.
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    await page.getByRole('button', { name: 'Close this view' }).click();

    await expect(page.getByRole('heading', messageHeading)).toBeVisible();
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toHaveCount(0);

    // Closing a modal hands focus back to what opened it, and that is the whole reason every way out of the window
    // leaves through the element rather than by taking the surface off the screen from underneath it.
    await expect(showTheMarkup).toBeFocused();

    // Nothing on either side wrote the answer down, so the control asks again rather than reopening what was shown.
    await page.getByRole('button', { name: 'Show the full HTML version' }).click();

    await expect(page.getByRole('heading', { name: 'Show the full HTML?' })).toBeVisible();
});

// A failure the pane cannot survive, induced by refusing the element every reading surface is built around: the frame
// draws no `article` at all, so this reaches the region under test and nothing else. It is the one way to produce an
// unexpected throw in the built bundle without the client carrying a seam for it, and what is being proven is what
// only a browser can say — that a real render failure in the real bundle costs the pane rather than the document.
// Written as an expression rather than as a closure, for the reason the overflow check above gives: this suite is
// compiled without a DOM declaration on purpose, so a function naming `document` would be the one thing that changes.
const refuseTheElementAMessageIsDrawnAs = `
    (() => {
        const create = document.createElement.bind(document);

        window.drawEverythingAgain = () => {
            document.createElement = create;
        };

        document.createElement = (name, options) => {
            if (name === 'article') {
                throw new TypeError('This message cannot be drawn.');
            }

            return create(name, options);
        };
    })()
`;

test('keeps the frame usable when the reading pane throws while it is being drawn', async ({ page }) => {
    await openTheFirstMessage(page);
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    await page.evaluate(refuseTheElementAMessageIsDrawnAs);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();

    // The pane says what happened rather than the document going blank, and everything the frame holds is still there
    // to be used: the list that opened it, the mailboxes beside it, and the way to another space.
    await expect(page.getByRole('alert')).toContainText('This part of MailFathom stopped working.');
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();

    await page.getByRole('link', { name: 'Discover' }).click();

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

// The way out the pane offers, taken after whatever made it fail has stopped: the region is drawn again, and the
// keyboard goes into it rather than being left on a control that has gone with the surface it stood on. It is here
// rather than in the unit suite because jsdom answers the question wrongly — the wrapper the boundary holds draws no
// box, which is a browser's reason for refusing it focus and not something jsdom models.
test('draws the region again when it is retried, and hands the keyboard into it', async ({ page }) => {
    await openTheFirstMessage(page);
    await page.evaluate(refuseTheElementAMessageIsDrawnAs);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();
    await expect(page.getByRole('alert')).toBeVisible();

    await page.evaluate('window.drawEverythingAgain()');
    await page.getByRole('button', { name: 'Try again' }).click();

    const message = page.getByRole('article', messageRegion);

    await expect(message).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    // Somebody rather than nobody holds the keyboard, and the next tab stop says where they are: the first control
    // the region draws, which is handing the conversation to the agent from the head of the message. Both are asked in
    // a browser rather than in jsdom, which draws no boxes and so does not model the element a browser refuses focus
    // to — the wrapper this lands on was one until this change. Named in full because the head's other acts share
    // their names with the toolbar's, as the design draws them, and the toolbar has no control for this one.
    expect(await page.evaluate('document.activeElement !== document.body')).toBe(true);

    await page.keyboard.press('Tab');

    await expect(
        page.getByRole('button', { name: 'Go to the agent with this thread as context — not built yet' }),
    ).toBeFocused();
});

// The failure no boundary is left to contain, induced by refusing the element every surface in this client is built
// out of: the region fails, the boundary around it fails drawing what it says instead, and so does the last-resort
// boundary above it — which is what leaves React with nothing to render and unmounts the root. Only a browser can say
// what is left then, because what stands there is markup the document itself carries rather than anything the bundle
// renders, and `main.tsx` wiring `onUncaughtError` to it is not a claim jsdom can make about the built bundle.
const refuseTheElementEverySurfaceIsBuiltFrom = `
    (() => {
        const create = document.createElement.bind(document);

        document.createElement = (name, options) => {
            if (name === 'div') {
                throw new TypeError('Nothing in this client can be drawn.');
            }

            return create(name, options);
        };
    })()
`;

test('says so in the document itself when a failure escapes every boundary the client has', async ({ page }) => {
    await openSignedIn(page, '/#/mail');
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

    await page.evaluate(refuseTheElementEverySurfaceIsBuiltFrom);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();

    // Nothing of the client is left, so what a reader is owed is a document that says what happened rather than an
    // empty one — announced, and holding the keyboard, whatever it was on having gone with the tree.
    const carried = page.getByRole('alert');

    await expect(carried).toContainText('MailFathom stopped');
    await expect(carried).toBeFocused();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toHaveCount(0);
});

// What only a browser can say about the message list: every row is one height, the document holds a window of rows
// rather than the folder, and a reader who leaves and comes back is put back where they were. Everything else about
// it — the paging arithmetic, the states, the selection, and every sentence — is jsdom's and lives in the unit suite
// beside the source.

test('draws every row of the list at one height, which is what the window is arithmetic over', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const heights = await Promise.all(
        (await list.getByRole('option').all()).map(async (row) => (await row.boundingBox())?.height),
    );

    // The measurement the choice of windowing was made against, kept as an assertion rather than as a number in a
    // pull request: a row whose height varied with its subject, its preview, or its marks would put every row below it
    // somewhere other than where the list drew the space for it — and would be the argument for a virtualizer that
    // measures rows, which this list deliberately does not carry.
    expect(new Set(heights).size).toBe(1);
    expect(heights[0]).toBeGreaterThan(0);
});

test('opens what MailFathom made of a message from its own row, with the passages it rests on', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    const enriched = list.getByRole('option').nth(1);

    await expect(enriched).toContainText('An answer is owed before the end of the week.');

    // The row's own menu, which is where checking a reading is reached from: a row is an `option` of a listbox and
    // holds no focusable descendant, so the sentence on it cannot be a control of its own.
    await enriched.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Check what MailFathom made of it' }).click();

    const checking = page.getByRole('dialog', { name: 'What MailFathom made of this message' });

    await expect(checking.getByText('A model — agents/reader')).toBeVisible();
    await expect(checking.getByText('Please confirm the bays you want before the end of the week.')).toBeVisible();
});

test('draws the mail screens at the phone composition, with its own row height and nothing over the question', async ({
    page,
}) => {
    // The design project's own phone frame, and the only composition in which the list is the whole screen. Everything
    // asked below is geometry, which is what puts it here: how tall a row is, what stands over what, and whether the
    // document has grown wider than the window are three things jsdom answers for nothing.
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const phoneRow = await boxOf(list.getByRole('option').first());

    // The control that writes a message stands over the list rather than over the window, so it clears whatever
    // stands under the column — at this composition the navigation between spaces, which the design draws along the
    // foot of the phone. Placed against the viewport it sat on that instead, which put a press meant for another space
    // onto the control that composes. The question field is not here to clear: the design draws it at the foot of the
    // reading column alone, and at this width that column stands in front of the list only once a message is open.
    const compose = await boxOf(page.getByRole('button', { name: /^New message/u }));
    const spaces = await boxOf(page.getByRole('navigation', { name: 'Spaces' }));

    expect(compose.y + compose.height).toBeLessThanOrEqual(spaces.y);

    // Nothing has laid out past the window, which is the bar `frontend/src/AGENTS.md` sets at every width. Asked as an
    // expression for the reason the same question is asked that way at the three-column width above: this suite is
    // compiled without a DOM declaration, so a closure naming `document` is the one thing that would change that.
    const overflowing = await page.evaluate<boolean>(
        'document.documentElement.scrollWidth > document.documentElement.clientWidth',
    );

    expect(overflowing).toBe(false);

    // The one measurement a composition changes rather than merely rearranges: the design draws a taller row where the
    // list is the whole screen and one height everywhere above that, so the same row is read at both widths.
    await page.setViewportSize(wideWindow);
    await expect(list.getByRole('option').first()).toBeVisible();

    expect(phoneRow.height).toBeGreaterThan((await boxOf(list.getByRole('option').first())).height);
});

test.describe('driven by a finger', () => {
    test.use({ hasTouch: true });

    test('draws every control on the mail screens at a size a fingertip can hit', async ({ page }) => {
        await page.setViewportSize(phoneWindow);
        await openSignedIn(page, '/#/mail');

        await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

        // The floor is asked of the pointer rather than of the width, and `styles.css` is the one place it is stated —
        // so what this proves is that the statement reaches the whole screen rather than the shapes that remembered it.
        // Only a browser answers it twice over: the query is the real one, and the size is a measured box.
        for (const control of await page.getByRole('main').getByRole('button').all()) {
            const box = await control.boundingBox();

            if (box === null) {
                continue;
            }

            expect(
                Math.min(box.width, box.height),
                `a control on the Mail space is ${String(box.height)} tall`,
            ).toBeGreaterThanOrEqual(44);
        }
    });
});

test('draws the three columns of the Mail space without the page scrolling sideways', async ({ page }) => {
    // The width the third column arrives at, which is where the three of them have the least room they will ever have:
    // one pixel narrower and the mailboxes are a drawer instead. A column that held its width here rather than giving
    // way is what would push the page wider than the window, and only a browser answers that — jsdom computes no
    // geometry.
    await page.setViewportSize({ width: 1180, height: 800 });

    // With a message open, so the third column is the pane a reader actually gets rather than the note standing in
    // for it: the pane is the column that has to give way, and an empty one proves nothing about a filled one.
    await openTheFirstMessage(page);
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    // Each column against the space it stands in, reached by its role like everything else here. A column that held
    // its width rather than giving way lays out past that space's right edge, which is what makes the region scroll
    // sideways while the document stays exactly as wide as the window.
    const room = await boxOf(page.getByRole('main'));
    const columns = [
        await boxOf(page.getByRole('tree', { name: 'Mailboxes and folders' })),
        await boxOf(page.getByRole('listbox', { name: 'Messages' })),
        await boxOf(page.getByRole('article', messageRegion)),
    ];

    for (const column of columns) {
        expect(column.x).toBeGreaterThanOrEqual(room.x);
        expect(column.x + column.width).toBeLessThanOrEqual(room.x + room.width);
    }
});

test('stops a message’s own content at the reading ceiling and leaves everything around it the pane', async ({
    page,
}) => {
    // Wider than the width the design draws the pane at, which is the only regime where the ceiling binds at all: at
    // the width the composition was drawn against the pane is already narrower than it, and a pane that had lost the
    // ceiling entirely would look identical there.
    await page.setViewportSize({ width: 1920, height: 1080 });

    await openTheFirstMessage(page);
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    // The sender's own heading stands inside the message's content and the list of files beside it does not, so the
    // two boxes are the whole comparison — same left edge, different widths. What the ceiling is worth in pixels
    // is the token's business rather than this suite's; what is asserted is the shape, because both ways of getting it
    // wrong are visible here and nowhere jsdom can reach. A ceiling dropped again draws the two at one width, and a
    // centred one moves the content's left edge off the edge everything around it keeps.
    const content = await boxOf(page.getByRole('heading', messageHeading));
    const aroundIt = await boxOf(page.getByRole('list', { name: 'Files this message carries' }));

    expect(content.width).toBeLessThan(aroundIt.width);
    expect(content.x).toBeCloseTo(aroundIt.x, 0);

    // And the region the pane draws the message in keeps the pane's own width, which is what the ceiling stopped
    // binding: a head laid out to a measure meant for paragraphs is the defect this asserts against.
    const region = await boxOf(page.getByRole('article', messageRegion));
    expect(region.width).toBeGreaterThan(content.width);
});

test('holds a window of rows in the document however far down the folder it is scrolled', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const drawnAtTheTop = await list.getByRole('option').count();

    for (let read = 1; read <= 6; read += 1) {
        await readOnward(page, list);
    }

    // Hundreds of rows further down a folder of two hundred and fourteen thousand, reached a screenful at a time the
    // way somebody scrolls — and the document holds no more rows than it did on the first screen. That is the whole
    // claim windowing makes, and only a browser laying the list out can answer it.
    await expect(list.getByRole('option').first()).toContainText(/Message [1-9]\d\d/);
    expect(await list.getByRole('option').count()).toBeLessThanOrEqual(drawnAtTheTop);
    await expect(list.getByRole('option', { name: /^Writer 0\D/ })).toHaveCount(0);
});

test('starts the list at its leading end when the order changes under a reader who had scrolled', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const drawnAtTheTop = await list.getByRole('option').count();

    for (let read = 1; read <= 3; read += 1) {
        await readOnward(page, list);
    }

    // The order is behind the list's filters, opened from the control at the end of the column's head row.
    await page.getByRole('button', { name: 'Filters' }).click();
    await page.getByRole('group', { name: 'Order' }).getByText('Oldest first', { exact: true }).click();

    // Changing the order empties the list, which takes the scroller out of the document, so the one that comes back is
    // at the top however far down the reader had been. A window still computed from where they were draws the far end
    // of the first page under a screen of blank space, and no scroll is left to fire the event that would correct it —
    // which only a browser laying the list out can answer.
    await expect(list.getByRole('option').first()).toContainText('Message 0');
    expect(await list.getByRole('option').count()).toBeGreaterThanOrEqual(drawnAtTheTop);
});

test('puts a reader back where they were reading, across a reload', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    // Far enough that the row the reader is on is in a page the client had to ask for with a cursor, which is the
    // whole of what returning to a position means: a client that read the folder again from its leading end would land
    // on message zero and look identical to one that had never scrolled.
    for (let read = 1; read <= 3; read += 1) {
        await readOnward(page, list);
    }

    const before = await restingFirstRow(list);

    // A reload is a cold start, so where somebody was reading is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held.
    await page.reload();

    const after = page.getByRole('listbox', { name: 'Messages' });
    await expect(after.getByRole('option').first()).toBeVisible();

    expect(await after.getByRole('option').first().textContent()).toBe(before);
});

/** The notification centre opened, which is the panel the client's own motion is stated on. */
async function openNotifications(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Notifications' }).click();

    await expect(page.getByRole('dialog', { name: 'Notifications' })).toBeVisible();
}

// What the panel's own transition runs at, read off the open centre. Written as a self-contained expression rather
// than as a closure taking the element, for the reason the two document-level ones above give: this suite compiles
// without a DOM declaration on purpose, so a function naming `getComputedStyle` would be the one thing that changes.
// The panel is named by the same accessible name the query above finds it by, so the style read and the visibility
// assertion are about one element rather than two.
const theOpenPanelsTransitionDuration = `
    getComputedStyle(document.querySelector('dialog[open][aria-label="Notifications"]')).transitionDuration
`;

/**
 * Every duration the panel's own transition runs at, deduplicated.
 *
 * A transition over four properties reports four durations, so what is read is the set of them: a rule that removed
 * the motion from three of them and left the fourth would be a screen that still moves.
 */
async function motionDurations(page: Page): Promise<string[]> {
    const stated = await page.evaluate(theOpenPanelsTransitionDuration);

    // A read answering with anything but the string a computed style is has not measured the panel at all, and an
    // empty list stood in for it would satisfy *both* assertions below — which is how the first version of this
    // helper reported the moving case green against a page it had never read.
    expect(typeof stated).toBe('string');

    return [...new Set(String(stated).split(', '))];
}

// Only a browser resolves a media query against a real preference and computes a duration from a stylesheet, so this
// is one of the claims `pnpm test` structurally cannot make: jsdom computes no styles and would pass for a client
// carrying no such rule at all.
test('moves the notification centre onto the screen over a duration of its own', async ({ page }) => {
    await openSignedIn(page);
    await openNotifications(page);

    expect(await motionDurations(page)).not.toEqual(['0s']);
});

test('takes that motion away for a reader who asked for less of it, rather than shortening it', async ({ browser }) => {
    const context = await browser.newContext({ reducedMotion: 'reduce' });
    const page = await context.newPage();

    await openSignedIn(page);
    await openNotifications(page);

    // Removed rather than shortened, which is the accessibility obligation: the panel still arrives where it belongs,
    // and what is gone is the travel. A duration merely made small would read as motion to somebody who asked for none.
    expect(await motionDurations(page)).toEqual(['0s']);
});

test('carries no example mail and no fixture deployment in what it publishes', async () => {
    // `pnpm dev:fixtures` serves this same client out of the corpus above, so the one thing that has to be proved
    // about that convenience is that it is a convenience: a production build folds away the condition
    // `src/Client.App/src/main.tsx` reaches it behind, and nothing under `dist/` may therefore mention either the
    // corpus or the options a development run publishes on `window`. This suite is where it is asserted because this
    // suite is the one that builds — `pnpm test` never does, so it could only assert it about the source.
    const published = resolve(import.meta.dirname, '../src/Client.App/dist');
    const files = await readdir(published, { recursive: true, withFileTypes: true });
    const written = await Promise.all(
        files.filter((entry) => entry.isFile()).map((file) => readFile(resolve(file.parentPath, file.name), 'utf8')),
    );
    const bundle = written.join('\n');

    // The version the build stamped in, asserted present before anything is asserted absent: an absence read off a
    // directory nothing was read from would pass whatever the build had written.
    expect(bundle).toContain(declaredVersion);

    expect(bundle).not.toContain(messages.newsletterSubject);
    expect(bundle).not.toContain('mailfathomFixtures');
});
