// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect } from '@playwright/test';

import { conversationId } from './fixtures/mail';
import { openSignedIn, test } from './client.harness';

// A conversation opened from the list lands on the message the row stood for and draws that one alone, with the rest of
// the correspondence one press away and the arrival marked once there is something to tell it apart from. What the
// pane holds is read against the conversation the page asked for, so the words of every message drawn are the words
// that came back rather than a preview the list already had.

const correspondence = [
    { from: 'Nordwind', words: 'Two prices, one for the shallow bays and one for the deep ones.' },
    { from: 'Iris Marlow', words: 'Does the deeper price include the fixings?' },
    { from: 'Nordwind', words: 'It does, and the delivery is inside the same figure.' },
    { from: 'Iris Marlow', words: 'Then take the deeper bays. I will confirm the dates on Monday.' },
    { from: 'Nordwind', words: 'Booked for the ninth. The crew will need the yard for a morning.' },
];

test('opens a conversation at the message the list opened, and shows and hides the rest of it with each message drawn', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page, '/#/mail');
    await page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: /The racking quote/u })
        .click();

    const conversation = page.getByRole('region', { name: 'Conversation' });
    const messages = conversation.getByRole('article', { name: /^Message from/u });
    const [opened] = correspondence;

    await expect(conversation.getByRole('heading', { name: 'The racking quote' })).toBeVisible();
    await expect(messages).toHaveCount(1);
    await expect(messages.first()).toContainText(opened?.words ?? '');
    await expect(conversation.getByText('Opened from the list')).toHaveCount(0);
    await expect(
        conversation.getByRole('region', { name: 'Where this conversation stands' }).getByRole('listitem'),
    ).toHaveCount(4);

    await conversation.getByRole('button', { name: 'Show all the messages' }).click();

    await expect(messages).toHaveCount(correspondence.length);

    for (const [position, { from, words }] of correspondence.entries()) {
        await expect(messages.nth(position)).toHaveAccessibleName(`Message from ${from}`);
        await expect(messages.nth(position)).toContainText(words);
    }

    await expect(messages.first()).toContainText('Opened from the list');
    await expect(conversation.getByRole('button', { name: 'Hide the other messages' })).toHaveAttribute(
        'aria-expanded',
        'true',
    );

    await conversation.getByRole('button', { name: 'Hide the other messages' }).click();

    await expect(messages).toHaveCount(1);
    await expect(messages.first()).toContainText(opened?.words ?? '');
    expect(deployment.requests('GET', `/threads/${conversationId}`).map(({ query }) => query.get('content'))).toContain(
        'true',
    );
});
