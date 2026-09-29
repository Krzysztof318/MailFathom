// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { messageRegion, openSignedIn, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import * as messages from './fixtures/messages';

// Writing mail, as journeys across the reading pane, the composer, the send confirmation, the draft book, and the
// folder list: what the composer opens holding, what leaves the client when it is sent, and what a draft left behind
// reads as once it is opened again from Drafts. Each is asserted by what the screen holds afterwards and by the requests
// the page issued, because a unit test of the composer proves what it does with a value it was handed and nothing about
// what reaches the deployment or what the next screen draws.

const recipient = 'sales@nordwind.example';
const subject = 'Racking for the yard';
const words = 'The racking arrives on the ninth.';

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
    const file = { name: 'yard-plan.txt', mimeType: 'text/plain', buffer: Buffer.from('bay 4, bay 5, bay 6\n') };

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

    expect(staged.map(({ query, body }) => [query.get('fileName'), body])).toStrictEqual([
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
