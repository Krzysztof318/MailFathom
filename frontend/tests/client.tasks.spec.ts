// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// A task written down, finished, shown again among the finished, and deleted, with the list's headings and the day's
// capacity beside them read after every act. The list reads itself again after a write rather than correcting what it
// holds, and the capacity counts off that same read — so the two agree only where both drew the deployment's answer.

// The corpus deployment states Greenwich as the reader's zone, and the reader sits in it, so the day the due-day field
// is filled with is the day the list files under *Today*.
test.use({ timezoneId: 'UTC' });

const written = 'Book the carrier for Friday';

test('moves a task it wrote, finished, and deleted between the headings, and the day beside them agrees', async ({
    page,
    deployment,
}) => {
    const today = new Date().toISOString().slice(0, 10);

    await openSignedIn(page, '/#/tasks');

    const dueToday = page.getByRole('region', { name: 'Today' });
    const capacity = page.getByRole('region', { name: 'Day capacity' });

    await expect(dueToday.getByRole('checkbox', { name: 'Mark Answer the Nordwind tender as done' })).toBeVisible();
    await expect(capacity).toContainText('One task is due today.');

    await page.getByRole('button', { name: 'New task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await writing.getByLabel('What has to be done').fill(written);
    await writing.getByLabel('Due day').fill(today);
    await writing.getByRole('button', { name: 'Write it down' }).click();

    await expect(dueToday.getByRole('checkbox', { name: `Mark ${written} as done` })).toBeVisible();
    await expect(capacity).toContainText('2 tasks are due today.');

    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        { title: written, dueOn: today, reminders: [], dueDayOffsetMinutes: null, sourceMessageId: null },
    ]);

    // Finished work leaves the list while done tasks are hidden, and stops counting against the day.
    await dueToday.getByRole('checkbox', { name: `Mark ${written} as done` }).check();

    await expect(page.getByRole('status').filter({ hasText: 'Marked as done.' })).toBeVisible();
    await expect(capacity).toContainText('One task is due today.');
    await expect(page.getByRole('checkbox', { name: new RegExp(`^Mark ${written} as`, 'u') })).toHaveCount(0);

    const [completion] = deployment.issued.filter(
        ({ method, route }) => method === 'POST' && route.endsWith('/completion'),
    );

    expect(completion?.body).toStrictEqual({ completed: true });

    // Shown again under the day it is due on, marked as done, and still not counted.
    await page.getByRole('button', { name: 'Show done' }).click();

    await expect(dueToday.getByRole('checkbox', { name: `Mark ${written} as not done` })).toBeChecked();
    await expect(capacity).toContainText('One task is due today.');

    await dueToday
        .getByRole('listitem')
        .filter({ has: page.getByRole('checkbox', { name: `Mark ${written} as not done` }) })
        .click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete task' }).click();
    await page.getByRole('dialog', { name: 'Delete this task?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Deleted.' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: new RegExp(`^Mark ${written} as`, 'u') })).toHaveCount(0);
    await expect(dueToday.getByRole('checkbox', { name: 'Mark Answer the Nordwind tender as done' })).toBeVisible();
    await expect(capacity).toContainText('One task is due today.');

    // The task deleted is the one finished, which is the one the write created.
    expect(deployment.issued.filter(({ method }) => method === 'DELETE').map(({ route }) => route)).toStrictEqual([
        completion?.route.replace(/\/completion$/u, ''),
    ]);
});
