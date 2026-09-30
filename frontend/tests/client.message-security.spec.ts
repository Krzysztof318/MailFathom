// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import {
    messageHeading,
    messageRegion,
    openApplicationSettings,
    openSignedIn,
    openTheFirstMessage,
    test,
} from './client.harness';
import * as messages from './fixtures/messages';

// What ADR 0024 asks of the two surfaces a message is read on that only a browser can answer: the origins a page
// reaches, what the built bundle puts in the document, what it fetches from a sender and when, and the policy it all
// runs under.

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

test('fetches nothing from the sender until the reader asks, and asks again next time', async ({ page }) => {
    const hosts = new Set<string>();

    page.on('request', (request) => {
        hosts.add(new URL(request.url()).hostname);
    });

    await openTheFirstMessage(page);

    const askForPictures = page.getByRole('button', { name: 'Load images from the sender' });
    await expect(askForPictures).toBeVisible();
    await expect(page.getByText('Removed references: 3')).toBeVisible();
    expect([...hosts]).not.toContain(messages.senderPictureHost);

    await askForPictures.click();

    // Asking is what makes the request, and it is the only thing that does. The address never reached the document
    // before this click, so there was nothing for a rendering defect to fetch.
    await expect(page.getByText('Images from the sender are loaded for this message.')).toBeVisible();
    await expect.poll(() => [...hosts]).toContain(messages.senderPictureHost);

    await page.reload();

    // Nothing on either side wrote the ask down, so the message opens asking again. Only a real document reloaded
    // proves that: browser storage is what a durable answer would have been kept in.
    await expect(page.getByRole('button', { name: 'Load images from the sender' })).toBeVisible();
});

// What only a browser can say about the second surface ADR 0024 takes: what the built bundle put in the document, and
// what it did and did not fetch. Everything else about it — the confirmation, the footer, the states, and every
// sentence — is jsdom's and lives in the unit suite beside the source.

/** The frame the sender's markup is drawn in, named by what a reader is told it is. */
const markupFrame = "The sender's own markup, drawn in isolation";

async function showTheSenderMarkup(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Show the original message' }).click();
    // Exactly, because the control that opened this confirmation is named *Show the original message* and the
    // agreement on it is named *Show the original* — one is a prefix of the other, and an inexact name matches both.
    await page.getByRole('button', { name: 'Show the original', exact: true }).click();
}

test('draws the sender own markup in a frame that permits no origin and no way out of itself', async ({ page }) => {
    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);

    const frame = page.locator(`iframe[title="${markupFrame}"]`);

    // `allow-scripts` alone, which ADR 0024's fourth question settles: no `allow-same-origin`, so the framed document
    // holds an opaque origin and reaches nothing of the page, and no `allow-popups` and no `allow-top-navigation`, so
    // the only way a link leaves the frame is the report the next test follows. What holds *nothing in the message
    // runs* is then the representation rather than the value asserted here, which is what the test below proves.
    await expect(frame).toHaveAttribute('sandbox', 'allow-scripts');
    await expect(frame).toHaveAttribute('srcdoc', /This week at Example/);
});

test('opens a link the sender wrote out of the application, and navigates nothing', async ({ page }) => {
    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);

    const frame = page.frameLocator(`iframe[title="${markupFrame}"]`);
    const asked: string[] = [];

    // What the new context asked for rather than where it ended up. Nothing in this suite resolves, so the address the
    // reader was taken to settles as a browser error page — the request is the observable act, exactly as it is in the
    // remote-picture check above.
    page.context().on('request', (request) => {
        asked.push(request.url());
    });

    const opened = page.context().waitForEvent('page');

    // The press lands on the `span` inside the anchor rather than on the anchor, which is where a real one lands and
    // is what the script inside the frame reads the tree upwards for.
    await frame.getByText('Read the offers').click();

    // `window.open` with `noopener` gives the new context no opener, so it arrives on the context rather than as this
    // page's popup. That it arrives at all is half the assertion: a frame granting no popup and no top navigation
    // would otherwise have swallowed the press, which is what this surface did before #1797.
    const context = await opened;

    await expect.poll(() => asked).toContain(messages.senderLink);

    // And the application is still where it was. A link that navigated the top-level context would have discarded the
    // session and the message with it, which is the failure `allow-top-navigation` would have bought.
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toBeVisible();
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    await context.close();
});

test('carries nothing that runs, and reaches no host but its own until the reader asks', async ({ page }) => {
    const hosts = new Set<string>();

    page.on('request', (request) => {
        hosts.add(new URL(request.url()).hostname);
    });

    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toBeVisible();

    // Scoped to the surface, because the message it stands over is still drawn underneath and offers the same ask: the
    // window is what the reader is looking at, and it is that one's promise being asserted.
    const surface = page.getByRole('region', { name: 'The original message, as its sender wrote it' });

    // The picture's host is what the anti-tracking promise is measured against: the representation carries no address
    // for it until the reader asks, so a frame that fetched one would be drawing markup this client composed rather
    // than the one it was served. The script host stands beside it as the shape of the other promise rather than as a
    // proof of it — since #1797 the frame permits script, so what keeps a message inert is that the representation
    // holds nothing executable, which is the service's own suite to prove and is why the corpus writes none. The
    // sentence under the frame is the design project's own wording of both, which #1693 brought the surface to.
    await expect(surface.getByText(/scripts and remote content are blocked/)).toBeVisible();
    expect([...hosts]).not.toContain(messages.senderScriptHost);
    expect([...hosts]).not.toContain(messages.senderPictureHost);

    await surface.getByRole('button', { name: 'Load images from the sender' }).click();

    // Asking is what makes the request, on this surface exactly as in the pane. What it restores is addresses and
    // nothing else: the consent widens what may be fetched and never widens what may run.
    await expect(surface.getByText(/their servers can tell it was opened/)).toBeVisible();
    await expect.poll(() => [...hosts]).toContain(messages.senderPictureHost);
    expect([...hosts]).not.toContain(messages.senderScriptHost);
});

// The preview server attaches the policy the build wrote into the bundle, which is the same file a deployment reads and
// attaches — so what is proven is that the screens a person reads mail on run under the policy they are served with.
// A violation is only ever reported to the console, including one inside the sender's frame, which inherits the page's
// policy: a frame script the policy did not admit would fail silently on the screen and loudly here.
test('reads mail, the sender own markup, its pictures, and its links under a policy none of them violates', async ({
    page,
}) => {
    const violations: string[] = [];

    page.on('console', (message) => {
        if (message.text().includes('Content Security Policy')) {
            violations.push(message.text());
        }
    });

    const served = await page.request.get('/');
    const policy = served.headers()['content-security-policy'];

    expect(policy).toContain("frame-ancestors 'none'");

    // The file the build wrote is what a deployment attaches and what it refuses to start without, and nothing else
    // reads it before an image ships — so the header this suite runs under is held to it here.
    const written = await page.request.get('/content-security-policy.txt');

    expect(written.ok()).toBe(true);
    expect((await written.text()).trim()).toBe(policy);

    await openTheFirstMessage(page);
    await showTheSenderMarkup(page);

    const surface = page.getByRole('region', { name: 'The original message, as its sender wrote it' });

    await surface.getByRole('button', { name: 'Load images from the sender' }).click();
    await expect(surface.getByText(/their servers can tell it was opened/)).toBeVisible();

    const opened = page.context().waitForEvent('page');

    await page.frameLocator(`iframe[title="${markupFrame}"]`).getByText('Read the offers').click();
    await (await opened).close();

    expect(violations).toStrictEqual([]);
});

// What only a browser can say about the shape the two surfaces take where somebody does not work in tabs: the design
// project draws each as a window over the message, and a window is the platform's own modal — which jsdom carries the
// element of and none of the behaviour of. So the focus handed back as it closes is asserted here and nowhere else.
test('leaves the markup surface for the message it was opened from, and asks again next time', async ({ page }) => {
    await openTheFirstMessage(page);

    const showTheMarkup = page.getByRole('button', { name: 'Show the original message' });

    await showTheSenderMarkup(page);
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toBeVisible();

    // The message is still where it was rather than replaced, which is what a window over it means.
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    await page.getByRole('button', { name: 'Close the original message' }).click();

    await expect(page.getByRole('heading', messageHeading)).toBeVisible();
    await expect(page.locator(`iframe[title="${markupFrame}"]`)).toHaveCount(0);

    // Closing a modal hands focus back to what opened it, and that is the whole reason every way out of the window
    // leaves through the element rather than by taking the surface off the screen from underneath it.
    await expect(showTheMarkup).toBeFocused();

    // Nothing on either side wrote the answer down, so the control asks again rather than reopening what was shown.
    await page.getByRole('button', { name: 'Show the original message' }).click();

    await expect(page.getByRole('heading', { name: 'Show the original message?' })).toBeVisible();
});

// The embedded view is the one place a message's own markup is drawn inline, and a frame has no height of its own: the
// script the client put in it measures the document and reports, and the pane sizes the frame to the report. A frame
// is a document jsdom does not lay out or run, so both halves — the report arriving and the pane acting on it — are
// asked here, together with the one thing a sandbox without popups would otherwise swallow, a followed link.
test('fits the embedded original to the height its document reports, and opens its links outside the application', async ({
    page,
}) => {
    // Recorded from the first document on, because the report is posted as soon as the frame has parsed.
    await page.addInitScript(
        'window.reportedHeights = []; window.addEventListener("message", (event) => {' +
            ' if (typeof event.data?.height === "number") window.reportedHeights.push(event.data.height); });',
    );

    await openSignedIn(page, '/#/mail');
    await openApplicationSettings(page);
    await page.getByRole('group', { name: 'Message view' }).getByText('Original', { exact: true }).click();
    await expect(page.getByRole('radio', { name: 'Original' })).toBeChecked();
    await page.keyboard.press('Escape');

    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();

    const message = page.getByRole('article', messageRegion);
    const frame = message.locator(`iframe[title="${markupFrame}"]`);

    await expect(
        message.getByText("The sender's HTML in isolation — scripts and remote resources blocked"),
    ).toBeVisible();

    // Read together on every attempt, because the script reports again as the document settles and each report moves
    // the frame: what is asserted is that the frame stands at the last one, plus the two pixels the pane adds.
    await expect
        .poll(async () => {
            const last = (await page.evaluate<number[]>('window.reportedHeights')).at(-1);
            const drawn = await frame.boundingBox();

            return last === undefined || drawn === null ? null : drawn.height - Math.round(last);
        })
        .toBe(2);

    const asked: string[] = [];

    page.context().on('request', (request) => {
        asked.push(request.url());
    });

    const opened = page.context().waitForEvent('page');

    await page.frameLocator(`iframe[title="${markupFrame}"]`).getByText('Read the offers').click();

    const context = await opened;

    await expect.poll(() => asked).toContain(messages.senderLink);
    await expect(frame).toBeVisible();
    expect(page.url()).toMatch(/#\/mail$/u);

    await context.close();
});
