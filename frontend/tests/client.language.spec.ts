// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Browser, type Page } from '@playwright/test';

import { openApplicationSettings, openSignedIn, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';

// Which language the client opens in: what a real head's preference list decides with nothing configured, and a
// choice that outranks it across a reload.

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

// What language the client opens in when nobody has chosen one. Only a browser can answer it: `navigator.languages` is
// the machine's own preference list, jsdom reports whatever the process was started with, and what this asks is that a
// real head reads a real preference. Each case gets a context of its own because the preference is a property of the
// browser context rather than of the page.
async function openedPreferring(
    browser: Browser,
    deployment: FakeDeployment,
    languages: readonly string[],
): Promise<Page> {
    const context = await browser.newContext({ locale: languages[0] ?? 'en-US' });

    await deployment.serve(context);

    // `locale` sets the first entry alone. The rule being proven walks the whole list, so the list itself is what is
    // put in front of the client — and an empty one is the head that reports no preference at all.
    await context.addInitScript(
        `Object.defineProperty(navigator, 'languages', { get: () => ${JSON.stringify(languages)} })`,
    );

    const page = await context.newPage();
    await page.goto('/');

    return page;
}

test('opens in Polish on a machine that prefers Polish, with nothing configured', async ({ browser, deployment }) => {
    const page = await openedPreferring(browser, deployment, ['pl-PL', 'en-US']);

    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
    await expect(page.getByRole('button', { name: 'Connect' })).toHaveCount(0);
});

test('opens in English on a machine preferring a language the client does not carry', async ({
    browser,
    deployment,
}) => {
    const page = await openedPreferring(browser, deployment, ['de-DE', 'fr-FR']);

    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('button', { name: 'Connect' })).toBeVisible();
});

test('opens in English on a head that reports no language preference at all', async ({ browser, deployment }) => {
    const page = await openedPreferring(browser, deployment, []);

    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
});

test('lets a choice outrank the machine preference, and keeps it across a reload', async ({ browser, deployment }) => {
    const page = await openedPreferring(browser, deployment, ['pl-PL']);
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
