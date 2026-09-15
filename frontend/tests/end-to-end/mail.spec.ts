// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test, type Page } from '@playwright/test';

// The path a person takes, against a MailFathom that was stood up the way an operator stands one up: signing in, the
// list of what it synchronized, one message, the conversation it belongs to, a search over the mailbox, and signing
// out. `scripts/run-end-to-end-client.sh` is what puts that deployment together and what runs this;
// `frontend/tests/AGENTS.md` § *The end-to-end suite* is where this suite's boundary is drawn.
//
// Nothing here routes a request. The bundle the deployment serves reaches the surface that deployment serves, over the
// same origin, and every answer comes from mail that was delivered to a mail server and synchronized out of it — which
// is the whole of what neither committed suite can claim.
//
// What it asserts is therefore structural rather than literal, and deliberately: the corpus was generated, so no
// subject, sender, or sentence in it is a value to write down here. What is written down is what the client itself
// says — the roles it publishes and the words it draws — plus what the deployment must have produced for those to
// mean anything: a folder with mail in it, a message that opens, a conversation, and a search that finds the message
// it was asked about.

// The credential `scripts/run-end-to-end-client.sh` provisioned through the administrative API, refused rather than
// defaulted for the reason the configuration refuses a missing origin: an empty password reaches the screen as a
// sign-in that failed, which reads as a defect in the client rather than as a suite nobody handed a deployment to.
function required(variable: string): string {
    const value = process.env[variable];

    if (value === undefined || value.trim() === '') {
        throw new Error(
            `${variable} is not set. This suite is run by scripts/run-end-to-end-client.sh, which provisions the credential and hands it over.`,
        );
    }

    return value;
}

const credential = {
    userName: required('MAILFATHOM_CLIENT_USERNAME'),
    password: required('MAILFATHOM_CLIENT_PASSWORD'),
};

/** Signs in with the credential the run provisioned, and waits for the client to have drawn its spaces. */
async function signIn(page: Page): Promise<void> {
    await page.goto('/app/');

    // The page is served by the deployment it calls, so it asks for no address — the same claim the pull-request suite
    // makes about the web head, made here against a service rather than against a preview server.
    await expect(page.getByRole('textbox', { name: 'Server' })).toHaveCount(0);

    await page.getByRole('textbox', { name: 'Login' }).fill(credential.userName);
    await page.getByLabel('Password', { exact: true }).fill(credential.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
}

/** Reaches the mail space and waits for the folder to have drawn at least one message. */
async function openTheMailbox(page: Page): Promise<void> {
    await page.getByRole('link', { name: 'Mail' }).click();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();
}

/**
 * Opens the first message the list drew and answers with the subject the reading pane names it by.
 *
 * The subject is read off the screen rather than written here for the reason the file's own note gives: what the
 * corpus says was decided when it was generated, and a spec restating one of its subjects would be asserting against
 * the archive rather than against the deployment.
 */
async function openTheFirstMessage(page: Page): Promise<string> {
    const list = page.getByRole('listbox', { name: 'Messages' });

    await list.getByRole('option').first().click();

    const subject = page.getByRole('heading', { level: 2 }).first();

    await expect(subject).toBeVisible();

    const drawn = await subject.textContent();

    expect(drawn?.trim()).toBeTruthy();

    return (drawn ?? '').trim();
}

/** The longest word of a subject, which is what a person looking for that message would type. */
function longestWordOf(subject: string): string {
    const words = subject.split(/[^\p{Letter}]+/u).filter((word) => word.length >= 4);

    expect(words, `'${subject}' carries no word long enough to search for.`).not.toHaveLength(0);

    return words.reduce((longest, word) => (word.length > longest.length ? word : longest));
}

test('signs in against the deployment and draws the mail it synchronized', async ({ page }) => {
    await signIn(page);
    await openTheMailbox(page);

    // Mail that arrived at a mail server, was synchronized out of it, and is being listed from this deployment's own
    // copy. Any count above zero establishes that, and a fixed one would be an assertion about the corpus.
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();
});

test('opens a message from the list and draws what the sender wrote', async ({ page }) => {
    await signIn(page);
    await openTheMailbox(page);

    await openTheFirstMessage(page);

    // The region the words are drawn in, whichever surface the row opened: a message the deployment threaded with
    // nothing opens as itself and names its region by the subject, and one it threaded opens as the whole
    // correspondence, whose regions are named by who wrote each message. The subject stands in the head over both, and
    // `openTheFirstMessage` has already read it there. What is asserted here is the one thing neither shape changes —
    // that a region holding the body the deployment derived from the delivered MIME was drawn at all.
    await expect(page.getByRole('article').first()).toBeVisible();
});

test('opens the whole conversation a message belongs to', async ({ page }) => {
    await signIn(page);
    await openTheMailbox(page);

    // The corpus is exchanges rather than a flat batch, so a row the deployment threaded says how long its
    // correspondence is. Its absence is a real failure rather than a spec to skip: it would mean this deployment
    // threaded nothing. The first row is not asked for here, because which message a folder sorts first says nothing
    // about whether it belongs to an exchange.
    const exchange = page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option')
        .filter({ hasText: /messages in this conversation/ })
        .first();

    await expect(exchange).toBeVisible();

    // Opening it is the whole path: a threaded row opens the correspondence rather than the message, so there is no
    // second control to reach for.
    await exchange.click();

    const conversation = page.getByRole('region', { name: 'Conversation' });

    await expect(conversation).toBeVisible();

    // A second message drawn beside the one that was clicked is what makes this the conversation rather than the
    // message, which is the claim the row's own count made.
    await expect(conversation.getByRole('article').nth(1)).toBeVisible();
});

test('searches the mailbox and finds the message it was asked about', async ({ page }) => {
    await signIn(page);
    await openTheMailbox(page);

    const subject = await openTheFirstMessage(page);

    await page.getByRole('searchbox', { name: 'Find a message' }).fill(longestWordOf(subject));
    await page.getByRole('button', { name: 'Search' }).click();

    // Ranked against the query by the deployment's own index rather than filtered in the page, which is why the
    // assertion is that the message asked about is among what came back. Among, and not alone: the corpus is
    // exchanges, so every turn of one conversation carries that subject and several rows match — which is a correct
    // answer rather than an ambiguous locator.
    await expect(
        page.getByRole('listbox', { name: 'What this search found' }).getByRole('option', { name: subject }).first(),
    ).toBeVisible();
});

test('signs out, and what it drew is gone', async ({ page }) => {
    await signIn(page);
    await openTheMailbox(page);

    await page.getByRole('button', { name: 'Account and preferences' }).click();
    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);

    // A reload is what proves the credential is gone from where the head keeps it rather than only from the screen.
    await page.reload();

    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
});
