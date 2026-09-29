// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { folder, holdTheClock, messageRegion, openSignedIn, openWithTheChannel, row, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';

// The client losing its way to the deployment and finding it again: the machine going offline, and the deployment
// itself going quiet. Each is read off every screen it reaches — the frame's line about the connection, the list, the
// open message, and a search — and off the requests the page issued, because what the client promises about both is
// that nothing already on the screen is thrown away and that it comes back by itself.

/** The frame's own line saying the machine is offline, which stands whatever each screen below it holds. */
function offlineLine(page: Page) {
    return page
        .getByRole('status')
        .filter({ hasText: 'This machine is offline. The client reconnects on its own when the network comes back.' });
}

/** The longest wait before each automatic attempt: `reconnectionDelay` in `Client.Backend`'s `reconnection.ts`. */
const longestWaitBefore = [1_250, 2_500, 5_000, 10_000, 20_000] as const;

/** The frame's line saying which of the automatic attempts it is on. */
function attempting(page: Page, attempt: number) {
    return page.getByText(`Your deployment did not answer. Trying again — attempt ${String(attempt)} of 5.`);
}

function sessionReads(deployment: FakeDeployment): number {
    return deployment.requests('GET', '/session').length;
}

test('says it is offline while keeping the list and the open message, and reads both again once the network is back', async ({
    page,
    context,
    deployment,
}) => {
    await openWithTheChannel(page, deployment);
    await row(page, 'Message 7').click();

    const message = page.getByRole('article', messageRegion);

    await expect(message).toBeVisible();

    await context.setOffline(true);

    await expect(offlineLine(page)).toBeVisible();
    await expect(row(page, 'Message 0')).toBeVisible();
    await expect(row(page, 'Message 7')).toBeVisible();
    await expect(message).toBeVisible();
    await expect.poll(() => deployment.channelsOpen()).toBe(0);

    const listReads = deployment.requests('GET', '/emails').length;
    const messageReads = deployment.requests('GET', '/messages/message-7').length;

    await context.setOffline(false);

    await expect(offlineLine(page)).toHaveCount(0);
    await expect.poll(() => deployment.channelsOpen()).toBe(1);
    await expect.poll(() => deployment.requests('GET', '/emails').length).toBeGreaterThan(listReads);
    await expect.poll(() => deployment.requests('GET', '/messages/message-7').length).toBeGreaterThan(messageReads);
    await expect(row(page, 'Message 0')).toBeVisible();
    await expect(message).toBeVisible();
});

test('keeps what a search found while offline, and asks it again once the network is back', async ({
    page,
    context,
    deployment,
}) => {
    await openWithTheChannel(page, deployment);

    const search = page.getByRole('searchbox', { name: 'Find a message' });
    const found = page.getByRole('listbox', { name: 'What this search found' });

    await search.fill('renewal');
    await search.press('Enter');
    await expect(found.getByRole('option')).toHaveCount(3);

    await context.setOffline(true);

    await expect(offlineLine(page)).toBeVisible();
    await expect(found.getByRole('option')).toHaveCount(3);

    const searches = deployment.requests('GET', '/emails/search').length;

    await context.setOffline(false);

    await expect(offlineLine(page)).toHaveCount(0);
    await expect.poll(() => deployment.requests('GET', '/emails/search').length).toBeGreaterThan(searches);
    await expect(found.getByRole('option')).toHaveCount(3);
    await expect(search).toHaveValue('renewal');
});

/** The client opened on a deployment that has stopped answering, with the clock held once it has said so. */
async function reopenOnADeploymentThatStopped(page: Page, deployment: FakeDeployment): Promise<void> {
    await page.clock.install();
    await openSignedIn(page, '/#/mail');
    await expect(row(page, 'Message 0')).toBeVisible();

    deployment.stopAnswering();
    await page.reload();

    await expect(attempting(page, 1)).toBeVisible();
    await holdTheClock(page);
}

test('reaches for a deployment that stopped answering until its attempts are spent, and restores every screen once asked again', async ({
    page,
    deployment,
}) => {
    await reopenOnADeploymentThatStopped(page, deployment);

    for (const [made, wait] of longestWaitBefore.entries()) {
        const reached = sessionReads(deployment);

        await page.clock.runFor(wait);
        await expect.poll(() => sessionReads(deployment)).toBe(reached + 1);

        if (made + 2 <= longestWaitBefore.length) {
            await expect(attempting(page, made + 2)).toBeVisible();
        }
    }

    await expect(page.getByText('Your deployment has not answered after 5 attempts.')).toBeVisible();
    await expect(row(page, 'Message 0')).toHaveCount(0);

    // Spent: however long nobody asks, the client reaches for it no more.
    const spent = sessionReads(deployment);

    await page.clock.runFor(60_000);
    expect(sessionReads(deployment)).toBe(spent);

    deployment.answerAgain();
    await page.getByRole('button', { name: 'Try again' }).click();

    await expect(row(page, 'Message 0')).toBeVisible();
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await expect(page.getByText('Every account is up to date.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Try again' })).toHaveCount(0);
    expect(sessionReads(deployment)).toBe(spent + 1);
});

test('restores every screen on its own once a deployment that stopped answers again before the attempts are spent', async ({
    page,
    deployment,
}) => {
    await reopenOnADeploymentThatStopped(page, deployment);

    deployment.answerAgain();
    await page.clock.runFor(longestWaitBefore[0]);

    await expect(row(page, 'Message 0')).toBeVisible();
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await expect(attempting(page, 1)).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Try again' })).toHaveCount(0);
});
