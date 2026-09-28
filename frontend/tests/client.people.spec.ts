// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// A person written into the address book, opened, and deleted through People, with the book read after every act. The
// book reads itself again after a write rather than adding the row it was answered with, and the page beside it reads
// what the mail index says about whoever is open — so the three agree only where each drew the deployment's answer.

const name = 'Beatrice Holm';
const address = 'beatrice@carrier.example';

test('lists a person it wrote down, opens their page, and lets them go from both when they are deleted', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.contacts.read', 'mailfathom.mail.contacts.write');

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
});
