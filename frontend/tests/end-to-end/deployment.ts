// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

// What every spec in this directory needs before it can assert anything: where the deployment is, the credential the
// run provisioned, and the two moves that get a browser from nothing to a space. It is not a spec itself — the
// configuration's `testMatch` picks up `*.spec.ts` alone — so nothing here runs on its own.

/**
 * One value `scripts/run-end-to-end-client.sh` hands over, refused rather than defaulted.
 *
 * The reason is the reason the configuration refuses a missing origin: an empty password reaches the screen as a
 * sign-in that failed, which reads as a defect in the client rather than as a suite nobody handed a deployment to.
 */
export function required(variable: string): string {
    const value = process.env[variable];

    if (value === undefined || value.trim() === '') {
        throw new Error(
            `${variable} is not set. This suite is run by scripts/run-end-to-end-client.sh, which stands the deployment up and hands it over.`,
        );
    }

    return value;
}

/** The credential the run provisioned through the administrative API. */
export const credential = {
    userName: required('MAILFATHOM_CLIENT_USERNAME'),
    password: required('MAILFATHOM_CLIENT_PASSWORD'),
};

/** Signs in with that credential, and waits for the client to have drawn its spaces. */
export async function signIn(page: Page): Promise<void> {
    await page.goto('/app/');

    // The page is served by the deployment it calls, so it asks for no address — the same claim the pull-request suite
    // makes about the web head, made here against a service rather than against a preview server.
    await expect(page.getByRole('textbox', { name: 'Server' })).toHaveCount(0);

    await page.getByRole('textbox', { name: 'Login' }).fill(credential.userName);
    await page.getByLabel('Password', { exact: true }).fill(credential.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
}

/** Signs in and moves to one of the client's spaces, which every spec but the mail one opens with. */
export async function openSpace(page: Page, space: string): Promise<void> {
    await signIn(page);
    await goToSpace(page, space);
}

/**
 * Moves to one of the client's spaces on a page that is already signed in, which is what a reload leaves behind.
 *
 * The space is reached by its own link rather than by its address, because that is the move a person makes and because
 * a fragment typed into the bar would prove the router rather than the navigation.
 */
export async function goToSpace(page: Page, space: string): Promise<void> {
    const navigation = page.getByRole('navigation', { name: 'Spaces' });
    const link = navigation.getByRole('link', { name: space });

    // The narrow composition hides four of the seven spaces behind an overflow. This suite runs at 1280 by 720, where
    // the rail carries all seven, so a link that is not there is a failure rather than a case to handle — asserted
    // here so that it reads as one instead of as a click that timed out.
    await expect(link).toBeVisible();

    await link.click();

    await expect(page.getByRole('main', { name: space })).toBeVisible();
}
