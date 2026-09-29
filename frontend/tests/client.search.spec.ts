// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { markupOnlySubject } from './fixtures/messages';
import { openSignedIn, phoneWindow, test } from './client.harness';

// A search run from a folder at the width of a phone, where the list and the pane take turns: the results replace the
// folder's list, a result replaces the results, and each way back returns to the screen it left rather than to the
// start — the results from the message, and the folder the search began in from the results.

test('searches from the inbox, opens a result, goes back to the results, and then back to the inbox', async ({
    page,
    deployment,
}) => {
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page, '/#/mail');

    const folder = page.getByRole('listbox', { name: 'Messages' });
    const found = page.getByRole('listbox', { name: 'What this search found' });

    await expect(folder.getByRole('option', { name: /The racking quote/u })).toBeVisible();

    const search = page.getByRole('searchbox', { name: 'Find a message' });

    await search.fill('renewal');
    await search.press('Enter');

    await expect(found.getByRole('option')).toHaveCount(3);
    await expect(folder).toHaveCount(0);

    const [asked] = deployment.requests('GET', '/emails/search');

    expect(asked?.query.get('account')).toBe('work');
    expect(asked?.query.get('folder')).toBe('INBOX');

    await found.getByRole('option', { name: new RegExp(markupOnlySubject, 'u') }).click();

    await expect(page.getByRole('heading', { name: markupOnlySubject, level: 2 })).toBeVisible();
    await expect(found).toHaveCount(0);

    await page.getByRole('button', { name: 'Back to the list' }).click();

    await expect(found.getByRole('option')).toHaveCount(3);
    await expect(search).toHaveValue('renewal');

    await page.getByRole('button', { name: 'Stop searching' }).click();

    await expect(folder.getByRole('option', { name: /The racking quote/u })).toBeVisible();
    await expect(found).toHaveCount(0);
    await expect(search).toHaveValue('');
});
