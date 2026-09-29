// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// An event written, amended, and deleted through the calendar, and each of the four views read after every act. A
// view reads its own span again rather than correcting what another one held, so what each draws is what the next read
// of the deployment answered — and a view that drew from anything it kept would disagree with the other three.

// The corpus deployment states Greenwich as the reader's zone, and the reader sits in it. The views place an event on
// the browser's own day and word its time in the deployment's zone, so a browser standing on another day would file the
// event under a day the journey never wrote it on.
test.use({ timezoneId: 'UTC' });

const views = ['Week', 'Day', 'Month', 'Agenda'] as const;

/** The day the reader is on, as the listbox of one day's events is named in the three views drawing days. */
function dayNamed(): string {
    return new Intl.DateTimeFormat('en', {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric',
        timeZone: 'UTC',
    }).format(new Date());
}

/** One hour of the day, as the listbox the day view draws that hour's events in is named. */
function hourNamed(hour: number): string {
    return new Intl.DateTimeFormat('en', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' }).format(
        Date.UTC(2026, 0, 1, hour),
    );
}

/** Today as the day field of the event form writes it, which is what an instant on it is composed from. */
function today(): string {
    return new Date().toISOString().slice(0, 10);
}

/**
 * Where one view draws the reader's day — the hour row in the day view, the day everywhere else — once that view's
 * own read has answered.
 *
 * What says the read answered is the corpus's first entry, which every window places on its first day at nine: the
 * views draw nothing while a span is being read, so an absence asserted before it would be an absence of anything.
 */
async function viewOfToday(page: Page, view: (typeof views)[number], hour: number) {
    // Chosen from the keyboard, because the radio itself is drawn inside the label that names it and a pointer lands
    // on the label.
    await page.getByRole('radio', { name: view }).press('Space');
    await expect(page.getByRole('option', { name: /Warehouse handover$/u }).first()).toBeVisible();

    return page.getByRole('listbox', {
        name: view === 'Day' ? `Events at ${hourNamed(hour)}` : `Events on ${dayNamed()}`,
    });
}

test('draws an event it wrote, amended, and deleted the same in the week, the day, the month, and the agenda', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page, '/#/calendar');

    await page.getByRole('button', { name: 'New event' }).click();

    const writing = page.getByRole('dialog', { name: 'New event' });

    await writing.getByLabel('Title').fill('Dentist appointment');
    await writing.getByLabel('Starts').fill('13:00');
    await writing.getByLabel('Ends').fill('13:30');
    await writing.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toBeVisible();

    for (const view of views) {
        await expect(
            (await viewOfToday(page, view, 13)).getByRole('option', { name: /Dentist appointment$/u }),
        ).toBeVisible();
    }

    expect(deployment.requests('POST', '/calendar').map(({ body }) => body)).toStrictEqual([
        {
            title: 'Dentist appointment',
            start: `${today()}T13:00:00.000Z`,
            end: `${today()}T13:30:00.000Z`,
            isAllDay: false,
            reminders: [],
            sourceMessage: null,
        },
    ]);

    // Amended from the agenda, which is the view the loop above ended on, and moved two hours later on the same day.
    await (await viewOfToday(page, 'Agenda', 13)).getByRole('option', { name: /Dentist appointment$/u }).click();

    const opened = page.getByRole('dialog', { name: 'Dentist appointment' });

    await opened.getByRole('button', { name: 'Edit' }).click();
    await opened.getByLabel('Title').fill('Dental check-up');
    await opened.getByLabel('Starts').fill('15:00');
    await opened.getByLabel('Ends').fill('15:45');
    await opened.getByRole('button', { name: 'Save' }).click();

    const amended = page.getByRole('dialog', { name: 'Dental check-up' });

    await expect(amended.getByRole('heading', { name: 'Dental check-up' })).toBeVisible();
    await amended.getByRole('button', { name: 'Close' }).click();

    for (const view of views) {
        const drawn = await viewOfToday(page, view, 15);

        await expect(drawn.getByRole('option', { name: /Dental check-up$/u })).toBeVisible();
        await expect(page.getByRole('option', { name: /Dentist appointment$/u })).toHaveCount(0);
    }

    // The hour it was moved out of holds nothing of it any more, which the day view is the only one to say.
    await expect((await viewOfToday(page, 'Day', 13)).getByRole('option', { name: /Dental check-up$/u })).toHaveCount(
        0,
    );

    const [amendment] = deployment.issued.filter(
        ({ method, route }) => method === 'PUT' && route.startsWith('/calendar/'),
    );

    expect(amendment?.body).toStrictEqual({
        title: 'Dental check-up',
        start: `${today()}T15:00:00.000Z`,
        end: `${today()}T15:45:00.000Z`,
        isAllDay: false,
        reminders: [],
    });

    await (await viewOfToday(page, 'Week', 15)).getByRole('option', { name: /Dental check-up$/u }).click();
    await page
        .getByRole('dialog', { name: 'Dental check-up' })
        .getByRole('button', { name: 'Delete the event' })
        .click();
    await page.getByRole('button', { name: 'Delete from the calendar' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Deleted.' })).toBeVisible();

    for (const view of views) {
        await viewOfToday(page, view, 15);
        await expect(page.getByRole('option', { name: /Dental check-up$/u })).toHaveCount(0);
    }

    // The event deleted is the one amended, which is the one the write created: one identity from start to end.
    expect(deployment.issued.filter(({ method }) => method === 'DELETE').map(({ route }) => route)).toStrictEqual([
        amendment?.route,
    ]);
});
