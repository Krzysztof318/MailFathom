// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { markupOnlySubject, newsletterSubject } from './fixtures/messages';
import { openSignedIn, test } from './client.harness';

// The folder acts driven from the tree, each held against what reads the mailbox's folders afterwards: the tree, the
// folders a message can be filed into, a reload, and — for a deletion — a search that used to find the mail filed
// there. Each of them reads the folders again on its own, which is the seam a folder act crosses.

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

// A folder is declared under its own name and keeps that alias through a rename and a move, so every one of these
// readings has to place it by where it sits rather than by what it is called.
test('makes a folder inside another, renames it, and moves it under a third, and the tree, the move targets, and a reload agree after each', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.folders.write', 'mailfathom.mail.move');
    await openSignedIn(page, '/#/mail');

    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const notices = page.getByRole('list', { name: 'Notices' });

    // A row somebody opens, which is always one that folds: the level `Archive` rather than the archive the mailbox
    // names by its role, which draws the same word and holds nothing. A reload keeps what was opened, so a row already
    // open is left as it is.
    const opened = async (name: string | RegExp) => {
        const shut = tree.getByRole('treeitem', { name, exact: true, expanded: false });
        const open = tree.getByRole('treeitem', { name, exact: true, expanded: true });

        await expect(shut.or(open)).toBeVisible();

        if ((await shut.count()) > 0) {
            await shut.focus();
            await page.keyboard.press('ArrowRight');
        }

        await expect(open).toBeVisible();
    };

    const agreesItSitsAt = async (opening: readonly (string | RegExp)[], name: string, filedAs: string) => {
        for (const level of opening) {
            await opened(level);
        }

        await expect(tree.getByRole('treeitem', { name, exact: true })).toHaveCount(1);
        await expect(tree.getByRole('treeitem', { name, exact: true })).toBeVisible();
        await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
        await page.getByRole('button', { name: 'Move', exact: true }).click();

        const filing = page.getByRole('dialog', { name: 'File in another folder' });

        await expect(filing.getByRole('button', { name: filedAs, exact: true })).toBeVisible();
        await filing.getByRole('button', { name: 'Close' }).click();
    };

    const editing = async (name: string) => {
        await tree.getByRole('treeitem', { name, exact: true }).click({ button: 'right' });
        await page.getByRole('menuitem', { name: 'Edit folder' }).click();

        return page.getByRole('dialog', { name: 'Edit folder' });
    };

    // Made inside a folder two levels down, and drawn under the name typed rather than in the capitals of its alias.
    await opened('Archive');
    await tree.getByRole('treeitem', { name: '2024', exact: true }).click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'New folder inside' }).click();

    const making = page.getByRole('dialog', { name: 'New folder' });

    await making.getByRole('textbox', { name: 'Folder name' }).fill('Suppliers');
    await making.getByRole('button', { name: 'Create folder' }).click();
    await expect(notices).toContainText('Created Suppliers in Work.');
    expect(deployment.requests('POST', '/managed-folders').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ account: 'work', parentId: 'ARCHIVE/2024', name: 'Suppliers' }),
    ]);
    await agreesItSitsAt(['2024'], 'Suppliers', 'Archive / 2024 / Suppliers');
    await page.reload();
    await agreesItSitsAt(['Archive', '2024'], 'Suppliers', 'Archive / 2024 / Suppliers');

    // Renamed where it stands, and nothing is left under its old name.
    const renaming = await editing('Suppliers');

    await renaming.getByRole('textbox', { name: 'Folder name' }).fill('Vendors');
    await renaming.getByRole('button', { name: 'Save changes' }).click();
    await expect(notices).toContainText('Renamed to Vendors in Work.');
    await expect(tree.getByRole('treeitem', { name: 'Suppliers', exact: true })).toHaveCount(0);
    await agreesItSitsAt([], 'Vendors', 'Archive / 2024 / Vendors');
    await page.reload();
    await agreesItSitsAt(['Archive', '2024'], 'Vendors', 'Archive / 2024 / Vendors');

    // Moved under the inbox, which takes it out from under the archive level altogether.
    const moving = await editing('Vendors');

    await moving.getByRole('combobox', { name: 'Inside' }).selectOption({ label: 'INBOX' });
    await moving.getByRole('button', { name: 'Save changes' }).click();
    await expect(notices).toContainText('Moved Vendors in Work.');
    expect(
        [
            ...deployment.requests('POST', '/managed-folders/renames'),
            ...deployment.requests('POST', '/managed-folders/moves'),
        ].map(({ body }) => body),
    ).toStrictEqual([
        { account: 'work', folderId: 'SUPPLIERS', name: 'Vendors' },
        { account: 'work', folderId: 'SUPPLIERS', parentId: 'INBOX' },
    ]);
    await agreesItSitsAt([/^Inbox/u], 'Vendors', 'INBOX / Vendors');
    await page.reload();
    await agreesItSitsAt([/^Inbox/u], 'Vendors', 'INBOX / Vendors');
    await opened('Archive');
    await expect(tree.getByRole('treeitem', { name: '2024', exact: true })).not.toHaveAttribute('aria-expanded');
});
