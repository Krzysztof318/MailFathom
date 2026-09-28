// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// A person written into the address book, offered to a message being written, opened, and deleted through People, with
// the book read after every act. The book reads itself again after a write rather than adding the row it was answered
// with, the composer searches the deployment's book rather than the one People drew, and the page beside the book reads
// what the mail index says about whoever is open — so all three agree only where each drew the deployment's answer.

const name = 'Beatrice Holm';
const address = 'beatrice@carrier.example';

// A fragment the new person carries and exactly one other person does — Bartosz Rowe, whom the mailboxes collected — so
// the list the composer offers after the deletion has somebody in it, and the absence is read off an answer that came.
const fragment = 'b';

/** Types the fragment into the new message's first header and answers the list it offers. */
async function offeredFor(page: Page) {
    await page.getByRole('combobox', { name: 'To' }).fill(fragment);

    return page.getByRole('listbox', { name: 'Suggested recipients for To' });
}

test('offers a person it wrote down to a message, opens their page, and lets them go from all three when deleted', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.contacts.read', 'mailfathom.mail.contacts.write', 'mailfathom.mail.drafts.write');

    await openSignedIn(page, '/#/people');

    const book = page.getByRole('listbox', { name: 'Contacts' });

    await expect(book.getByRole('option', { name: /Anna Marlow/u })).toBeVisible();

    await page.getByRole('button', { name: 'New contact' }).click();

    const writing = page.getByRole('dialog', { name: 'New contact' });

    await writing.getByLabel('Name').fill(name);
    await writing.getByLabel('Email address').fill(address);
    await writing.getByRole('button', { name: 'Save contact' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Contact added.' })).toBeVisible();
    await expect(book.getByRole('option', { name: new RegExp(name, 'u') })).toBeVisible();

    expect(deployment.requests('POST', '/contacts').map(({ body }) => body)).toStrictEqual([
        { displayName: name, addresses: [address], preferredAddress: address, note: null },
    ]);

    // At once somebody to write to: the composer finds them by a fragment, and choosing them addresses the message.
    await page.getByRole('link', { name: 'Mail' }).click();
    await page.getByTitle('New message').click();

    const offered = await offeredFor(page);

    await expect(offered.getByRole('option', { name: /Bartosz Rowe/u })).toBeVisible();
    await offered.getByRole('option', { name: new RegExp(name, 'u') }).click();
    await expect(page.getByRole('button', { name: `Remove ${address} from To` })).toBeVisible();

    await page.getByRole('link', { name: 'People' }).click();
    await book.getByRole('option', { name: new RegExp(name, 'u') }).click();

    const person = page.getByRole('region', { name });

    await expect(person.getByText(address)).toBeVisible();
    await expect(person.getByText('No conversation here names this person.')).toBeVisible();

    const [correspondence] = deployment.issued.filter(
        ({ method, route }) => method === 'GET' && route.endsWith('/correspondence'),
    );
    const contactRoute = correspondence?.route.replace(/\/correspondence$/u, '');

    await person.getByRole('button', { name: 'Delete contact' }).click();
    await page.getByRole('dialog', { name: 'Delete this contact?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Deleted.' })).toBeVisible();
    await expect(page.getByText('Open somebody to read their page.')).toBeVisible();
    await expect(book.getByRole('option', { name: /Anna Marlow/u })).toBeVisible();
    await expect(book.getByRole('option', { name: new RegExp(name, 'u') })).toHaveCount(0);

    // The person deleted is the one opened, which is the one the write created.
    expect(deployment.issued.filter(({ method }) => method === 'DELETE').map(({ route }) => route)).toStrictEqual([
        contactRoute,
    ]);

    // The message is where it was left, having outlived both moves between the spaces. The address is taken off it
    // again, so what the composer leaves out after the deletion is the book's doing rather than the header's: a field
    // never offers an address it already holds.
    await page.getByRole('link', { name: 'Mail' }).click();
    await page.getByRole('button', { name: `Remove ${address} from To` }).click();

    const offeredAgain = await offeredFor(page);

    await expect(offeredAgain.getByRole('option', { name: /Bartosz Rowe/u })).toBeVisible();
    await expect(offeredAgain.getByRole('option', { name: new RegExp(name, 'u') })).toHaveCount(0);

    // Both lookups asked the deployment's own book, with the fragment and no more than the list shows of it.
    expect(
        deployment
            .requests('GET', '/contacts')
            .filter(({ query }) => query.has('search'))
            .map(({ query }) => [query.get('search'), query.get('pageSize')]),
    ).toStrictEqual([
        [fragment, '5'],
        [fragment, '5'],
    ]);
});
