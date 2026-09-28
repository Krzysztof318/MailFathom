// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// The client's own motion, resolved against a real reduced-motion preference and a computed stylesheet.

/** The notification centre opened, which is the panel the client's own motion is stated on. */
async function openNotifications(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Notifications' }).click();

    await expect(page.getByRole('dialog', { name: 'Notifications' })).toBeVisible();
}

// What the panel's own transition runs at, read off the open centre. Written as a self-contained expression rather
// than as a closure taking the element, for the reason the two document-level ones above give: this suite compiles
// without a DOM declaration on purpose, so a function naming `getComputedStyle` would be the one thing that changes.
// The panel is named by the same accessible name the query above finds it by, so the style read and the visibility
// assertion are about one element rather than two.
const theOpenPanelsTransitionDuration = `
    getComputedStyle(document.querySelector('dialog[open][aria-label="Notifications"]')).transitionDuration
`;

/**
 * Every duration the panel's own transition runs at, deduplicated.
 *
 * A transition over four properties reports four durations, so what is read is the set of them: a rule that removed
 * the motion from three of them and left the fourth would be a screen that still moves.
 */
async function motionDurations(page: Page): Promise<string[]> {
    const stated = await page.evaluate(theOpenPanelsTransitionDuration);

    // A read answering with anything but the string a computed style is has not measured the panel at all, and an
    // empty list stood in for it would satisfy *both* assertions below — which is how the first version of this
    // helper reported the moving case green against a page it had never read.
    expect(typeof stated).toBe('string');

    return [...new Set(String(stated).split(', '))];
}

// Only a browser resolves a media query against a real preference and computes a duration from a stylesheet, so this
// is one of the claims `pnpm test` structurally cannot make: jsdom computes no styles and would pass for a client
// carrying no such rule at all.
test('moves the notification centre onto the screen over a duration of its own', async ({ page }) => {
    await openSignedIn(page);
    await openNotifications(page);

    expect(await motionDurations(page)).not.toEqual(['0s']);
});

test('takes that motion away for a reader who asked for less of it, rather than shortening it', async ({ browser, deployment }) => {
    const context = await browser.newContext({ reducedMotion: 'reduce' });

    await deployment.serve(context);

    const page = await context.newPage();

    await openSignedIn(page);
    await openNotifications(page);

    // Removed rather than shortened, which is the accessibility obligation: the panel still arrives where it belongs,
    // and what is gone is the travel. A duration merely made small would read as motion to somebody who asked for none.
    expect(await motionDurations(page)).toEqual(['0s']);
});
