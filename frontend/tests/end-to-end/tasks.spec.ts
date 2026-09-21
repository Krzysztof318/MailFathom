// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test, type Locator, type Page } from '@playwright/test';
import { openSpace } from './deployment';
import { anEventSeededToday, seededDay, tasksUnderEachHeading, theOtherTaskDueToday } from './seed';

// The Tasks space against a real deployment: the sample list the run wrote drawn under the three headings the design
// draws, the day beside it read out of the same calendar the Calendar space reads, and one task written down, marked
// done and deleted through the screen.
//
// Naming what the run seeded is allowed here for the reason `calendar.spec.ts` gives: this list was stated by
// `seed.ts` rather than generated, so a spec reading the same declarations asserts that what the deployment took is
// what the client draws.

/** Reaches the Tasks space and waits for the seeded list to have been drawn. */
async function openTheList(page: Page): Promise<void> {
    await openSpace(page, 'Tasks');
    await expect(rowFor(page, theOtherTaskDueToday.title)).toBeVisible();
}

/** One row of the list, which is a plain item carrying the task's own controls rather than an option. */
function rowFor(page: Page, title: string): Locator {
    return page.getByRole('listitem').filter({ hasText: title });
}

test('draws the list under the three headings, with each task under the day it is due', async ({ page }) => {
    await openTheList(page);

    // The headings are a rendering decision rather than the deployment's — the surface answers one ordered walk — so
    // what is asserted is that a task due today, one due inside the week, and one nobody dated each stand under the
    // heading the design puts them under.
    for (const [heading, task] of Object.entries(tasksUnderEachHeading)) {
        await expect(page.getByRole('region', { name: heading, exact: true }).getByText(task.title)).toBeVisible();
    }
});

test('draws the day beside the list out of the calendar the deployment holds', async ({ page }) => {
    await openTheList(page);

    // The same event the Calendar space draws, reached from a different screen and a different route. That is the one
    // thing this check has that a component test of the panel does not: two screens agreeing about one deployment.
    await expect(
        page.getByRole('region', { name: "Today's calendar" }).getByText(anEventSeededToday.title),
    ).toBeVisible();
});

test('writes a task down, marks it done, and deletes it', async ({ page }) => {
    const written = 'Collect the archive keys from reception';

    await openTheList(page);

    await page.getByRole('button', { name: 'New task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await expect(writing).toBeVisible();

    await writing.getByLabel('What has to be done', { exact: true }).fill(written);
    await writing.getByLabel('Due day', { exact: true }).fill(seededDay(0));
    await writing.getByRole('button', { name: 'Write it down' }).click();

    // Due today, so it stands under *Today* beside what the run seeded — which is the grouping asserted above, read
    // here against a task the deployment has only just been told about.
    await expect(page.getByRole('region', { name: 'Today', exact: true }).getByText(written)).toBeVisible();

    await page.getByRole('checkbox', { name: `Mark ${written} as done` }).check();

    // Done work leaves the list rather than sitting in it struck through, which is what the switch below is for.
    await expect(rowFor(page, written)).toHaveCount(0);

    const showDone = page.getByRole('switch', { name: 'Show done' });

    await showDone.click();

    await expect(page.getByRole('checkbox', { name: `Mark ${written} as not done` })).toBeChecked();

    // Picked out with a modifier held, which is the one gesture that opens the acts over a row: a task has nowhere to
    // be opened to, so a plain press on one does nothing at all.
    await rowFor(page, written).click({ modifiers: ['ControlOrMeta'] });

    await page
        .getByRole('toolbar', { name: 'Acts over the tasks you picked out' })
        .getByRole('button', { name: 'Delete' })
        .click();

    await page.getByRole('dialog', { name: 'Delete this task?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(rowFor(page, written)).toHaveCount(0);

    await showDone.click();

    // The seeded list is as it was, which is what lets the files in this directory run in any order.
    await expect(rowFor(page, theOtherTaskDueToday.title)).toBeVisible();
});
