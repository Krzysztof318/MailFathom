// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { declaredVersion, openSettings, openSignedIn, test } from './client.harness';

// Moving between spaces the way a document with a history does, and finding each one — and the folder tree — where it
// was left after a reload.

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

test('keeps the folder tree as it was left, across a reload', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });

    // The level the corpus nests a folder under, which arrives shut: a mailbox filed three levels deep would
    // otherwise open as everything it has ever held. The corpus holds one mailbox, which is a user the tree offers
    // no row spanning every mailbox to, so what unfolds here is a level of a folder's own path.
    const above = tree.getByRole('treeitem', { name: /^Archive/ });

    await expect(above).toHaveAttribute('aria-expanded', 'false');

    await above.click();
    await page.keyboard.press('ArrowRight');
    await expect(above).toHaveAttribute('aria-expanded', 'true');

    const nested = tree.getByRole('treeitem', { name: /^2024/ });

    await nested.click();
    await expect(nested).toHaveAttribute('aria-selected', 'true');

    await page.reload();

    // A reload is a cold start, so what somebody was looking at is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held: a remount in jsdom re-reads the
    // same process's storage, and what this asks is that a browser wrote it. What was written is the move away from
    // the fold the row opens at rather than the fold itself, so a level opened by hand comes back open.
    await expect(tree.getByRole('treeitem', { name: /^Archive/ })).toHaveAttribute('aria-expanded', 'true');

    // The scope outlives the reload beside the fold, which is the other half of what was left here.
    await expect(tree.getByRole('treeitem', { name: /^2024/ })).toHaveAttribute('aria-selected', 'true');
});
