// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';

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

// What the deployment the preview server stands in for answers. It accepts any credential presented to it, because
// what this suite proves about signing in is the composing, the sending, and the keeping — which of two passwords a
// service accepts is the service's own decision and is proven where that decision is made.
// The grant it reports is what decides which spaces the client offers, so it names both the client acts on: a session
// answer without them would open a frame with Discover and the intent field absent, which is a different screen from
// the one every test below is about.
const sessionAnswer = {
    service: 'MailFathom',
    version: declaredVersion,
    permissions: ['mailfathom.mail.read', 'mailfathom.mail.ask'],
};

const mailAccounts = {
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'work',
            displayName: 'Work',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
            behind: false,
        },
    ],
};

// The tree the Mail space is scoped by, standing in for what the folders route answers. One mailbox with an inbox and
// a folder nested where a mail server nests one, which is what the reload below is read against.
const mailFolders = {
    synchronizationEnabled: true,
    accounts: [
        {
            account: mailAccounts.accounts[0],
            folders: [
                {
                    alias: 'INBOX',
                    role: 'Inbox',
                    path: ['INBOX'],
                    storedEmailCount: 4213,
                    unreadEmailCount: 12,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                    behind: false,
                },
                {
                    alias: 'ARCHIVE-2024',
                    role: null,
                    path: ['Archive', '2024'],
                    storedEmailCount: 980,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: '2026-08-31T09:00:00+00:00',
                    behind: false,
                },
            ],
        },
    ],
};

// The password this suite signs in with, and the RFC 7617 value the client is expected to compose out of it. Neither
// belongs to anybody: the deployment is the preview server, and nothing here reaches a machine holding real mail.
const userName = 'owner';
const password = 'open sesame';
const expectedAuthorization = 'Basic b3duZXI6b3BlbiBzZXNhbWU=';

// The message the Mail space draws, standing in for one a deployment would hold, and the closest thing this suite has
// to a mailbox. Nothing in it comes from a real one and nothing in it names a host this page could actually reach: the
// blocks are chosen so that each the catalogue holds is drawn at least once, and so that the two things a sender may
// try are visible — markup written as text, and a link whose words name one place while its target names another.
//
// A one-pixel transparent picture stands in for a part the message carried itself; `pictures.invalid` stands in for one
// it asked to be fetched, and is the host the assertions below watch for.
const transparentPicture = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

function run(text: string, overrides: Readonly<Record<string, unknown>> = {}) {
    return { text, emphasis: 'None', foreground: null, link: null, ...overrides };
}

function messageBlocks(pictureSource: string) {
    return [
        { type: 'heading', version: 1, level: 1, content: [run('This week at Example')], alignment: 'Start' },
        {
            type: 'paragraph',
            version: 1,
            content: [
                run('A sender may write '),
                run('<script>alert(1)</script>', { emphasis: 'Bold, Monospace' }),
                run(' and it stays words.'),
            ],
            alignment: 'Inherited',
        },
        {
            type: 'paragraph',
            version: 1,
            content: [
                run('example.invalid', {
                    link: {
                        target: 'https://offers.invalid/claim',
                        host: 'offers.invalid',
                        asciiHost: null,
                        deception: 'DisplayedHostDiffers',
                        isWorthWarningAbout: true,
                    },
                }),
            ],
            alignment: 'Inherited',
        },
        {
            type: 'image',
            version: 1,
            image: { source: pictureSource, alternativeText: 'The Example mark', width: 32, height: 32 },
            link: null,
            alignment: 'Center',
        },
        {
            type: 'quote',
            version: 1,
            depth: 1,
            blocks: [
                { type: 'paragraph', version: 1, content: [run('You wrote: send me the list.')], alignment: 'Start' },
            ],
        },
        { type: 'separator', version: 1 },
        { type: 'preformatted', version: 1, text: '  order  quantity\n  kettle 1' },
    ];
}

function messageBody(remoteImages: boolean): string {
    return JSON.stringify({
        storedEmailId: '00000000-0000-4000-8000-000000000000',
        availability: 'Readable',
        plainText: {
            text: 'A newsletter, as words.\n\nRead it at example.invalid.',
            originalCharacterCount: 48,
            truncation: 'None',
        },
        document: {
            schemaVersion: 1,
            blocks: messageBlocks(remoteImages ? 'https://pictures.invalid/mark.png' : transparentPicture),
            refusal: 'None',
            removedRemoteReferenceCount: remoteImages ? 0 : 3,
            retainedRemoteImageCount: remoteImages ? 1 : 0,
            inlineImageCount: remoteImages ? 0 : 1,
            undrawnInlineImageCount: 0,
            truncated: false,
        },
        remoteImagesRequested: remoteImages,
    });
}

// The heading the message below carries. The sender wrote it as their own first-level heading, and two levels above it
// are already taken — the space's own title and the subject the reading pane draws — so the pane draws it two deeper,
// which is the assertion in the level rather than an accident of the fixture.
const messageHeading = { name: 'This week at Example', level: 3 } as const;

/** The subject the message below carries, which is what names the region the pane draws it in. */
const messageRegion = { name: 'A newsletter from Example' } as const;

/** What the attachment below holds, small enough to state here and large enough to arrive in more than nothing. */
const attachedOctets = 'order,quantity\nkettle,1\n';

/** What the message route answers with: everything the pane draws around a body it never carries. */
const messageDescription = JSON.stringify({
    storedEmailId: '00000000-0000-4000-8000-000000000000',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    sizeOctets: 40_960,
    headers: {
        subject: messageRegion.name,
        sentAt: '2026-08-31T09:41:00+00:00',
        receivedAt: '2026-08-31T09:41:10+00:00',
        participants: [
            { role: 'From', address: 'news@example.invalid', displayName: 'Example' },
            { role: 'To', address: 'reader@example.invalid', displayName: null },
        ],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    body: { availability: 'Readable', plainText: true, html: true },
    sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
    attachments: [
        {
            position: 0,
            fileName: 'orders.csv',
            wasFileNameNormalized: false,
            mediaType: 'text/csv',
            sizeOctets: attachedOctets.length,
        },
    ],
    carried: null,
    unread: true,
    flagged: false,
    answered: false,
});

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
    await page.route('**/api/client/messages/*', (route) =>
        route.fulfill({ status: 200, contentType: 'application/json', body: messageDescription }),
    );

    // The one route that answers with octets rather than with JSON, which is what makes a real download something this
    // suite can watch: the built bundle composes the request, sends it with the credential it holds, reads the stream,
    // and hands the browser a file — none of which jsdom has any of.
    await page.route('**/api/client/messages/*/attachments/*', (route) =>
        route.fulfill({
            status: 200,
            contentType: 'text/csv',
            body: attachedOctets,
        }),
    );

    await page.route('**/api/client/session', (route) =>
        route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessionAnswer) }),
    );

    await page.route('**/api/client/accounts', (route) =>
        route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(mailAccounts) }),
    );

    await page.route('**/api/client/folders', (route) =>
        route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(mailFolders) }),
    );

    await page.route('**/api/client/emails*', (route) =>
        route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: timelinePage(new URL(route.request().url())),
        }),
    );

    // The one route whose answer depends on what the client asked for: the reader's ask for the sender's pictures is
    // in the query and nowhere else, so answering it here is what lets this suite watch a request leave for the
    // sender's host — and watch it not leave before the ask.
    await page.route('**/api/client/messages/*/body*', (route) =>
        route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: messageBody(new URL(route.request().url()).searchParams.get('remoteImages') === 'true'),
        }),
    );
}

// How many messages the mailbox behind the routing holds, which is the number `frontend/src/AGENTS.md` names as the
// one the client actually has to render. Nothing here is anybody's mail: every row is generated from its own number.
const mailboxSize = 214_000;
const rowsPerPage = 100;

/**
 * One page of the mailbox, keyset-paged the way the client surface pages it.
 *
 * The cursor is the row the page starts at, written as text, because what this suite proves about a cursor is that the
 * client holds one and continues from it — what a deployment encodes in one is the deployment's own business.
 */
function timelinePage(url: URL): string {
    const asked = Number(url.searchParams.get('cursor') ?? '0');
    const backward = url.searchParams.get('direction') === 'backward';
    const from = Math.max(backward ? asked - rowsPerPage : asked, 0);
    const rows = Math.min(rowsPerPage, mailboxSize - from);

    return JSON.stringify({
        emails: Array.from({ length: rows }, (_, at) => ({
            id: `message-${String(from + at)}`,
            account: 'work',
            folder: 'INBOX',
            threadId: null,
            subject: `Message ${String(from + at)}`,
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: null,
            senderAddress: `writer-${String(from + at)}@nordwind.example`,
            senderDisplayName: `Writer ${String(from + at)}`,
            toAddresses: ['owner@example.invalid'],
            unread: at % 3 === 0,
            flagged: false,
            answered: false,
            hasAttachments: at % 5 === 0,
            attachmentCount: at % 5 === 0 ? 1 : 0,
            sizeOctets: 4_096,
            preview: `The opening of message ${String(from + at)}.`,
        })),
        nextCursor: from + rows >= mailboxSize ? null : String(from + rows),
        previousCursor: from === 0 ? null : String(from),
        pageSize: rowsPerPage,
    });
}

/**
 * Reads onward the way a reader does, and answers once the list has moved.
 *
 * A wheel over the rows rather than a scroll offset written into the scroller: the scroller carries no role of its
 * own, and this suite is compiled without a DOM declaration on purpose — `tsconfig.json` says why — so a closure
 * naming an element would be the one thing that changes. A gesture needs neither.
 */
async function readOnward(page: Page, list: Locator): Promise<void> {
    const before = await list.getByRole('option').first().textContent();

    await list.getByRole('option').first().hover();
    await page.mouse.wheel(0, 6_000);

    await expect.poll(() => list.getByRole('option').first().textContent()).not.toBe(before);
}

async function signIn(page: Page): Promise<void> {
    await page.getByRole('textbox', { name: 'User name' }).fill(userName);
    await page.getByLabel('Password').fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
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
    await expect(page.getByRole('heading', { name: 'Sign in to your MailFathom' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Deployment address' })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);

    await signIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

test('sends the password as one Basic header the bundle composed, on every request it makes', async ({ page }) => {
    const presented: [string, string | undefined][] = [];

    page.on('request', (request) => {
        const address = new URL(request.url());

        if (address.pathname.startsWith('/api/client/')) {
            presented.push([address.pathname, request.headers()['authorization']]);
        }
    });

    await openSignedIn(page);

    // Every request on the client surface rather than the set of distinct values: a read that stopped carrying the
    // credential would leave the set unchanged, because the sign-in request already put the one value in it. Only the
    // built bundle answers this at all — the encoding runs through the browser's own `TextEncoder` and `btoa` after
    // the bundler has been over it, and what a screen sends is not what a component was handed in jsdom.
    expect(presented.length).toBeGreaterThan(0);
    for (const [route, authorization] of presented) {
        expect(authorization, `no credential on ${route}`).toBe(expectedAuthorization);
    }
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
    await expect(secondTab.getByRole('textbox', { name: 'User name' })).toBeVisible();
    await secondTab.close();
});

test('asks for the credential again after signing out, including across a reload', async ({ page }) => {
    await openSignedIn(page);

    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page.getByRole('textbox', { name: 'User name' })).toBeVisible();

    await page.reload();

    await expect(page.getByRole('textbox', { name: 'User name' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);
});

test('opens in Discover, under the version it was built from and the one the deployment answered', async ({ page }) => {
    await openSignedIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    // The client's own is substituted into the bundle at build time, which is the half only a built bundle proves; the
    // deployment's arrives over the wire beside it.
    await expect(page.getByText(`Client ${declaredVersion}, deployment ${declaredVersion}`)).toBeVisible();

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
    await expect(page.getByRole('heading', { name: 'Mail', level: 1 })).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.goForward();
    await expect(page.getByRole('heading', { name: 'Mail', level: 1 })).toBeVisible();
});

test('carries the question and the mailbox in scope from one space to the next', async ({ page }) => {
    await openSignedIn(page);

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('the renewal Nordwind sent');
    await page.getByRole('combobox', { name: 'Mailbox in scope' }).selectOption({ label: 'Work' });

    await page.getByRole('link', { name: 'Cases' }).click();
    await expect(page.getByRole('heading', { name: 'Cases', level: 1 })).toBeVisible();

    await expect(question).toHaveValue('the renewal Nordwind sent');
    await expect(page.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveValue('work');
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

    const bottomBar = await boxOf(navigation);
    const narrowSpace = await boxOf(space);
    expect(bottomBar.y).toBeGreaterThanOrEqual(narrowSpace.y + narrowSpace.height);

    // Nothing is hidden by width alone: the same three destinations are reachable in both shapes.
    await expect(page.getByRole('link', { name: 'Cases' })).toBeVisible();
});

test('moves a keyboard through a narrow window in the order the window shows', async ({ page }) => {
    await page.setViewportSize(narrowWindow);
    await openSignedIn(page);

    // The narrow composition draws the navigation at the bottom of the screen, and the keyboard follows the document
    // rather than the layout — so a document that put the navigation first would hand a reader the bottom bar before
    // the header at the top of the window. Only a browser answers this: jsdom has no sequential focus navigation.
    // The freshness line is the first thing the header holds, and it is a disclosure onto the account-by-account
    // reading of the same sentence — so it is where a keyboard arrives before any of the controls beside it.
    await page.keyboard.press('Tab');
    await expect(page.getByText('Every account is up to date.')).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('combobox', { name: 'Theme' })).toBeFocused();

    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeFocused();

    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Discover' })).toBeFocused();
});

test('stays usable at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await openSignedIn(page);

    // 320 CSS pixels is the bar `frontend/src/AGENTS.md` sets, and what is asked of it is that the frame still holds
    // everything rather than that it looks the same: the space, the intent field, its scope, and all three
    // destinations. Nothing is dropped by width, and the window it is measured in is the width alone.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Mailbox in scope' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Spaces' }).getByRole('link')).toHaveCount(3);
});

test('signs in at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await servedByADeployment(page);
    await page.goto('/');

    // The screen in front of the frame meets the same bar the frame does, and it is the one screen nobody can go
    // around: a form that overflowed at this width would be a client somebody could not sign in to at all.
    await expect(page.getByRole('textbox', { name: 'User name' })).toBeVisible();
    await expect(page.getByLabel('Password')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();

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
    await openSignedIn(page);

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

    await page.getByRole('combobox', { name: 'Theme' }).selectOption('dark');
    await page.reload();

    // Only a real document loaded a second time proves the choice was written and read back. What it is asserted
    // through is the one attribute the whole token layer is declared against, which is what a screen is painted by.
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('opens again in the language that was chosen, after the page is loaded afresh', async ({ page }) => {
    await openSignedIn(page);

    const discover = page.getByRole('heading', { name: 'Discover', level: 1 });
    await expect(discover).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    await page.getByRole('combobox', { name: 'Language' }).selectOption('pl');
    await page.reload();

    // The assertion is the English heading being gone and the document declaring the other language, rather than the
    // Polish one being present — the catalogue is the one file in this repository deliberately not in English, and a
    // second copy of its wording here would be a string to keep in step with it and a word for the spell check to
    // object to.
    await expect(discover).toHaveCount(0);
    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
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

test('issues every request to the origin it was served from and to no other', async ({ page }) => {
    const origins = new Set<string>();

    page.on('request', (request) => {
        origins.add(new URL(request.url()).origin);
    });

    // Mail rather than the root, because drawing a message is where a request to somebody else's server would come
    // from: reading mail must not be what tells a sender it was read.
    await openSignedIn(page, '/#/mail');
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    // A client of one person's own mail reaches the deployment serving it and nothing else, so a font, an analytics
    // beacon, or a stray CDN reference arriving in the bundle is a privacy defect rather than a slow page. Only a
    // browser can answer this: jsdom loads no subresource, and the source says nothing about what a build inlined.
    expect([...origins]).toStrictEqual([new URL(page.url()).origin]);
});
// What only a browser can say about the reading pane, per ADR 0024. Everything else about it — every refusal the
// parser makes, every block, and every sentence — is jsdom's and lives in the unit suite beside the source.

test('draws the message as this document own elements, with nothing a sender wrote becoming one', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

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
    await openSignedIn(page, '/#/mail');

    const message = page.getByRole('article', messageRegion);

    await expect(message.getByText('goes to offers.invalid', { exact: true })).toBeVisible();
    await expect(
        message.getByText('This link does not go where its words say. It goes to offers.invalid.'),
    ).toBeVisible();
});

test('leaves the application when a link is followed rather than navigating it', async ({ page }) => {
    await openSignedIn(page, '/#/mail');
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

    await openSignedIn(page, '/#/mail');

    const download = page.getByRole('button', { name: 'Download orders.csv' });
    await expect(download).toBeVisible();
    await expect(page.getByText('text/csv')).toBeVisible();

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

test('presents the credential the bundle composed when it fetches an attached file', async ({ page }) => {
    const presented: (string | undefined)[] = [];

    await openSignedIn(page, '/#/mail');

    // Registered after the deployment's own routes so this one wins for the attachment, which is what lets the header
    // the built bundle actually sent be read rather than inferred from the source.
    await page.route('**/api/client/messages/*/attachments/*', async (route) => {
        presented.push(route.request().headers()['authorization']);

        await route.fulfill({ status: 200, contentType: 'text/csv', body: attachedOctets });
    });

    const offered = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download orders.csv' }).click();
    await offered;

    expect(presented).toStrictEqual([expectedAuthorization]);
});

test('fetches nothing from the sender until the reader asks, and asks again next time', async ({ page }) => {
    const hosts = new Set<string>();

    page.on('request', (request) => {
        hosts.add(new URL(request.url()).hostname);
    });

    await openSignedIn(page, '/#/mail');

    const askForPictures = page.getByRole('button', { name: 'Load pictures from the sender' });
    await expect(askForPictures).toBeVisible();
    await expect(page.getByText('References removed: 3')).toBeVisible();
    expect([...hosts]).not.toContain('pictures.invalid');

    await askForPictures.click();

    // Asking is what makes the request, and it is the only thing that does. The address never reached the document
    // before this click, so there was nothing for a rendering defect to fetch.
    await expect(page.getByText('Pictures are being loaded from the sender for this message.')).toBeVisible();
    await expect.poll(() => [...hosts]).toContain('pictures.invalid');

    await page.reload();

    // Nothing on either side wrote the ask down, so the message opens asking again. Only a real document reloaded
    // proves that: browser storage is what a durable answer would have been kept in.
    await expect(page.getByRole('button', { name: 'Load pictures from the sender' })).toBeVisible();
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

test('draws the three columns of the Mail space without the page scrolling sideways', async ({ page }) => {
    // The width the composition opens out at, which is where the three columns have the least room they will ever
    // have: any narrower and they are a stack instead. A column that held its width here rather than giving way is
    // what would push the page wider than the window, and only a browser answers that — jsdom computes no geometry.
    await page.setViewportSize({ width: 780, height: 800 });
    await openSignedIn(page, '/#/mail');

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();

    // The space's own box rather than the document's: the region that holds the three columns scrolls its own
    // overflow, so a column too wide for the window makes that region scroll sideways while the document stays
    // exactly as wide as the window. Asked of the landmark by its element name, because what is being measured is a
    // box rather than something a reader would look for — and as an expression rather than a closure, for the reason
    // the sign-in test above gives.
    const scrollingSideways = await page.evaluate<boolean>(
        'document.querySelector("main").scrollWidth > document.querySelector("main").clientWidth',
    );
    expect(scrollingSideways).toBe(false);
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

    const before = await list.getByRole('option').first().textContent();

    // A reload is a cold start, so where somebody was reading is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held.
    await page.reload();

    const after = page.getByRole('listbox', { name: 'Messages' });
    await expect(after.getByRole('option').first()).toBeVisible();

    expect(await after.getByRole('option').first().textContent()).toBe(before);
});
