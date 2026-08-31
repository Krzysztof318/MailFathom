// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';

// What the unit suite structurally cannot answer, asked of the directory of static files `pnpm build` writes, in a
// browser. Everything below would pass or fail identically under jsdom except for the reason it is here: the bundle is
// the built one rather than the source, the document is a real one with a history, and the requests are the requests a
// browser actually issued. `frontend/tests/AGENTS.md` is where that boundary is decided.

// Read the way `src/Client.App/vite.config.ts` reads it, so the assertion below is against the number the build
// substituted rather than against a second copy of it written here.
const declaredVersion = execFileSync(resolve(import.meta.dirname, '../../scripts/read-declared-version.sh'), {
    encoding: 'utf8',
}).trim();

const clientHeading = { name: 'MailFathom', level: 1 } as const;

test('renders the accounts it read, under the version it was built from', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', clientHeading)).toBeVisible();
    await expect(page.getByText(declaredVersion, { exact: true })).toBeVisible();

    const accounts = page.getByRole('listitem');

    await expect(accounts).toHaveCount(3);
    await expect(accounts.filter({ hasText: 'Work' })).toContainText('synchronized');
    await expect(accounts.filter({ hasText: 'Archive' })).toContainText('unreachable, behind');
    await expect(accounts.filter({ hasText: 'Personal' })).toContainText('never synchronized');
});

test('issues every request to the origin it was served from and to no other', async ({ page }) => {
    const origins = new Set<string>();

    page.on('request', (request) => {
        origins.add(new URL(request.url()).origin);
    });

    await page.goto('/');
    await expect(page.getByRole('heading', clientHeading)).toBeVisible();

    // A client of one person's own mail reaches the deployment serving it and nothing else, so a font, an analytics
    // beacon, or a stray CDN reference arriving in the bundle is a privacy defect rather than a slow page. Only a
    // browser can answer this: jsdom loads no subresource, and the source says nothing about what a build inlined.
    expect([...origins]).toStrictEqual([new URL(page.url()).origin]);
});

test('renders again when the browser goes back to it', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', clientHeading)).toBeVisible();

    await page.goto('about:blank');
    await page.goBack();

    // The application remounts and reads its accounts again, rather than coming back as the empty document a bundle
    // restored from the back-forward cache without rerunning would leave. The client carries no router yet, so this is
    // the whole of the navigation it has; an in-application back gesture is checked here on the day one exists.
    await expect(page.getByRole('heading', clientHeading)).toBeVisible();
    await expect(page.getByRole('listitem')).toHaveCount(3);
});

test('opens again in the language that was chosen, after the page is loaded afresh', async ({ page }) => {
    await page.goto('/');

    const accountsAreRefreshing = page.getByText('This deployment refreshes the local copy of these accounts.');
    await expect(accountsAreRefreshing).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    await page.getByRole('combobox', { name: 'Language' }).selectOption('pl');
    await page.reload();

    // The whole of what the unit suite cannot answer about this: it proves the choice was written, and only a real
    // document loaded a second time proves a later run reads it back. The assertion is the English sentence being gone
    // and the document declaring the other language, rather than the Polish sentence being present — the catalogue is
    // the one file in this repository deliberately not in English, and a second copy of its wording here would be a
    // string to keep in step with it and a word for the spell check to object to.
    await expect(accountsAreRefreshing).toHaveCount(0);
    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
    await expect(page.getByRole('listitem')).toHaveCount(3);
});
