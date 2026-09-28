// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { messageRegion, openTheFirstMessage, test } from './client.harness';
import * as deployment from './fixtures/deployment';
import * as messages from './fixtures/messages';

// A file a message carries: described before it is fetched, opened inside the client, and fetched under the session
// the client holds.

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
    await expect(page.getByText('File downloaded')).toBeVisible();
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
