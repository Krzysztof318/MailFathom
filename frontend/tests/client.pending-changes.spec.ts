// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { bodiesOf, folder, fromTheMenu, openTheInbox, row, test } from './client.harness';

// A change the deployment wrote down and the mailbox never took, followed from the act that asked for it to the
// question it turns into and through each of its two answers. What is read back is every place the change was drawn —
// the row, the folder's count, the line saying how many changes are still on their way — and what a reload draws,
// because a change let go of that went on being claimed anywhere is exactly the silent loss the question exists for.

/** How often the client asks where its changes stand: `followedChangeInterval` in `pendingChanges/usePendingChanges.ts`. */
const followedChangeInterval = 5_000;

const stoppedTrying = 'Your mailbox would not take this change, and MailFathom has stopped trying to make it.';

/** The question one change turned into, found by the act it names and the two ways out it offers. */
function question(page: Page, act: string): Locator {
    return page
        .getByRole('listitem')
        .filter({ hasText: act })
        .filter({ has: page.getByRole('button', { name: 'Let it go' }) });
}

/** Lets the client ask where its changes stand, which is the one way it learns the mailbox would not take one. */
async function followTheChanges(page: Page): Promise<void> {
    await page.clock.runFor(followedChangeInterval);
}

test('lists a read mark the mailbox would not take, and once it is let go the row, the count, and a reload agree it never happened', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    deployment.refuseNextChange();
    await fromTheMenu(page, 'Message 12', 'Mark as read');

    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(page.getByText('One change has not reached your mailbox yet.')).toBeVisible();

    await followTheChanges(page);

    const asked = question(page, 'Marked read');

    await expect(asked).toContainText(stoppedTrying);
    await expect(page.getByText('One change has not reached your mailbox yet.')).toHaveCount(0);

    await asked.getByRole('button', { name: 'Let it go' }).click();

    await expect(asked).toHaveCount(0);
    await expect(row(page, 'Message 12')).toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    expect(bodiesOf(deployment, '/mutations/flags')).toStrictEqual([
        { changes: [{ storedEmailId: 'message-12', flags: { seen: true } }] },
    ]);

    await page.reload();

    await expect(row(page, 'Message 12')).toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await expect(question(page, 'Marked read')).toHaveCount(0);
});

test('asks for a read mark the mailbox would not take again, and the row, the count, and a reload agree it landed', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    deployment.refuseNextChange();
    await fromTheMenu(page, 'Message 12', 'Mark as read');
    await followTheChanges(page);

    const asked = question(page, 'Marked read');

    await asked.getByRole('button', { name: 'Ask again' }).click();

    await expect(asked).toHaveCount(0);
    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(page.getByText('One change has not reached your mailbox yet.')).toBeVisible();
    expect(bodiesOf(deployment, '/mutations/flags')).toStrictEqual([
        { changes: [{ storedEmailId: 'message-12', flags: { seen: true } }] },
        { changes: [{ storedEmailId: 'message-12', flags: { seen: true } }] },
    ]);

    await followTheChanges(page);

    await expect(page.getByText('One change has not reached your mailbox yet.')).toHaveCount(0);

    await page.reload();

    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();
});

test('lists a filing the mailbox would not take, and once it is let go the message is back where it was, after a reload too', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    deployment.refuseNextChange();
    await fromTheMenu(page, 'Message 4', 'Move…');
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();

    await expect(row(page, 'Message 4')).toHaveCount(0);

    await followTheChanges(page);

    const asked = question(page, 'Filed in another folder');

    await expect(asked).toContainText(stoppedTrying);

    await asked.getByRole('button', { name: 'Let it go' }).click();

    await expect(asked).toHaveCount(0);
    await expect(row(page, 'Message 4')).toBeVisible();

    await page.reload();

    await expect(row(page, 'Message 4')).toBeVisible();
    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await folder(page, '2024').click();
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();
});
