// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { bodiesOf, folder, notice, openTheArchive, openTheInbox, refreshInterval, row, test } from './client.harness';

// Picking several messages out and acting on all of them: the list that makes the selection, the bar that stands over
// it and acts, and every pane the act moves. What each proves is that an act asked of a selection is never quietly an
// act on one of its messages — which is a claim about the seam between the list, the bar, and the folders, and nothing a
// test of the bar with a selection handed to it can see.

function selectionBar(page: Page) {
    return page.getByRole('toolbar', { name: 'Actions on the messages selected' });
}

/** Adds rows to the selection one at a time, the way a modifier under a pointer does. */
async function pickOut(page: Page, ...subjects: readonly string[]): Promise<void> {
    for (const subject of subjects) {
        await row(page, subject).click({ modifiers: ['ControlOrMeta'] });
    }
}

test('marks, flags, and archives every selected message, and the list, the counts, and a reload agree', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();

    const bar = selectionBar(page);

    // Three unread messages, marked read together; then two of them marked unread again and flagged.
    await pickOut(page, 'Message 6', 'Message 12', 'Message 15');
    await expect(bar.getByRole('status')).toHaveText('3 selected');
    await bar.getByRole('button', { name: 'Mark read' }).click();

    for (const subject of ['Message 6', 'Message 12', 'Message 15']) {
        await expect(row(page, subject)).not.toHaveAccessibleName(/\bUnread\b/u);
    }

    // The count is the deployment's, and this one serves no signal channel, so it moves when the client reads
    // everything again on its own.
    await page.clock.runFor(refreshInterval);
    await expect(folder(page, 'Inbox 9 unread')).toBeVisible();

    await pickOut(page, 'Message 6', 'Message 12');
    await bar.getByRole('button', { name: 'Mark unread' }).click();
    await expect(bar).toHaveCount(0);

    await pickOut(page, 'Message 6', 'Message 12');
    await bar.getByRole('button', { name: 'Flag' }).click();

    for (const subject of ['Message 6', 'Message 12']) {
        await expect(row(page, subject)).toHaveAccessibleName(/\bUnread\b/u);
        await expect(row(page, subject)).toHaveAccessibleName(/\bFlagged\b/u);
    }

    await page.clock.runFor(refreshInterval);
    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();

    await pickOut(page, 'Message 7', 'Message 8');
    await bar.getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Archived')).toContainText('2 messages');
    await expect(row(page, 'Message 7')).toHaveCount(0);
    await expect(row(page, 'Message 8')).toHaveCount(0);

    expect(bodiesOf(deployment, '/mutations/flags')).toStrictEqual([
        {
            changes: [
                { storedEmailId: 'message-6', flags: { seen: true } },
                { storedEmailId: 'message-12', flags: { seen: true } },
                { storedEmailId: 'message-15', flags: { seen: true } },
            ],
        },
        {
            changes: [
                { storedEmailId: 'message-6', flags: { seen: false } },
                { storedEmailId: 'message-12', flags: { seen: false } },
            ],
        },
        {
            changes: [
                { storedEmailId: 'message-6', flags: { flagged: true } },
                { storedEmailId: 'message-12', flags: { flagged: true } },
            ],
        },
    ]);
    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        {
            moves: [
                { storedEmailId: 'message-7', destinationFolder: 'FILED' },
                { storedEmailId: 'message-8', destinationFolder: 'FILED' },
            ],
        },
    ]);

    await openTheArchive(page);
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(2);

    await page.reload();

    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();
    await folder(page, 'Inbox 11 unread').click();
    await expect(row(page, 'Message 15')).not.toHaveAccessibleName(/\bUnread\b/u);

    for (const subject of ['Message 6', 'Message 12']) {
        await expect(row(page, subject)).toHaveAccessibleName(/\bUnread\b/u);
        await expect(row(page, subject)).toHaveAccessibleName(/\bFlagged\b/u);
    }

    await expect(row(page, 'Message 7')).toHaveCount(0);
});

test('files every selected message into the folder chosen, where Select all takes in both and Clear lets them go', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    const bar = selectionBar(page);

    await pickOut(page, 'Message 4', 'Message 7');
    await bar.getByRole('button', { name: 'Move' }).click();
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();

    await expect(notice(page, 'Filed in “Archive / 2024”')).toContainText('2 messages');
    await expect(row(page, 'Message 4')).toHaveCount(0);
    await expect(row(page, 'Message 7')).toHaveCount(0);

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        {
            moves: [
                { storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' },
                { storedEmailId: 'message-7', destinationFolder: 'ARCHIVE/2024' },
            ],
        },
    ]);

    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await folder(page, '2024').click();

    const listed = page.getByRole('listbox', { name: 'Messages' }).getByRole('option');

    await expect(listed).toHaveCount(2);

    // Everything is what the folder holds rather than what the bar was told about: one picked out, and both taken in.
    await pickOut(page, 'Message 4');
    await expect(bar.getByRole('status')).toHaveText('1 selected');
    await bar.getByRole('button', { name: 'Select all' }).click();

    await expect(bar.getByRole('status')).toHaveText('2 selected');
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option', { selected: true })).toHaveCount(
        2,
    );

    // Clearing takes the bar away and gives the strip back the toolbar, with nothing in the list still picked out.
    await bar.getByRole('button', { name: 'Clear the selection' }).click();

    await expect(bar).toHaveCount(0);
    await expect(page.getByRole('toolbar', { name: 'Mail actions' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option', { selected: true })).toHaveCount(
        0,
    );
    await expect(listed).toHaveCount(2);
});

test('extends a selection with Shift, by pointer and by arrow key, over every row between the anchor and the row reached', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    const bar = selectionBar(page);

    // A plain press opens and anchors; Shift on a row two further down takes in everything between the two.
    await row(page, 'Message 6').click();
    await row(page, 'Message 8').click({ modifiers: ['Shift'] });

    await expect(bar.getByRole('status')).toHaveText('3 selected');
    await bar.getByRole('button', { name: 'Flag' }).click();

    for (const subject of ['Message 6', 'Message 7', 'Message 8']) {
        await expect(row(page, subject)).toHaveAccessibleName(/\bFlagged\b/u);
    }

    await expect(row(page, 'Message 10')).not.toHaveAccessibleName(/\bFlagged\b/u);

    expect(bodiesOf(deployment, '/mutations/flags')).toContainEqual({
        changes: [6, 7, 8].map((at) => ({ storedEmailId: `message-${String(at)}`, flags: { flagged: true } })),
    });

    // From the keyboard. Letting the selection go hands focus back to the row the reader was on, so the arrow keys walk
    // on from there: two rows down to anchor, then Shift with the arrow twice.
    await expect(row(page, 'Message 8')).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowDown');
    await expect(row(page, 'Message 10')).toBeFocused();

    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('Shift+ArrowDown');

    await expect(bar.getByRole('status')).toHaveText('3 selected');
    await bar.getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Archived')).toContainText('3 messages');

    for (const subject of ['Message 10', 'Message 11', 'Message 12']) {
        await expect(row(page, subject)).toHaveCount(0);
    }

    await expect(row(page, 'Message 14')).toBeVisible();

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        {
            moves: [
                { storedEmailId: 'message-10', destinationFolder: 'FILED' },
                { storedEmailId: 'message-11', destinationFolder: 'FILED' },
                { storedEmailId: 'message-12', destinationFolder: 'FILED' },
            ],
        },
    ]);
});
