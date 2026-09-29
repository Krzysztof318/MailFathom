// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { markupOnlySubject, newsletterSubject } from './fixtures/messages';
import { openSignedIn, test } from './client.harness';

// A folder that holds mail deleted from the tree, with the three things that read the mailbox's folders held against
// what the act said afterwards: the tree, the folders a message can be filed into, and a search that used to find the
// mail filed there. Each of them reads the folders again on its own, which is the seam a deletion crosses.

async function findRenewal(page: Page) {
    const search = page.getByRole('searchbox', { name: 'Find a message' });

    await search.fill('renewal');
    await search.press('Enter');

    return page.getByRole('listbox', { name: 'What this search found' });
}

test('deletes a folder holding mail, and the tree, the move targets, a search, and a reload agree with what it said', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.folders.write', 'mailfathom.mail.move');
    await openSignedIn(page, '/#/mail');

    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const notices = page.getByRole('list', { name: 'Notices' });

    // File a message the search finds into the folder about to go, so the deletion has mail to say something about.
    await (await findRenewal(page)).getByRole('option', { name: new RegExp(markupOnlySubject, 'u') }).click();
    await page.getByRole('button', { name: 'Move', exact: true }).click();
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();
    await expect(notices).toContainText('Filed in “Archive / 2024”');
    await page.getByRole('button', { name: 'Stop searching' }).click();

    // The level the folder sits under, rather than the archive the mailbox names by its role, which draws the same word.
    await tree.getByRole('treeitem', { name: 'Archive', expanded: false }).focus();
    await page.keyboard.press('ArrowRight');
    await tree.getByRole('treeitem', { name: '2024' }).click();
    await expect(
        page
            .getByRole('listbox', { name: 'Messages' })
            .getByRole('option', { name: new RegExp(markupOnlySubject, 'u') }),
    ).toBeVisible();

    await tree.getByRole('treeitem', { name: '2024' }).click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete folder' }).click();

    const confirming = page.getByRole('dialog', { name: 'Delete 2024?' });

    await expect(confirming).toContainText('2024 leaves Work.');
    await expect(confirming).toContainText('What happens to the mail in it depends on how this mailbox is kept');
    await confirming.getByRole('button', { name: 'Delete folder' }).click();

    await expect(notices).toContainText('Deleted 2024 on your mail server. The mail stored from it was removed.');
    expect(deployment.requests('POST', '/managed-folders/deletions').map(({ body }) => body)).toStrictEqual([
        { account: 'work', folderId: 'ARCHIVE/2024' },
    ]);

    // The folder is gone from each reading of the mailbox, and so is the mail the notice said was removed with it.
    const agreesTheFolderIsGone = async () => {
        await expect(tree.getByRole('treeitem', { name: /^Inbox/u })).toBeVisible();
        await expect(tree.getByRole('treeitem', { name: '2024' })).toHaveCount(0);
        await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
        await page.getByRole('button', { name: 'Move', exact: true }).click();

        const filing = page.getByRole('dialog', { name: 'File in another folder' });

        await expect(filing.getByRole('button', { name: 'Inbox' })).toBeVisible();
        await expect(filing.getByRole('button', { name: 'Archive / 2024' })).toHaveCount(0);
        await filing.getByRole('button', { name: 'Close' }).click();
    };

    await agreesTheFolderIsGone();

    const found = await findRenewal(page);

    await expect(found.getByRole('option', { name: new RegExp(newsletterSubject, 'u') })).toBeVisible();
    await expect(found.getByRole('option', { name: new RegExp(markupOnlySubject, 'u') })).toHaveCount(0);
    await page.getByRole('button', { name: 'Stop searching' }).click();

    await page.reload();
    await agreesTheFolderIsGone();
});
