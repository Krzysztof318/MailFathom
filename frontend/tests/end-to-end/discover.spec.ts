// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test } from '@playwright/test';
import { signIn } from './deployment';

// Discover, which is where the client opens and which is now a screen rather than a placeholder.
//
// **What this file holds is the screen a deployment lands somebody on, and not an answer.** The space draws the
// questions it offers, and pressing one starts a real run over `POST /api/client/discovery/runs` — but composing an
// answer needs a chat provider, and this run configures none. So a check that pressed a suggestion and waited for a
// block would be waiting on a model nothing here provides, and one that asserted a particular ending would be pinning
// which failure a deployment with no provider reports — a service decision this file is not the place to fix.
//
// What is worth holding is that landing reaches the screen rather than the note saying nothing is behind it, that the
// questions it offers are there to press, and that the question field and its scope — the frame's rather than the
// space's — stand on it, because a space in front is exactly where a frame regression shows.
//
// The answered canvas is the unit suite's claim, over a transport handed the run the corpus states, and the browser
// suite's for what a built bundle draws. What would move here is a run this deployment can actually answer, which is a
// provider configured for the run rather than a line in this file.

test('opens in Discover, on the questions it offers', async ({ page }) => {
    await signIn(page);

    // Signing in lands here rather than navigating to it, which is the claim `defaultSpace` makes.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    const discover = page.getByRole('main', { name: 'Discover' });

    await expect(discover.getByRole('heading', { name: 'Try this' })).toBeVisible();
    await expect(discover.getByRole('listitem').first().getByRole('button')).toBeVisible();

    // The note every unbuilt space carries is what this screen replaced, so its absence is part of the claim.
    await expect(discover.getByText('This space is not built yet.', { exact: false })).toHaveCount(0);

    // The question and the scope it is asked under are the frame's, composed for whichever space is in front — and the
    // design draws them under this screen's own head rather than at its foot.
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

    // The question outlives the space it was typed into, because it belongs to the frame. Typing it asked nothing, so
    // the screen is still the one that offers questions.
    await expect(page.getByRole('searchbox', { name: 'Ask your mail' })).toHaveValue(question);
    await expect(page.getByRole('main', { name: 'Discover' }).getByRole('heading', { name: 'Try this' })).toBeVisible();
});
