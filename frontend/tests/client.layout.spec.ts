// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Locator } from '@playwright/test';

import {
    actingGrants,
    desktopWindow,
    folder,
    foldWindow,
    fromTheMenu,
    messageHeading,
    messageRegion,
    narrowWindow,
    openSignedIn,
    openTheFirstMessage,
    phoneWindow,
    row,
    tabletWindow,
    test,
    wideWindow,
} from './client.harness';

// What a width and a pointer decide: where the navigation stands, the order a keyboard walks the frame in, what the
// narrowest head still holds, and how the mail screens are composed from the phone to the widest window.

interface Box {
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
}

// Playwright answers with nothing for an element that is not laid out, which is a different thing from an element in
// the wrong place — so it is refused by name here rather than asserted through a non-null assertion.
async function boxOf(element: Locator): Promise<Box> {
    const box = await element.boundingBox();

    if (box === null) {
        throw new Error('The element is in the document but has no layout box to read a position off.');
    }

    return box;
}

// The defect this replaced: the stylesheet's focus ring is drawn around whatever took focus, so a transparent field
// inside a bordered box painted a second rectangle inside the first and the bar read as two nested borders. Only a
// browser can answer it — jsdom computes no styles — and what it is asked is both halves together, because a field that
// simply gave its outline up would leave focus invisible. Asked as expressions rather than as closures, for the reason
// the overflow reading above gives: this suite is compiled without a DOM declaration on purpose.
const theFocusedAskFieldsOwnOutline = `
    getComputedStyle(document.activeElement).outlineStyle
`;

const theBoxAroundTheFocusedAskField = `
    getComputedStyle(document.activeElement.parentElement).boxShadow
`;

test('lights the whole ask field on focus rather than drawing a second border inside it', async ({ page }) => {
    await openSignedIn(page);
    await page.getByRole('searchbox', { name: 'Ask your mail' }).focus();

    expect(await page.evaluate<string>(theFocusedAskFieldsOwnOutline)).toBe('none');
    expect(await page.evaluate<string>(theBoxAroundTheFocusedAskField)).not.toBe('none');
});

test('puts the navigation beside the workspace in a wide window and under it in a narrow one', async ({ page }) => {
    await page.setViewportSize(wideWindow);
    await openSignedIn(page);

    const navigation = page.getByRole('navigation', { name: 'Spaces' });
    const space = page.getByRole('main');

    await expect(navigation).toBeVisible();

    // Only a browser answers this: jsdom computes no geometry, so where the two regions sit relative to each other is
    // outside what the unit suite may claim. It is asked of the width alone — nothing in the client reads which head
    // it is running on, and this same tree produces both shapes.
    const rail = await boxOf(navigation);
    const wideSpace = await boxOf(space);
    expect(rail.x + rail.width).toBeLessThanOrEqual(wideSpace.x);

    await page.setViewportSize(narrowWindow);

    // Where the navigation is drawn changes with the width, but what it holds changes with a render: the bar's own
    // five places are what `useWideWorkspace` answers a `matchMedia` change with, so for one task afterwards the
    // navigation is still the rail's contents laid out across the foot of the window — 54 pixels taller than the bar,
    // because the column the rail stacks the bell and the account in is standing in a row. A box read on either side
    // of that render is a measurement of a different screen, and the pair below is read on one side of it by waiting
    // for the control only the bar draws.
    await expect(page.getByRole('button', { name: 'More' })).toBeVisible();

    const bottomBar = await boxOf(navigation);
    const narrowSpace = await boxOf(space);
    expect(bottomBar.y).toBeGreaterThanOrEqual(narrowSpace.y + narrowSpace.height);

    // Nothing is hidden by width alone. The bar has five places and spends them on three spaces, the bell, and an
    // overflow, so a space it has no room for is reached through that rather than dropped — which only a browser can
    // say, the platform's own popover being what opens and what closes it.
    await expect(page.getByRole('link', { name: 'Cases' })).toBeHidden();

    await page.getByRole('button', { name: 'More' }).click();
    await expect(page.getByRole('link', { name: 'Cases' })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByRole('link', { name: 'Cases' })).toBeHidden();
});

test('moves a keyboard through a narrow window in the order the window shows', async ({ page }) => {
    await page.setViewportSize(narrowWindow);
    await openSignedIn(page);

    // The narrow composition draws the navigation at the bottom of the screen, and the keyboard follows the document
    // rather than the layout — so a document that put the navigation first would hand a reader the bottom bar before
    // the space at the top of the window. Only a browser answers this: jsdom has no sequential focus navigation.
    // The freshness line is the first thing the space holds, and it is a disclosure onto the account-by-account
    // reading of the same sentence — so it is where a keyboard arrives before any of the controls beneath it.
    //
    // Waited for before the first key rather than after it: until the accounts have answered, that line is a sentence
    // saying the deployment is being reached, which is text rather than a disclosure and takes no focus at all. A Tab
    // pressed then lands on the control below it and stays there, and every assertion after it would be reading a tab
    // order the window was not yet showing.
    await expect(page.getByText('Every account is up to date.')).toBeVisible();

    // Before the space, the strip saying what this credential may not do. It is a scroller, having a ceiling rather
    // than growing out of the frame, and a scroller with nothing focusable in it is a stop of its own — which is what
    // lets somebody reach the sentences past the ceiling with a keyboard. It is named, so the stop announces itself.
    await page.keyboard.press('Tab');
    await expect(page.getByRole('region', { name: 'What this credential may not do here' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByText('Every account is up to date.')).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeFocused();

    // Past the question's own two controls, the questions Discover offers. They are the space's own contents rather
    // than the frame's, and a keyboard meets them before the navigation for the reason this whole test exists: the
    // document puts the space first whatever the narrow layout draws at the foot of the window.
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'What am I still owed an answer on?' })).toBeFocused();

    // Then the bar's five places in the order the design project draws them: three spaces, then the bell, then the
    // overflow that holds everything else — the account among it. Reached from the last question rather than by
    // tabbing through the two between, which would count the list instead of stating where it ends.
    await page
        .getByRole('button', { name: 'Which version of the contract is the latest one anybody sent me?' })
        .focus();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Discover' })).toBeFocused();

    await page.getByRole('link', { name: /^Agent/u }).focus();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Notifications' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'More' })).toBeFocused();

    // Nothing in the bar comes after the overflow, so the account is reached by opening it rather than by tabbing past
    // it — which is what a bar of five places costs and what the sheet is for.
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Account and preferences' })).toBeVisible();
});

test('stays usable at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await openSignedIn(page);

    // 320 CSS pixels is the bar `frontend/src/AGENTS.md` sets, and what is asked of it is that the frame still holds
    // everything rather than that it looks the same: the space, the intent field, its scope, and every destination the
    // design project shows. Nothing is dropped by width, and the window it is measured in is the width alone.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'What the question is asked about' })).toBeVisible();

    // Three stand in the bar itself and the rest behind its overflow, which is what makes five places enough for every
    // destination. Reached rather than dropped is the whole of the claim, so both halves are counted and the sheet is
    // opened to count the second.
    //
    // Six rather than the seven the design project draws, because this corpus states the two permissions the client
    // needs to open a frame at all and no more: People is reached under the grant that reads the address book, so a
    // credential without it meets six destinations at every width. What is being measured here is the width.
    const bar = page.getByRole('navigation', { name: 'Spaces' });

    await expect(bar.getByRole('link')).toHaveCount(3);

    await page.getByRole('button', { name: 'More' }).click();
    await expect(bar.getByRole('link')).toHaveCount(6);
    await expect(page.getByRole('button', { name: 'Account and preferences' })).toBeVisible();
});

test('draws the mail screens at the phone composition, with its own row height and nothing over the question', async ({
    page,
}) => {
    // The design project's own phone frame, and the only composition in which the list is the whole screen. Everything
    // asked below is geometry, which is what puts it here: how tall a row is, what stands over what, and whether the
    // document has grown wider than the window are three things jsdom answers for nothing.
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const phoneRow = await boxOf(list.getByRole('option').first());

    // The control that writes a message stands over the list rather than over the window, so it clears whatever
    // stands under the column — at this composition the navigation between spaces, which the design draws along the
    // foot of the phone. Placed against the viewport it sat on that instead, which put a press meant for another space
    // onto the control that composes. The question field is not here to clear: the design draws it at the foot of the
    // reading column alone, and at this width that column stands in front of the list only once a message is open.
    const compose = await boxOf(page.getByRole('button', { name: /^New message/u }));
    const spaces = await boxOf(page.getByRole('navigation', { name: 'Spaces' }));

    expect(compose.y + compose.height).toBeLessThanOrEqual(spaces.y);

    // Nothing has laid out past the window, which is the bar `frontend/src/AGENTS.md` sets at every width. Asked as an
    // expression for the reason the same question is asked that way at the three-column width above: this suite is
    // compiled without a DOM declaration, so a closure naming `document` is the one thing that would change that.
    const overflowing = await page.evaluate<boolean>(
        'document.documentElement.scrollWidth > document.documentElement.clientWidth',
    );

    expect(overflowing).toBe(false);

    // The one measurement a composition changes rather than merely rearranges: the design draws a taller row where the
    // list is the whole screen and one height everywhere above that, so the same row is read at both widths.
    await page.setViewportSize(wideWindow);
    await expect(list.getByRole('option').first()).toBeVisible();

    expect(phoneRow.height).toBeGreaterThan((await boxOf(list.getByRole('option').first())).height);
});

test.describe('driven by a finger', () => {
    test.use({ hasTouch: true });

    test('draws every control on the mail screens at a size a fingertip can hit', async ({ page }) => {
        await page.setViewportSize(phoneWindow);
        await openSignedIn(page, '/#/mail');

        await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

        // The floor is asked of the pointer rather than of the width, and `styles.css` is the one place it is stated —
        // so what this proves is that the statement reaches the whole screen rather than the shapes that remembered it.
        // Only a browser answers it twice over: the query is the real one, and the size is a measured box.
        for (const control of await page.getByRole('main').getByRole('button').all()) {
            const box = await control.boundingBox();

            if (box === null) {
                continue;
            }

            expect(
                Math.min(box.width, box.height),
                `a control on the Mail space is ${String(box.height)} tall`,
            ).toBeGreaterThanOrEqual(44);
        }
    });

    // The three compositions a finger drives, each at the design project's own frame. What sets them apart is which of
    // three regions stands beside which: the phone draws the list or the message and no toolbar, while the fold and the
    // tablet draw both panes under the toolbar. All three keep the mailboxes behind a drawer.
    const touchCompositions = [
        { name: 'phone', window: phoneWindow, twoPanes: false },
        { name: 'fold', window: foldWindow, twoPanes: true },
        { name: 'tablet', window: tabletWindow, twoPanes: true },
    ] as const;

    for (const composition of touchCompositions) {
        test(`draws the Mail space at the ${composition.name} composition within the window and at a fingertip’s size`, async ({
            page,
        }) => {
            await page.setViewportSize(composition.window);
            await openSignedIn(page, '/#/mail');

            const list = page.getByRole('listbox', { name: 'Messages' });

            await expect(list.getByRole('option').first()).toBeVisible();

            // The mailboxes stand behind a drawer at every one of the three, opened from the list's own head and
            // closed again from inside it. The drawer is the platform's modal dialog, so closing it is what hands focus
            // back to the control that opened it.
            const opener = page.getByRole('button', { name: 'Folders and filters' });
            const drawer = page.getByRole('dialog', { name: 'Folders and filters' });

            await expect(page.getByRole('complementary', { name: 'Folders and filters' })).toHaveCount(0);
            await opener.click();
            await expect(drawer.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();

            await drawer.getByRole('button', { name: 'Close the folders' }).click();
            await expect(drawer).toBeHidden();
            await expect(opener).toBeFocused();

            await list.getByRole('option').first().click();
            await expect(page.getByRole('article', messageRegion)).toBeVisible();

            const listColumn = page.getByRole('region', { name: 'Message list' });
            const readingColumn = page.getByRole('region', { name: 'What is open' });
            const toolbar = page.getByRole('toolbar', { name: 'Mail actions' });

            if (composition.twoPanes) {
                // Both panes, side by side, under the toolbar the phone has no room for.
                const listed = await boxOf(listColumn);
                const reading = await boxOf(readingColumn);

                expect(listed.x + listed.width).toBeLessThanOrEqual(reading.x);
                await expect(toolbar).toBeVisible();

                // Whatever the strip had to give up to fit the width, every control on it still says what it does.
                for (const control of await toolbar.getByRole('button').all()) {
                    await expect(control).toHaveAccessibleName(/\S/u);
                }
            } else {
                // One pane: the message stands where the list stood, and the list is out of the way rather than
                // beside it.
                await expect(readingColumn).toBeVisible();
                await expect(listColumn).toBeHidden();
                await expect(toolbar).toHaveCount(0);
            }

            for (const control of await page.getByRole('main').getByRole('button').all()) {
                const box = await control.boundingBox();

                if (box === null) {
                    continue;
                }

                expect(
                    Math.min(box.width, box.height),
                    `${String(await control.textContent())} on the ${composition.name} Mail space is ${String(box.height)} tall`,
                ).toBeGreaterThanOrEqual(44);
            }

            expect(
                await page.evaluate<boolean>(
                    'document.documentElement.scrollWidth > document.documentElement.clientWidth',
                ),
            ).toBe(false);
        });
    }
});

test('keeps the open message, the folder, and the selection as the window crosses every composition', async ({
    page,
    deployment,
}) => {
    deployment.grant(...actingGrants);

    await page.setViewportSize(desktopWindow);
    await openSignedIn(page, '/#/mail');

    // A folder of two messages of its own, so the scope is one a width change could lose rather than the inbox every
    // composition opens on anyway.
    await fromTheMenu(page, 'Message 4', 'Move…');
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();
    await expect(row(page, 'Message 4')).toHaveCount(0);

    await fromTheMenu(page, 'Message 7', 'Move…');
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();
    await expect(row(page, 'Message 7')).toHaveCount(0);

    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await folder(page, '2024').click();

    const listed = page.getByRole('listbox', { name: 'Messages' }).getByRole('option');
    const selection = page.getByRole('toolbar', { name: 'Actions on the messages selected' });

    await row(page, 'Message 4').click();
    await row(page, 'Message 7').click({ modifiers: ['ControlOrMeta'] });
    await expect(selection.getByRole('status')).toHaveText('1 selected');

    async function holdsWhatItHeld(): Promise<void> {
        await expect(page.getByRole('article', messageRegion)).toBeVisible();
        await expect(selection.getByRole('status')).toHaveText('1 selected');
    }

    // Down to the tablet, which is known by the way into the drawer the desktop has no need of; then the list and the
    // message are measured side by side, which is the tablet's own shape.
    await page.setViewportSize(tabletWindow);
    await expect(page.getByRole('button', { name: 'Folders and filters' })).toBeVisible();
    await holdsWhatItHeld();
    await expect(listed).toHaveCount(2);
    await expect(row(page, 'Message 4')).toHaveAttribute('aria-current', 'true');
    await expect(row(page, 'Message 7')).toHaveAttribute('aria-selected', 'true');

    const tabletList = await boxOf(page.getByRole('region', { name: 'Message list' }));
    const tabletReading = await boxOf(page.getByRole('region', { name: 'What is open' }));

    expect(tabletList.x + tabletList.width).toBeLessThanOrEqual(tabletReading.x);

    // Down to the phone, which is known by the bottom bar's overflow: the message stands in front, and the way back
    // finds the list of the folder it was left in, still holding what was picked out.
    await page.setViewportSize(phoneWindow);
    await expect(page.getByRole('button', { name: 'More' })).toBeVisible();
    await holdsWhatItHeld();
    await expect(page.getByRole('region', { name: 'Message list' })).toBeHidden();

    await page.getByRole('button', { name: 'Back to the list' }).click();
    await expect(listed).toHaveCount(2);
    await expect(row(page, 'Message 7')).toHaveAttribute('aria-selected', 'true');

    // And back to the desktop, which is known by the mailbox column standing beside the list, with the folder still
    // the one chosen there.
    await page.setViewportSize(desktopWindow);
    await expect(page.getByRole('complementary', { name: 'Folders and filters' })).toBeVisible();
    await expect(folder(page, '2024')).toHaveAttribute('aria-selected', 'true');
    await expect(listed).toHaveCount(2);
    await expect(selection.getByRole('status')).toHaveText('1 selected');
});

test('draws the three columns of the Mail space without the page scrolling sideways', async ({ page }) => {
    // The width the third column arrives at, which is where the three of them have the least room they will ever have:
    // one pixel narrower and the mailboxes are a drawer instead. A column that held its width here rather than giving
    // way is what would push the page wider than the window, and only a browser answers that — jsdom computes no
    // geometry.
    await page.setViewportSize({ width: 1180, height: 800 });

    // With a message open, so the third column is the pane a reader actually gets rather than the note standing in
    // for it: the pane is the column that has to give way, and an empty one proves nothing about a filled one.
    await openTheFirstMessage(page);
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    // Each column against the space it stands in, reached by its role like everything else here. A column that held
    // its width rather than giving way lays out past that space's right edge, which is what makes the region scroll
    // sideways while the document stays exactly as wide as the window.
    const room = await boxOf(page.getByRole('main'));
    const columns = [
        await boxOf(page.getByRole('tree', { name: 'Mailboxes and folders' })),
        await boxOf(page.getByRole('listbox', { name: 'Messages' })),
        await boxOf(page.getByRole('article', messageRegion)),
    ];

    for (const column of columns) {
        expect(column.x).toBeGreaterThanOrEqual(room.x);
        expect(column.x + column.width).toBeLessThanOrEqual(room.x + room.width);
    }
});

test('stops a message’s own content at the reading ceiling and leaves everything around it the pane', async ({
    page,
}) => {
    // Wider than the width the design draws the pane at, which is the only regime where the ceiling binds at all: at
    // the width the composition was drawn against the pane is already narrower than it, and a pane that had lost the
    // ceiling entirely would look identical there.
    await page.setViewportSize({ width: 1920, height: 1080 });

    await openTheFirstMessage(page);
    await expect(page.getByRole('heading', messageHeading)).toBeVisible();

    // The sender's own heading stands inside the message's content and the list of files beside it does not, so the
    // two boxes are the whole comparison — same left edge, different widths. What the ceiling is worth in pixels
    // is the token's business rather than this suite's; what is asserted is the shape, because both ways of getting it
    // wrong are visible here and nowhere jsdom can reach. A ceiling dropped again draws the two at one width, and a
    // centred one moves the content's left edge off the edge everything around it keeps.
    const content = await boxOf(page.getByRole('heading', messageHeading));
    const aroundIt = await boxOf(page.getByRole('list', { name: 'Files this message carries' }));

    expect(content.width).toBeLessThan(aroundIt.width);
    expect(content.x).toBeCloseTo(aroundIt.x, 0);

    // And the region the pane draws the message in keeps the pane's own width, which is what the ceiling stopped
    // binding: a head laid out to a measure meant for paragraphs is the defect this asserts against.
    const region = await boxOf(page.getByRole('article', messageRegion));
    expect(region.width).toBeGreaterThan(content.width);
});
