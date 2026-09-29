// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { holdTheClock, messageRegion, openSignedIn, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import * as deploymentCorpus from './fixtures/deployment';
import * as drafts from './fixtures/drafts';
import * as messages from './fixtures/messages';

// Writing mail, as journeys across the reading pane, the composer, the send confirmation, the draft book, and the
// folder list: what the composer opens holding, what leaves the client when it is sent, and what a draft left behind
// reads as once it is opened again from Drafts. Each is asserted by what the screen holds afterwards and by the requests
// the page issued, because a unit test of the composer proves what it does with a value it was handed and nothing about
// what reaches the deployment or what the next screen draws.
//
// The second half is every way writing does not go as planned: a send the deployment refuses, a send stopped on its
// way, a message given up or kept, a machine that went offline or reloaded, and a sign-in that ended while somebody was
// writing. What each of those can lose is what somebody wrote, and where it went is only visible on the next screen.

const recipient = 'sales@nordwind.example';
const subject = 'Racking for the yard';
const words = 'The racking arrives on the ninth.';
const file = { name: 'yard-plan.txt', mimeType: 'text/plain', buffer: Buffer.from('bay 4, bay 5, bay 6\n') };

/** Signs in to Mail as a credential that may write and send mail, which is what the composer is drawn for. */
async function openMailWriting(page: Page, deployment: FakeDeployment): Promise<void> {
    deployment.grant('mailfathom.mail.drafts.write', 'mailfathom.mail.send');

    await openSignedIn(page, '/#/mail');
}

/** Writes an address into one header and commits it, the way a person finishes typing one. */
async function address(page: Page, header: 'To' | 'Cc' | 'Bcc', written: string): Promise<void> {
    await page.getByRole('combobox', { name: header, exact: true }).fill(written);
    await page.getByRole('combobox', { name: header, exact: true }).press('Enter');
    await expect(page.getByRole('button', { name: `Remove ${written} from ${header}` })).toBeVisible();
}

/** Writes the words of a message into its body. */
async function write(page: Page, written: string): Promise<void> {
    await page.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.type(written);
}

/** Presses send and confirms it, which is the whole of sending: the composer closes and a toast says what became of it. */
async function send(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    await expect(sent(page)).toBeVisible();
}

/** The notice a send the deployment queued leaves standing over whatever the person turned to next. */
function sent(page: Page) {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({ hasText: 'Queued to go out.' });
}

/** The one draft route a check expects a request on, read off the order the page wrote them in. */
function draftRoutesOf(deployment: FakeDeployment, method: string, suffix: string): string[] {
    return deployment.issued
        .filter(
            (issued) =>
                issued.method === method && issued.route.startsWith('/drafts/') && issued.route.endsWith(suffix),
        )
        .map(({ route }) => route);
}

test('sends a new message carrying exactly the recipient, the subject, and the words typed into it', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);
    await send(page);

    await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        {
            plainTextBody: words,
            htmlBody: words,
            to: [recipient],
            cc: [],
            bcc: [],
            account: 'work',
            subject,
        },
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
});

// Each way of answering opens where the reader stands — the mailbox and the list beside it, at the same address — and
// is addressed the way the client promises: the author, everybody, or nobody yet. What it answers travels as the
// message's identity rather than as quoted text, because the deployment derives the subject, the threading, and what a
// forward carries from the message itself.
for (const answer of [
    { pressed: 'Reply', titled: 'Reply', answers: 'senderOnly', to: ['news@example.invalid'], cc: [], prefix: 'Re' },
    {
        pressed: 'Reply all',
        titled: 'Reply to everyone',
        answers: 'everyone',
        to: ['news@example.invalid'],
        cc: ['reader@example.invalid'],
        prefix: 'Re',
    },
    { pressed: 'Forward', titled: 'Forward', answers: 'forward', to: [], cc: [], prefix: 'Fwd' },
] as const) {
    test(`opens ${answer.pressed.toLowerCase()} beside the message it answers, addressed as it promises, and sends it as an answer`, async ({
        page,
        deployment,
    }) => {
        await openMailWriting(page, deployment);
        await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
        await expect(page.getByRole('article', messageRegion)).toBeVisible();
        await page
            .getByRole('toolbar', { name: 'Mail actions' })
            .getByRole('button', { name: answer.pressed, exact: true })
            .click();

        const composer = page.getByRole('region', { name: answer.titled });

        await expect(composer.getByLabel('Subject')).toContainText(`${answer.prefix}: ${messages.newsletterSubject}`);
        await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
        await expect(page).toHaveURL(/#\/mail$/u);

        for (const addressed of answer.to) {
            await expect(composer.getByRole('button', { name: `Remove ${addressed} from To` })).toBeVisible();
        }

        for (const copied of answer.cc) {
            await expect(composer.getByRole('button', { name: `Remove ${copied} from Cc` })).toBeVisible();
        }

        if (answer.to.length === 0) {
            await address(page, 'To', recipient);
        }

        await write(page, words);
        await send(page);

        expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
            {
                plainTextBody: words,
                htmlBody: words,
                to: answer.to.length === 0 ? [recipient] : answer.to,
                cc: answer.cc,
                bcc: [],
                answeredEmailId: 'message-0',
                answers: answer.answers,
            },
        ]);
        expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
    });
}

test('shows the copy headers, keeps what they hold while hidden, and sends nothing from them once they are hidden', async ({
    page,
    deployment,
}) => {
    const copied = 'yard@example.invalid';
    const blind = 'ledger@example.invalid';

    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();
    await address(page, 'Cc', copied);
    await address(page, 'Bcc', blind);

    // Hidden, the headers are gone from the screen and from what the confirmation says the message goes to.
    await page.getByRole('button', { name: 'Hide CC and BCC fields' }).click();
    await expect(page.getByRole('combobox', { name: 'Cc', exact: true })).toHaveCount(0);

    // Shown again, they hold what was written in them: hiding them is not giving the addresses up.
    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();
    await expect(page.getByRole('button', { name: `Remove ${copied} from Cc` })).toBeVisible();
    await expect(page.getByRole('button', { name: `Remove ${blind} from Bcc` })).toBeVisible();

    await page.getByRole('button', { name: 'Hide CC and BCC fields' }).click();
    await page.getByRole('button', { name: 'Send', exact: true }).click();

    const confirmation = page.getByRole('dialog', { name: 'Send this message?' });

    await expect(confirmation.getByText(`To ${recipient}`)).toBeVisible();
    await expect(confirmation.getByText(/^Copy to|^Blind copy to/u)).toHaveCount(0);

    await confirmation.getByRole('button', { name: /^Send/u }).click();
    await expect(sent(page)).toBeVisible();

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ to: [recipient], cc: [], bcc: [] }),
    ]);
});

test('keeps a draft with its file when the composer is left, lists it in Drafts, and sends it whole from there', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    const chosen = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Attach' }).click();
    await (await chosen).setFiles(file);

    await expect(page.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    // Leaving the composer is asked about, and keeping what was written files it with the file it carries.
    await page.getByRole('button', { name: 'Close the message' }).click();
    await page
        .getByRole('dialog', { name: 'Discard this message?' })
        .getByRole('button', { name: 'Save draft' })
        .click();
    await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);

    await page
        .getByRole('tree', { name: 'Mailboxes and folders' })
        .getByRole('treeitem', { name: /^Drafts/u })
        .click();
    await page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(subject, 'u') })
        .click();

    // Opened again from Drafts, it holds everything that was written and attached.
    const draft = page.getByRole('region', { name: 'Draft' });

    await expect(draft.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(draft.getByLabel('Subject')).toHaveValue(subject);
    await expect(draft.getByRole('textbox', { name: 'Message' })).toHaveText(words);
    await expect(draft.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    await send(page);

    // The words come back from the deployment as its own paragraphs, so the markup is the one part allowed to differ.
    const written = { plainTextBody: words, to: [recipient], cc: [], bcc: [], account: 'work', subject };

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        { ...written, htmlBody: words },
        { ...written, htmlBody: `<p>${words}</p>` },
    ]);

    // The file went up once for the draft that was kept and once for the one that was sent, the same octets both times,
    // and the send named the draft the second of them was staged against.
    const staged = deployment.issued.filter(
        ({ method, route }) => method === 'POST' && route.startsWith('/drafts/') && route.endsWith('/attachments'),
    );

    expect(staged.map(({ query, text }) => [query.get('fileName'), text])).toStrictEqual([
        [file.name, file.buffer.toString()],
        [file.name, file.buffer.toString()],
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toStrictEqual([
        staged[1]?.route.replace(/\/attachments$/u, '/send'),
    ]);
});

// What the stylesheet's own focus ring would have drawn is an outline on the focused element; a field drawn without one
// has to be lit by the row around it instead. Asked as an expression rather than as a closure, for the reason
// `client.layout.spec.ts` gives: this suite is compiled without a DOM declaration on purpose.
const focusIsDrawn = `(() => {
    const drawn = (element) => {
        const style = getComputedStyle(element);
        return style.outlineStyle !== 'none' || style.boxShadow !== 'none';
    };
    return drawn(document.activeElement) || drawn(document.activeElement.parentElement);
})()`;

test('draws a visible focus on every field of the composer reached by the keyboard', async ({ page, deployment }) => {
    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();
    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();

    for (const field of [
        page.getByRole('combobox', { name: 'To', exact: true }),
        page.getByRole('combobox', { name: 'Cc', exact: true }),
        page.getByRole('combobox', { name: 'Bcc', exact: true }),
        page.getByLabel('Subject'),
        page.getByRole('textbox', { name: 'Message' }),
    ]) {
        // Reached from the control before it, which is how a keyboard arrives rather than how a script places focus.
        await field.focus();
        await page.keyboard.press('Shift+Tab');
        await page.keyboard.press('Tab');
        await expect(field).toBeFocused();

        expect(await page.evaluate<boolean>(focusIsDrawn)).toBe(true);
    }
});

/** Opens a new message and writes all of it: the recipient, the subject, the words, and a file. */
async function writeAll(page: Page): Promise<Locator> {
    await page.getByTitle('New message').click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    const chosen = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Attach' }).click();
    await (await chosen).setFiles(file);

    const composer = page.getByRole('region', { name: 'New message' });

    await expect(composer.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    return composer;
}

/** That a composer holds everything `writeAll` wrote into it, with the words it is expected to carry by now. */
async function holdsEverything(composer: Locator, written = words): Promise<void> {
    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText(written);
    await expect(composer.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();
}

/** Opens Drafts in the folder column. */
async function openDrafts(page: Page): Promise<void> {
    await page
        .getByRole('tree', { name: 'Mailboxes and folders' })
        .getByRole('treeitem', { name: /^Drafts/u })
        .click();
}

/** The row Drafts lists for the message these checks write. */
function draftRow(page: Page): Locator {
    return page.getByRole('listbox', { name: 'Messages' }).getByRole('option', { name: new RegExp(subject, 'u') });
}

/** Every notice standing, which is where what became of a send is said once the composer has closed. */
function notices(page: Page): Locator {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem');
}

// Screening refusing what the corpus refusal names, which is what changing the words is the answer to.
const refusedContent =
    'Screening refused what this message carries. Changing what it says, or what it attaches, is what would change that.';

test('keeps everything written in the composer when the deployment refuses the send, and sends the corrected message as the same draft', async ({
    page,
    deployment,
}) => {
    const corrected = 'The racking arrives on the tenth.';
    const refused: string[] = [];

    await page.clock.install();
    await openMailWriting(page, deployment);
    await page.route(
        '**/api/client/drafts/*/send',
        (route) => {
            refused.push(new URL(route.request().url()).pathname);

            return route.fulfill({
                status: 409,
                contentType: 'application/problem+json',
                body: JSON.stringify(drafts.refusedSend),
            });
        },
        { times: 1 },
    );

    const composer = await writeAll(page);

    await holdTheClock(page);
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    // Said in the notice for somebody who looked away, and beside the words it refused, which are all still there.
    await expect(notices(page).filter({ hasText: 'Message not sent' })).toContainText(refusedContent);
    await expect(composer.getByText(refusedContent)).toBeVisible();
    await holdsEverything(composer);

    await composer.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.type(corrected);
    await send(page);

    await expect(composer).toHaveCount(0);

    // One draft, filed once with its file and revised with the correction, is what went out — the refused send and the
    // one that followed it both named it, and nothing was filed or attached a second time.
    const [revised = ''] = draftRoutesOf(deployment, 'PUT', '');

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: words, subject, to: [recipient] }),
    ]);
    expect(deployment.requests('PUT', revised).map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: corrected, subject, to: [recipient] }),
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/attachments')).toStrictEqual([`${revised}/attachments`]);
    expect(refused).toStrictEqual([`/api/client${revised}/send`]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toStrictEqual([`${revised}/send`]);
});

// Taking a send back is the stop its notice carries, and when it lands decides nothing about where the message goes:
// asked while the deployment is still filing it, the stop waits for the send and withdraws it; asked while the
// deployment is queueing it, it withdraws what was queued. Either way the message is back in Drafts, whole, and the
// notices say so — which is the seam no unit test reaches, the stop being the toast's and the folder the list's.
for (const held of [
    { while: 'filing it', route: '**/api/client/drafts' },
    { while: 'queueing it', route: '**/api/client/drafts/*/send' },
]) {
    test(`takes a send stopped while the deployment is ${held.while} back into Drafts, whole, and says that is where it is`, async ({
        page,
        deployment,
    }) => {
        let release: () => void = () => undefined;
        const released = new Promise<void>((resolve) => {
            release = resolve;
        });

        let holding = true;

        await page.clock.install();
        await openMailWriting(page, deployment);
        // Held by a flag rather than by `times: 1`: a route that expires is removed the moment its handler starts, and
        // Playwright hands a request whose handler was removed straight on to the next one — so the deployment would
        // answer at once, and the stop would land on a send it had already queued.
        await page.route(held.route, async (route) => {
            if (holding && route.request().method() === 'POST') {
                holding = false;
                await released;
            }

            await route.fallback();
        });

        await writeAll(page);
        await holdTheClock(page);
        await page.getByRole('button', { name: 'Send', exact: true }).click();
        await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

        await notices(page)
            .filter({ hasText: 'Sending your message…' })
            .getByRole('button', { name: 'Stop the operation' })
            .click();
        await page
            .getByRole('dialog', { name: 'Stop the operation?' })
            .getByRole('button', { name: 'Stop the operation' })
            .click();
        release();

        await expect(notices(page).filter({ hasText: 'Message taken back' })).toContainText(
            'Taken back before it went out.',
        );
        await expect(notices(page).filter({ hasText: 'Stopped' })).toContainText(
            'take the message back into your own drafts',
        );
        await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);
        expect(deployment.requests('POST', '/outbox/cancellation')).toHaveLength(1);

        await openDrafts(page);
        await draftRow(page).click();

        await holdsEverything(page.getByRole('region', { name: 'Draft' }));
    });
}

test('keeps everything when the composer is left for writing again, and takes the filed draft out of Drafts for good on Discard', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);

    const composer = await writeAll(page);

    await composer.getByRole('button', { name: 'Save draft' }).click();
    await expect(composer.getByText('Draft filed in your own drafts.')).toBeVisible();
    await openDrafts(page);
    await expect(draftRow(page)).toBeVisible();

    const asked = page.getByRole('dialog', { name: 'Discard this message?' });

    await composer.getByRole('button', { name: 'Close the message' }).click();
    await asked.getByRole('button', { name: 'Back to writing' }).click();
    await holdsEverything(composer);

    await composer.getByRole('button', { name: 'Close the message' }).click();
    await asked.getByRole('button', { name: 'Discard', exact: true }).click();
    await expect(composer).toHaveCount(0);
    // The dialog and the composer close in two renders, and each gives back the history entry it stood on: the client
    // is still what the tab shows, rather than whatever the tab showed before it.
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    expect(new URL(page.url()).hash).toBe('#/mail');

    const [staged = ''] = draftRoutesOf(deployment, 'POST', '/attachments');

    expect(draftRoutesOf(deployment, 'DELETE', '')).toStrictEqual([staged.replace(/\/attachments$/u, '')]);

    await page.reload();
    await openDrafts(page);

    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();
    await expect(draftRow(page)).toHaveCount(0);
});

test('keeps what is being written across a reload and while offline, and sends all of it once the network is back', async ({
    page,
    deployment,
    context,
}) => {
    const writtenOffline = ' Written with no network.';

    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await page.getByTitle('New message').click();

    const composer = page.getByRole('region', { name: 'New message' });
    const message = composer.getByRole('textbox', { name: 'Message' });

    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(message).toHaveText(words);

    await context.setOffline(true);
    await expect(composer.getByText(/^This machine is offline\./u)).toBeVisible();
    await message.click();
    await page.keyboard.press('ControlOrMeta+End');
    await page.keyboard.type(writtenOffline);
    await expect(composer.getByRole('button', { name: 'Send', exact: true })).toBeDisabled();

    await context.setOffline(false);
    await expect(composer.getByRole('button', { name: 'Send', exact: true })).toBeEnabled();
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: `${words}${writtenOffline}`, subject, to: [recipient] }),
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
});

/**
 * Writes a message, has the deployment stop accepting the sign-in at the next read of a folder, and signs in again as
 * the login named — which is where what was written either comes back or does not.
 */
async function turnedAwayWhileWriting(page: Page, deployment: FakeDeployment, login: string): Promise<Locator> {
    await openMailWriting(page, deployment);
    await page.getByTitle('New message').click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.route('**/api/client/emails?*', (route) => route.fulfill({ status: 401 }), { times: 1 });
    await openDrafts(page);
    await expect(
        page.getByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.'),
    ).toBeVisible();

    await page.getByRole('textbox', { name: 'Login' }).fill(login);
    await page.getByLabel('Password', { exact: true }).fill(deploymentCorpus.password);
    await page.getByRole('button', { name: 'Connect' }).click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

    return page.getByRole('region', { name: 'New message' });
}

// A sign-in the deployment ends while somebody is writing ends the frame, and what they wrote is theirs rather than
// the machine's: the same person signing in again finds the composer as they left it, and anybody else finds nothing
// of it — which is the seam between the frame that ends a sign-in and the composer that keeps a composition.
test('keeps what was being written when the deployment stops accepting the sign-in, for the person who wrote it', async ({
    page,
    deployment,
}) => {
    const composer = await turnedAwayWhileWriting(page, deployment, deploymentCorpus.userName);

    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText(words);
});

test('offers nothing of what was being written to anybody else who signs in after the deployment ended the sign-in', async ({
    page,
    deployment,
}) => {
    const composer = await turnedAwayWhileWriting(page, deployment, 'somebody.else');

    await expect(composer).toHaveCount(0);

    await page.getByTitle('New message').click();

    await expect(composer.getByLabel('Subject')).toHaveValue('');
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText('');
});
