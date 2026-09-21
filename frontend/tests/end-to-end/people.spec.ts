// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test, type Locator, type Page } from '@playwright/test';
import { goToSpace, openSpace } from './deployment';
import { seededContacts, theOpenedContact } from './seed';

// The People space against a real deployment: the address book the run wrote, somebody opened out of it, the second
// book this screen offers, and one contact written down and deleted through the screen.
//
// Naming what the run seeded is allowed here for the reason `calendar.spec.ts` gives: this book was stated by
// `seed.ts` rather than generated.

/** Reaches the People space and waits for the seeded book to have been drawn. */
async function openTheBook(page: Page): Promise<void> {
    await openSpace(page, 'People');
    await expect(rowFor(page, theOpenedContact.displayName)).toBeVisible();
}

/** One person in the book, which is an option of the one list the screen draws. */
function rowFor(page: Page, displayName: string): Locator {
    return page.getByRole('listbox', { name: 'Contacts' }).getByRole('option', { name: displayName });
}

/** Puts the screen onto one of the two books, by the choice a person makes. */
async function reading(page: Page, book: string): Promise<void> {
    await page.getByRole('group', { name: 'Which address book' }).getByText(book, { exact: true }).click();
}

test('draws the address book the deployment holds', async ({ page }) => {
    await openTheBook(page);

    for (const contact of seededContacts) {
        await expect(rowFor(page, contact.displayName)).toBeVisible();
    }
});

test('opens somebody, and says what the deployment found about their correspondence', async ({ page }) => {
    await openTheBook(page);

    await rowFor(page, theOpenedContact.displayName).click();

    const opened = page.getByRole('region', { name: theOpenedContact.displayName });

    await expect(opened).toBeVisible();
    await expect(opened.getByText(theOpenedContact.address)).toBeVisible();

    // The deployment correlated this person against the mail it synchronized and found nothing, which is the right
    // answer: every address in the sample book is invented and the corpus names none of them. What it proves is that
    // the correlation ran at all — a screen that never asked would draw the same words as one still reading.
    await expect(
        page.getByRole('region', { name: 'Relationship state' }).getByText('No mail exchanged with this person.'),
    ).toBeVisible();
});

test('keeps what mail picked up in a book of its own, which this deployment collects nothing into', async ({
    page,
}) => {
    await openTheBook(page);

    await reading(page, 'Collected');

    // Collecting correspondents out of synchronized mail is switched off for an account this run records through the
    // administrative API, so the honest state of the second book here is the empty one — and drawing it is what says
    // the two books are two books rather than one list with a filter over it.
    await expect(page.getByText('Nobody has been picked up from your mail yet.')).toBeVisible();

    await reading(page, 'Own');

    await expect(rowFor(page, theOpenedContact.displayName)).toBeVisible();
});

test('writes somebody down, and takes them out of the book again', async ({ page }) => {
    const written = 'Elin Saarinen';
    const address = 'elin.saarinen@lakeside-press.invalid';

    await openTheBook(page);

    await page.getByRole('button', { name: 'New contact' }).click();

    const writing = page.getByRole('dialog', { name: 'New contact' });

    await expect(writing).toBeVisible();

    await writing.getByLabel('Name', { exact: true }).fill(written);
    await writing.getByLabel('Email address', { exact: true }).fill(address);
    await writing.getByRole('button', { name: 'Save contact' }).click();

    await expect(rowFor(page, written)).toBeVisible();

    // The reload is what says the deployment holds them rather than the screen.
    await page.reload();
    await goToSpace(page, 'People');
    await expect(rowFor(page, written)).toBeVisible();

    await rowFor(page, written).click();
    await page.getByRole('region', { name: written }).getByRole('button', { name: 'Delete contact' }).click();
    await page.getByRole('dialog', { name: 'Delete this contact?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(rowFor(page, written)).toHaveCount(0);

    // The seeded book is as it was, which is what lets the files in this directory run in any order.
    await expect(rowFor(page, theOpenedContact.displayName)).toBeVisible();
});
