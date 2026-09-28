// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { messageRegion, openSignedIn, openTheFirstMessage, test } from './client.harness';

// A render failure in the built bundle: what it costs the region it happened in, the way back into that region, and
// what the document says when nothing of the client is left to say it.

// A failure the pane cannot survive, induced by refusing the element every reading surface is built around: the frame
// draws no `article` at all, so this reaches the region under test and nothing else. It is the one way to produce an
// unexpected throw in the built bundle without the client carrying a seam for it, and what is being proven is what
// only a browser can say — that a real render failure in the real bundle costs the pane rather than the document.
// Written as an expression rather than as a closure, for the reason the overflow check above gives: this suite is
// compiled without a DOM declaration on purpose, so a function naming `document` would be the one thing that changes.
const refuseTheElementAMessageIsDrawnAs = `
    (() => {
        const create = document.createElement.bind(document);

        window.drawEverythingAgain = () => {
            document.createElement = create;
        };

        document.createElement = (name, options) => {
            if (name === 'article') {
                throw new TypeError('This message cannot be drawn.');
            }

            return create(name, options);
        };
    })()
`;

test('keeps the frame usable when the reading pane throws while it is being drawn', async ({ page }) => {
    await openTheFirstMessage(page);
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    await page.evaluate(refuseTheElementAMessageIsDrawnAs);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();

    // The pane says what happened rather than the document going blank, and everything the frame holds is still there
    // to be used: the list that opened it, the mailboxes beside it, and the way to another space.
    await expect(page.getByRole('alert')).toContainText('This part of MailFathom stopped working.');
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();

    await page.getByRole('link', { name: 'Discover' }).click();

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

// The way out the pane offers, taken after whatever made it fail has stopped: the region is drawn again, and the
// keyboard goes into it rather than being left on a control that has gone with the surface it stood on. It is here
// rather than in the unit suite because jsdom answers the question wrongly — the wrapper the boundary holds draws no
// box, which is a browser's reason for refusing it focus and not something jsdom models.
test('draws the region again when it is retried, and hands the keyboard into it', async ({ page }) => {
    await openTheFirstMessage(page);
    await page.evaluate(refuseTheElementAMessageIsDrawnAs);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();
    await expect(page.getByRole('alert')).toBeVisible();

    await page.evaluate('window.drawEverythingAgain()');
    await page.getByRole('button', { name: 'Try again' }).click();

    const message = page.getByRole('article', messageRegion);

    await expect(message).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    // Somebody rather than nobody holds the keyboard, and the next tab stop says where they are: the first control
    // the region draws, which for a message belonging to no conversation is replying from the head of the message —
    // there is no thread to hand the agent, so no way to the agent is drawn before it. Both are asked in a browser
    // rather than in jsdom, which draws no boxes and so does not model the element a browser refuses focus to — the
    // wrapper this lands on was one until this change. Looked for inside the message because the head's acts share
    // their names with the toolbar's, as the design draws them.
    expect(await page.evaluate('document.activeElement !== document.body')).toBe(true);

    await page.keyboard.press('Tab');

    await expect(message.getByRole('button', { name: /^Reply/u })).toBeFocused();
});

// The failure no boundary is left to contain, induced by refusing the element every surface in this client is built
// out of: the region fails, the boundary around it fails drawing what it says instead, and so does the last-resort
// boundary above it — which is what leaves React with nothing to render and unmounts the root. Only a browser can say
// what is left then, because what stands there is markup the document itself carries rather than anything the bundle
// renders, and `main.tsx` wiring `onUncaughtError` to it is not a claim jsdom can make about the built bundle.
const refuseTheElementEverySurfaceIsBuiltFrom = `
    (() => {
        const create = document.createElement.bind(document);

        document.createElement = (name, options) => {
            if (name === 'div') {
                throw new TypeError('Nothing in this client can be drawn.');
            }

            return create(name, options);
        };
    })()
`;

test('says so in the document itself when a failure escapes every boundary the client has', async ({ page }) => {
    await openSignedIn(page, '/#/mail');
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

    await page.evaluate(refuseTheElementEverySurfaceIsBuiltFrom);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').nth(1).click();

    // Nothing of the client is left, so what a reader is owed is a document that says what happened rather than an
    // empty one — announced, and holding the keyboard, whatever it was on having gone with the tree.
    const carried = page.getByRole('alert');

    await expect(carried).toContainText('MailFathom stopped');
    await expect(carried).toBeFocused();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toHaveCount(0);
});
