// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test, type Locator, type Page } from '@playwright/test';
import { goToSpace, openSpace } from './deployment';
import { anEventSeededToday, eventsSeededToday, seededDay, theDayLongEvent } from './seed';

// The Calendar space against a MailFathom that was stood up the way an operator stands one up: the sample calendar the
// run wrote drawn in each of the four views, a day reached by walking forward to it, and one event written, amended
// and taken off again through the screen.
//
// **What it may name and the mail suite may not.** `mail.spec.ts` writes down no subject, because the mail it reads
// was generated and a spec restating one of its values would be asserting against the archive rather than against the
// service. This calendar was not generated: `seed.ts` states it, the run writes exactly it, and this spec reads the
// same declarations — so naming an event here asserts that what the deployment took is what the client draws, which
// is the claim.

/** Reaches the Calendar space and waits for the seeded calendar to have been drawn. */
async function openTheCalendar(page: Page): Promise<void> {
    await openSpace(page, 'Calendar');
    await expect(entryFor(page, anEventSeededToday.title)).toBeVisible();
}

/** One entry wherever a view draws one, which is the same role and the same name in all four. */
function entryFor(page: Page, title: string): Locator {
    return page.getByRole('option', { name: title });
}

/** Puts the space into one of its four views, by the choice a person makes rather than by an address. */
async function drawnAs(page: Page, view: string): Promise<void> {
    await page.getByRole('group', { name: 'Which view' }).getByText(view, { exact: true }).click();
}

test('draws the calendar the deployment holds, in the week it opens on', async ({ page }) => {
    await openTheCalendar(page);

    for (const event of eventsSeededToday) {
        await expect(entryFor(page, event.title)).toBeVisible();
    }
});

test('keeps the day it is standing on in sight through each of the four views', async ({ page }) => {
    await openTheCalendar(page);

    // Today and nothing else, because that is the one day every view has in it: which week a day three days out falls
    // in, and whether a day six days out is still this month, are both properties of the date the run happens on, and
    // a spec asserting either would pass or fail by the calendar rather than by the client.
    for (const view of ['Day', 'Month', 'Agenda', 'Week']) {
        await drawnAs(page, view);

        for (const event of eventsSeededToday) {
            await expect(entryFor(page, event.title)).toBeVisible();
        }
    }
});

test('walks forward to a day stated as a day rather than as a clock time, and comes back', async ({ page }) => {
    await openTheCalendar(page);
    await drawnAs(page, 'Day');

    // One day at a time from today, which is the walk a person makes and which lands on the seeded day whichever
    // weekday the run happens on — a week or a month would not.
    for (let walked = 0; walked < theDayLongEvent.dayOffset; walked++) {
        await page.getByRole('button', { name: 'Later' }).click();
    }

    const dayLong = entryFor(page, theDayLongEvent.title);

    await expect(dayLong).toBeVisible();

    // The event states no hours, so the entry carries no time beside its title — which is the whole of the difference
    // a day-long event makes to what is drawn.
    await expect(dayLong).toHaveText(theDayLongEvent.title);

    await page.getByRole('button', { name: 'Today' }).click();

    await expect(entryFor(page, anEventSeededToday.title)).toBeVisible();
    await expect(dayLong).toHaveCount(0);
});

test('writes an event down, amends it, and takes it off the calendar', async ({ page }) => {
    const written = 'Handover of the archive keys';
    const amended = 'Key handover, moved to the afternoon';

    await openTheCalendar(page);

    await page.getByRole('button', { name: 'New event' }).click();

    const writing = page.getByRole('dialog', { name: 'New event' });

    await expect(writing).toBeVisible();

    await writing.getByLabel('Title', { exact: true }).fill(written);
    await writing.getByLabel('Day', { exact: true }).fill(seededDay(0));
    await writing.getByLabel('Starts', { exact: true }).fill('17:00');
    await writing.getByLabel('Ends', { exact: true }).fill('17:45');
    await writing.getByRole('button', { name: 'Save' }).click();

    await expect(entryFor(page, written)).toBeVisible();

    // The reload is what says the deployment holds it rather than the screen: everything above would be equally true
    // of a client that had written nothing and drawn what it was asked to.
    await page.reload();
    await goToSpace(page, 'Calendar');
    await expect(entryFor(page, written)).toBeVisible();

    await entryFor(page, written).click();

    const open = page.getByRole('dialog', { name: written });

    await open.getByRole('button', { name: 'Edit' }).click();
    await open.getByLabel('Title', { exact: true }).fill(amended);
    await open.getByRole('button', { name: 'Save' }).click();

    await expect(entryFor(page, amended)).toBeVisible();
    await expect(entryFor(page, written)).toHaveCount(0);

    await entryFor(page, amended).click();
    await page.getByRole('dialog', { name: amended }).getByRole('button', { name: 'Delete the event' }).click();
    await page.getByRole('button', { name: 'Delete from the calendar' }).click();

    await expect(entryFor(page, amended)).toHaveCount(0);

    // The calendar the run seeded is untouched by all of that, which is what lets the files in this directory run in
    // any order under the suite's single worker.
    await expect(entryFor(page, anEventSeededToday.title)).toBeVisible();
});
