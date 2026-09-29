// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { holdTheClock, messageRegion, openSignedIn, test } from './client.harness';
import * as notifications from './fixtures/notifications';

// The notification centre as a person uses it: a notification arriving while they work, opening one to what it is
// about, marking, and deleting — each asserted by what the bell, the centre, and the screen it pointed at hold
// afterwards, and by what the page asked the deployment to do. What one row, the bell, or the panel draws in answer to
// a value handed to it is the unit suite's, and none of it is repeated here.

/** How long the client waits between two reads of the unread count, which is what an arrival waits on. */
const unreadCountInterval = 60_000;

function bell(page: Page): Locator {
    return page.getByRole('button', { name: /^(\d+ unread notifications?|Notifications)$/u });
}

function centre(page: Page): Locator {
    return page.getByRole('dialog', { name: 'Notifications' });
}

/** One row of the open centre, found by the headline a person reads on it. */
function row(page: Page, headline: string): Locator {
    return centre(page).getByRole('listitem').filter({ hasText: headline });
}

async function openCentre(page: Page): Promise<void> {
    await bell(page).click();
    await expect(centre(page)).toBeVisible();
}

test('draws a notification that arrives while somebody works, and opens the message it is about from its notice', async ({
    page,
    deployment,
}) => {
    await page.clock.install();
    await openSignedIn(page);
    await expect(bell(page)).toHaveAccessibleName('2 unread notifications');

    // Nothing is pushed: the deployment holds it, and the client finds it on the next read of the count. The clock is
    // held from here, so the notice it raises stands until the journey has pressed it.
    deployment.raiseNotification();
    await holdTheClock(page);
    await page.clock.runFor(unreadCountInterval);

    await expect(bell(page)).toHaveAccessibleName('3 unread notifications');

    const notice = page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({
        hasText: notifications.arrivingNotification.body,
    });

    await notice.getByRole('button', { name: 'Show' }).click();

    // Opened to what it is about, which is a message — and a message is read in the Mail space, whichever space the
    // notice was raised over.
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();
    await expect(page.getByRole('article', messageRegion)).toBeVisible();
    await expect(bell(page)).toHaveAccessibleName('2 unread notifications');
    await expect
        .poll(() => deployment.requests('POST', `/notifications/${notifications.arrivingNotification.id}/read-state`))
        .toMatchObject([{ body: { read: true } }]);

    await openCentre(page);
    await expect(row(page, notifications.arrivingNotification.body)).not.toContainText('Unread');
});

test('goes to the space a notification names, and the centre and the bell agree it was read, after a reload too', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await centre(page)
        .getByRole('button', { name: /4 new messages arrived/u })
        .click();

    await expect(centre(page)).toBeHidden();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();
    await expect(bell(page)).toHaveAccessibleName('1 unread notification');
    await expect
        .poll(() => deployment.requests('POST', `/notifications/${notifications.notificationId}/read-state`))
        .toHaveLength(1);

    await page.reload();

    await expect(bell(page)).toHaveAccessibleName('1 unread notification');
    await openCentre(page);
    await expect(row(page, '4 new messages arrived.')).not.toContainText('Unread');
});

test('marks one notification and then the rest read, and the bell holds nothing unread across a reload', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await row(page, 'This account needs signing in again').getByRole('button', { name: 'Mark as read' }).click();

    await expect(bell(page)).toHaveAccessibleName('1 unread notification');

    // The unread tab is read at the moment it is drawn, so the row marked a moment ago is not on it.
    await centre(page).getByText('Unread · 1').click();
    await expect(centre(page).getByRole('listitem')).toHaveCount(1);

    await centre(page).getByRole('button', { name: 'Mark all' }).click();

    await expect(bell(page)).toHaveAccessibleName('Notifications');
    await expect.poll(() => deployment.requests('POST', '/notifications/read')).toHaveLength(1);

    await page.reload();

    await expect(bell(page)).toHaveAccessibleName('Notifications');
    await openCentre(page);
    await expect(centre(page).getByRole('listitem')).toHaveCount(notifications.notificationPage.notifications.length);
    await expect(centre(page).getByRole('button', { name: 'Mark as read' })).toHaveCount(0);
});

test('deletes a notification from its own menu, and it stays gone from the centre and the bell after a reload', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await centre(page)
        .getByRole('button', { name: /4 new messages arrived/u })
        .click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete notification' }).click();

    const question = page.getByRole('dialog', { name: 'Delete notification?' });

    await question.getByRole('button', { name: 'Delete' }).click();

    await expect(row(page, '4 new messages arrived.')).toHaveCount(0);
    await expect(bell(page)).toHaveAccessibleName('1 unread notification');
    await expect
        .poll(() => deployment.requests('POST', '/notifications/deletions'))
        .toMatchObject([{ body: { notificationIds: [notifications.notificationId] } }]);

    await page.reload();

    await expect(bell(page)).toHaveAccessibleName('1 unread notification');
    await openCentre(page);
    await expect(centre(page).getByRole('listitem')).toHaveCount(
        notifications.notificationPage.notifications.length - 1,
    );
    await expect(row(page, '4 new messages arrived.')).toHaveCount(0);
});
