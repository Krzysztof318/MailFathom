// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { folder, holdTheClock, messageRegion, openWithTheChannel, row, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import * as mail from './fixtures/mail';

// What the deployment says over its signal channel while somebody has the client open, read back off every screen the
// statement moves: the list, the folder column, the open message, and the frame's own line about the accounts. A unit
// test of the hook that holds the connection hands one statement to one listener; what it cannot say is whether the
// screens that should have moved did, which is the seam this file stands on.
//
// Every check here holds the clock once the channel stands, so nothing the client runs on its own — the interval that
// reads every screen again among it — can be what moved a screen. What moved it is the statement or nothing.

/** The inbox opened against a deployment serving the channel, with the clock held from the moment it stands. */
async function openTheInboxOverTheChannel(page: Page, deployment: FakeDeployment): Promise<void> {
    await page.clock.install();
    await openWithTheChannel(page, deployment);
    await expect(row(page, 'Message 0')).toBeVisible();
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await holdTheClock(page);
}

/** How many times the frame has read the session, which every refresh of every screen starts with. */
function sessionReads(deployment: FakeDeployment): number {
    return deployment.requests('GET', '/session').length;
}

test('lists mail the deployment says arrived, at the head of the folder, and moves the folder’s unread count', async ({
    page,
    deployment,
}) => {
    await openTheInboxOverTheChannel(page, deployment);

    const refreshed = sessionReads(deployment);

    deployment.deliver();

    await expect(row(page, mail.arrivingRow.subject)).toHaveAccessibleName(/\bUnread\b/u);
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toHaveAccessibleName(
        new RegExp(`${mail.arrivingRow.subject}$`, 'u'),
    );
    await expect(folder(page, 'Inbox 13 unread')).toBeVisible();
    await expect(row(page, 'Message 0')).toBeVisible();

    // Read again because of what was said, and nothing else: no refresh of every screen went out beside it.
    expect(deployment.requests('GET', '/emails').at(-1)?.query.get('cursor') ?? null).toBeNull();
    expect(sessionReads(deployment)).toBe(refreshed);
});

test('redraws the row and the open message from flags the deployment states, and from a change it only names', async ({
    page,
    deployment,
}) => {
    // Granted so the message draws its flag as a control rather than as a mark it may not change, which is the one
    // place the pane says which way the flag stands.
    deployment.grant('mailfathom.mail.flags.write');

    await openTheInboxOverTheChannel(page, deployment);
    await row(page, 'Message 7').click();

    const message = page.getByRole('article', messageRegion);

    await expect(message.getByRole('button', { name: 'Flag', exact: true })).toBeVisible();

    const opened = deployment.requests('GET', '/messages/message-7').length;

    // Stated: the flag is drawn from the statement itself, so neither the row nor the message is read again for it.
    deployment.flagElsewhere('message-7', { flagged: true });

    await expect(row(page, 'Message 7')).toHaveAccessibleName(/\bFlagged\b/u);
    await expect(message.getByRole('button', { name: 'Unflag' })).toBeVisible();
    expect(deployment.requests('GET', '/messages/message-7')).toHaveLength(opened);

    // Only named: the message is read again for it, and what the read answers is what is drawn.
    deployment.changeElsewhere('message-7', { flagged: false });

    await expect(message.getByRole('button', { name: 'Flag', exact: true })).toBeVisible();
    await expect(row(page, 'Message 7')).not.toHaveAccessibleName(/\bFlagged\b/u);
    expect(deployment.requests('GET', '/messages/message-7').length).toBeGreaterThan(opened);
    expect(sessionReads(deployment)).toBe(1);
});

test('draws a folder the deployment says the mailbox gained, where the tree already stands', async ({
    page,
    deployment,
}) => {
    await openTheInboxOverTheChannel(page, deployment);

    deployment.makeFolderElsewhere('Receipts 2026');

    await expect(folder(page, 'Receipts 2026')).toBeVisible();
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    expect(sessionReads(deployment)).toBe(1);
});

test('says an account stopped synchronizing once the deployment says its state moved', async ({ page, deployment }) => {
    await openTheInboxOverTheChannel(page, deployment);
    await expect(page.getByText('Every account is up to date.')).toBeVisible();

    deployment.troubleTheAccounts();

    await expect(page.getByText('Some accounts stopped synchronizing.')).toBeVisible();
    await expect(page.getByText('Every account is up to date.')).toHaveCount(0);
    await expect(row(page, 'Message 0')).toBeVisible();
});
