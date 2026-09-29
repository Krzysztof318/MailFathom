// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { test as base, expect, type Locator, type Page } from '@playwright/test';

import { FakeDeployment } from './fakeDeployment';
import * as deployment from './fixtures/deployment';
import * as messages from './fixtures/messages';

// What every `client.<concern>.spec.ts` shares: the deployment each check signs in to, the way in, and the handful of
// places on the frame a check opens before it asks anything. The browser suite answers what the unit suite
// structurally cannot, asked of the directory of static files `pnpm build` writes, in a browser: the bundle is the
// built one rather than the source, the document is a real one with a history, the window has a width that decides a
// layout, and the requests are the requests a browser actually issued. `frontend/tests/AGENTS.md` is where that
// boundary is decided.

// Read the way `src/Client.App/vite.config.ts` reads it, so an assertion about the version is against the number the
// build substituted rather than against a second copy of it written here.
export const declaredVersion = execFileSync(resolve(import.meta.dirname, '../../scripts/read-declared-version.sh'), {
    encoding: 'utf8',
}).trim();

export const wideWindow = { width: 1280, height: 720 };
export const narrowWindow = { width: 380, height: 720 };

// The design project's phone frame, which is the composition where the list is the whole screen and the one width at
// which the row and the head are drawn differently. It is the project's own number rather than a rounding of the
// breakpoint, so what is measured against it is what the artboard shows.
export const phoneWindow = { width: 390, height: 844 };

// The design project's other three frames, each its own number for the reason the phone's is. The fold and the tablet
// are the two compositions with two panes and a drawer, and the desktop is the artboard's own preview size.
export const foldWindow = { width: 884, height: 832 };
export const tabletWindow = { width: 1024, height: 768 };
export const desktopWindow = { width: 1440, height: 900 };

// The heading the corpus message carries. The sender wrote it as their own first-level heading, and two levels above
// it are already taken — the space's own title and the subject the reading pane draws — so the pane draws it two
// deeper, which is the assertion in the level rather than an accident of the fixture.
export const messageHeading = { name: messages.newsletterHeading, level: 3 } as const;

/** The subject that message carries, which is what names the region the pane draws it in. */
export const messageRegion = { name: messages.newsletterSubject } as const;

/**
 * The runner every browser check is written against, carrying the deployment the check signs in to.
 *
 * The deployment is served to the check's own browser context before the check starts, so every page it opens there —
 * a second tab included — reaches the same one, and it is automatic because no page in this suite may reach the
 * preview server for anything but the bundle. A check that opens a context of its own serves it with
 * `deployment.serve`. A route a check registers on its page afterwards takes precedence over the deployment's, which is
 * how a check puts one refusal or one recording in front of it.
 *
 * A request the deployment has no answer for fails the check that issued it, naming the method and the route, once the
 * check has finished — rather than being answered with an empty body the screen would draw as something else.
 */
export const test = base.extend<{ deployment: FakeDeployment }>({
    deployment: [
        async ({ context }, use) => {
            const served = new FakeDeployment(declaredVersion);

            await served.serve(context);
            await use(served);

            expect(served.unimplemented, 'requests the fake deployment has no answer for').toStrictEqual([]);
        },
        { auto: true },
    ],
});

export async function signIn(page: Page): Promise<void> {
    await page.getByRole('textbox', { name: 'Login' }).fill(deployment.userName);
    await page.getByLabel('Password', { exact: true }).fill(deployment.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
}

/**
 * How long the client waits before reading a silent answer again: `silentRunPollInterval` in `Client.Backend`'s
 * `runFollowing.ts`, which this suite resolves neither package to import. The fake deployment serves no signal channel,
 * so this interval is what moves an answer the fake handed back running on to its next read.
 */
export const silentAnswerInterval = 3_000;

/**
 * How long a visible client goes before reading again everything it draws: `refreshInterval` in `Client.App`'s
 * `signals/useSignals.ts`. The fake deployment serves no signal channel, so this interval is how the client learns what
 * an act moved that it does not draw by itself — the counts in the folder column among it.
 */
export const refreshInterval = 5 * 60_000;

/**
 * Stops the page's clock where it stands, so a timer the client runs fires only when the check advances it.
 *
 * The clock is installed before the page loads and runs as a real one until here, because signing in and drawing the
 * frame have timers of their own that nothing is proving. Held from here, a running answer reads as running for as long
 * as the check is looking at it, however loaded the machine is, and reads again only on `page.clock.runFor`.
 */
export async function holdTheClock(page: Page): Promise<void> {
    await page.clock.pauseAt(Date.now() + 1_000);
}

/** The client opened at an address and signed in, which is where every test about the frame starts. */
export async function openSignedIn(page: Page, address = '/'): Promise<void> {
    await page.goto(address);
    await signIn(page);
}

/** The account menu at the foot of the navigation opened, which is where the preferences and signing out live. */
export async function openAccountMenu(page: Page): Promise<void> {
    const control = page.getByRole('button', { name: 'Account and preferences' });

    // The rail carries the control itself. The bottom bar has five places and spends them on three spaces, the bell,
    // and the overflow, so in a narrow window what is about the person stands one press further in — which is where
    // the design project puts it and what this helper has to know to reach it at either width.
    if (!(await control.isVisible())) {
        await page.getByRole('button', { name: 'More' }).click();
    }

    await control.click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
}

/** The settings screen opened from the row the account menu carries, which is the only way in. */
export async function openSettings(page: Page): Promise<void> {
    await openAccountMenu(page);
    await page.getByRole('button', { name: 'Settings', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Settings' })).toBeVisible();
}

/** The settings surface opened on its second tab, which is where everything about the client rather than the person is. */
export async function openApplicationSettings(page: Page): Promise<void> {
    await openSettings(page);
    await page.getByRole('tab', { name: 'Application' }).click();
}

// Opening a message is an act rather than a state the client lands in: the reading pane draws what a row of the list
// was opened, so every check about it starts by opening one.
export async function openTheFirstMessage(page: Page): Promise<void> {
    await openSignedIn(page, '/#/mail');

    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
}

/** The three grants the acts on a mailbox are reached under, which the corpus credential does not carry. */
export const actingGrants = ['mailfathom.mail.flags.write', 'mailfathom.mail.move', 'mailfathom.mail.delete'] as const;

/** One row of the list, found by the subject it ends on, which every generated corpus row numbers. */
export function row(page: Page, subject: string): Locator {
    return page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(`\\b${subject}$`, 'u') });
}

/** One folder of the tree, by the name it is drawn with — the inbox's carrying its count. */
export function folder(page: Page, name: string): Locator {
    return page.getByRole('tree', { name: 'Mailboxes and folders' }).getByRole('treeitem', { name, exact: true });
}

/**
 * The folder the archive act files into, opened from the tree.
 *
 * The tree names it by the role it plays, which is the name the level `Archive / 2024` sits under too — so it is
 * reached the way a keyboard reaches it, one row up from the trash, rather than by a name two rows answer to.
 */
export async function openTheArchive(page: Page): Promise<void> {
    await folder(page, 'Trash').click();
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Enter');

    await expect(
        page
            .getByRole('tree', { name: 'Mailboxes and folders' })
            .getByRole('treeitem', { name: 'Archive', selected: true }),
    ).toBeVisible();
}

/** The notice an act raised, found by what it says it did. */
export function notice(page: Page, title: string): Locator {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({ hasText: title });
}

/** What the client sent to one write route, in the order it sent it. */
export function bodiesOf(deployment: FakeDeployment, route: string): unknown[] {
    return deployment.requests('POST', route).map(({ body }) => body);
}

/**
 * The inbox opened with the acts granted and the clock held, so the notice offering the way back stays for as long as
 * the check is reading it rather than for as long as a loaded machine lets it.
 */
export async function openTheInbox(page: Page, deployment: FakeDeployment): Promise<void> {
    deployment.grant(...actingGrants);

    await page.clock.install();
    await openSignedIn(page, '/#/mail');
    await expect(row(page, 'Message 0')).toBeVisible();
    await holdTheClock(page);
}

/** Asks the row's own menu for an act, which is how one message is acted on without opening it. */
export async function fromTheMenu(page: Page, subject: string, act: string): Promise<void> {
    await row(page, subject).click({ button: 'right' });
    await page.getByRole('menuitem', { name: act, exact: true }).click();
}
