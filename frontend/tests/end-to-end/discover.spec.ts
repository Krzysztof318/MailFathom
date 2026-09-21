// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test } from '@playwright/test';
import { signIn } from './deployment';

// Discover, which is where the client opens and which no issue has built yet.
//
// **What this file asserts is the whole of what can be asserted, and saying so is the point.** `routing/spaces.ts`
// leaves `discover` out of `implementedSpaces`, so what the client draws there is the placeholder every unbuilt space
// gets: its name, the note that the frame is all there is, and the question field the frame composes for every space.
// Nothing in `Client.App` reads `/api/client/discovery/runs`, and the `answerCanvas` components that will one day draw
// an answer are mounted by nothing — so there is no answer to ask this deployment for, with or without a model behind
// it. This run configures no chat provider either, which is a second reason and the weaker one.
//
// A check that typed a question and waited for an answer would therefore be waiting for a screen that does not exist,
// and one that asserted an error would be asserting a failure this client never reports. What is worth holding is
// that the space is reachable, that it says what it is rather than drawing an empty frame somebody would read as
// broken, and that the question field is there — because that field is the frame's rather than the space's, and a
// space with nothing behind it is exactly where a frame regression would show.
//
// This file is what has to change when the space is built: an answer drawn against a real run belongs here, beside
// the specs for the three spaces that already have something behind them.

test('opens in Discover, and says that the space is the frame and nothing else yet', async ({ page }) => {
    await signIn(page);

    // Signing in lands here rather than navigating to it, which is the claim `defaultSpace` makes.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await expect(
        page.getByRole('main', { name: 'Discover' }).getByText('This space is not built yet.', { exact: false }),
    ).toBeVisible();

    // The question and the scope it is asked under are the frame's, composed for whichever space is in front — so
    // they stand on a space that holds nothing, which is what a reader meets on landing.
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'What the question is asked about' })).toBeVisible();
});

test('reaches Discover again from another space, and keeps the question that was typed into it', async ({ page }) => {
    const question = 'What did the archive ask for last week?';

    await signIn(page);

    await page.getByRole('searchbox', { name: 'Ask your mail' }).fill(question);

    await page.getByRole('navigation', { name: 'Spaces' }).getByRole('link', { name: 'Mail' }).click();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();

    await page.getByRole('navigation', { name: 'Spaces' }).getByRole('link', { name: 'Discover' }).click();

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    // The question outlives the space it was typed into, because it belongs to the frame. Nothing was asked of the
    // deployment and nothing could have been: there is no screen to answer onto.
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toHaveValue(question);
});
