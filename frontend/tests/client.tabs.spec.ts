// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { desktopWindow, openAccountMenu, openSignedIn, row, test } from './client.harness';

// Working in tabs, which is a composition of the desktop width alone: the list opens each message as a tab of its own,
// the strip brings one forward, and closing the last one hands the pane to what stands in for an empty workspace. Each
// step is read on a different component from the one acted on — the strip, the reading pane, and the empty state —
// which is what makes it a journey rather than a copy of the strip's own unit test. The fake answers every message with
// one body, so which message the pane holds is read off the strip rather than off the pane.

test.use({ viewport: desktopWindow });

test('opens messages as tabs, brings one forward from the keyboard, and opens the last one again once nothing is open', async ({
    page,
}) => {
    await openSignedIn(page, '/#/mail');
    await openAccountMenu(page);
    // Pressed from the keyboard because the row's own label is drawn over the switch and takes the pointer.
    await page.getByRole('switch', { name: 'Tab mode' }).press('Space');
    await expect(page.getByRole('switch', { name: 'Tab mode' })).toBeChecked();
    await page.keyboard.press('Escape');

    await row(page, 'Message 0').click();
    await row(page, 'Message 2').click();

    const strip = page.getByRole('tablist', { name: 'Open tabs' });

    await expect(strip.getByRole('tab')).toHaveCount(2);
    await expect(strip.getByRole('tab', { name: 'Message 2', selected: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'What is open' })).toBeVisible();

    // The tab is a button to the platform, and Enter and Space activating a focused button is the platform's own
    // behaviour rather than a handler the strip wrote — which is why a browser is the one witness to it.
    await strip.getByRole('tab', { name: 'Message 2' }).focus();
    await page.keyboard.press('ArrowLeft');
    await expect(strip.getByRole('tab', { name: 'Message 2', selected: true })).toBeVisible();
    await page.keyboard.press('Enter');
    await expect(strip.getByRole('tab', { name: 'Message 0', selected: true })).toBeVisible();

    // Bringing a tab forward puts focus at the start of what it opened, so the strip is reached again before Space.
    await strip.getByRole('tab', { name: 'Message 2' }).focus();
    await page.keyboard.press('Space');
    await expect(strip.getByRole('tab', { name: 'Message 2', selected: true })).toBeVisible();

    await page.getByRole('button', { name: 'Close Message 0' }).click();
    await expect(strip.getByRole('tab')).toHaveCount(1);

    await page.getByRole('button', { name: 'Close Message 2' }).click();
    await expect(strip).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Nothing is open' })).toBeFocused();

    await page.getByRole('button', { name: 'Open the last message' }).click();
    await expect(strip.getByRole('tab')).toHaveCount(1);
    await expect(strip.getByRole('tab', { name: 'Message 2', selected: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'What is open' })).toBeVisible();
});
