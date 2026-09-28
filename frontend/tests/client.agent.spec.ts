// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheClock, openSignedIn, silentAnswerInterval, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import * as agent from './fixtures/agent';

// Asking the agent, following its answer while it is composed, deciding what it proposes, and putting a conversation
// away and back: each is an act in the thread or the history whose outcome the next read of the conversation, or of
// the history beside it, has to agree with.

const threadOf = (page: Page) => page.getByRole('log', { name: 'Conversation with the agent' });
const conversations = (page: Page) => page.getByRole('listbox', { name: 'Conversations with the agent' });

/** Asks the agent something in a new conversation, which is what every proposal in it waits on. */
async function ask(page: Page, question: string): Promise<void> {
    await page.getByRole('textbox', { name: 'Tell the agent what to do' }).fill(question);
    await page.getByRole('button', { name: 'Send', exact: true }).click();
}

/** The questions the page put to the agent, each as the body it was sent with. */
function asked(deployment: FakeDeployment): unknown[] {
    return deployment.issued
        .filter(({ method, route }) => method === 'POST' && /^\/agent\/conversations\/[^/]+\/messages$/u.test(route))
        .map(({ body }) => body);
}

/** Every decision the page sent about a proposal, as the place it named and what it decided. */
function decided(deployment: FakeDeployment): { at: string; decision: unknown }[] {
    return deployment.issued
        .filter(({ method, route }) => method === 'PUT' && /\/proposals\/\d+$/u.test(route))
        .map(({ route, body }) => ({
            at: route.slice(route.lastIndexOf('/') + 1),
            decision: (body as { decision?: unknown } | null)?.decision,
        }));
}

// Each kind of proposal the client draws, the control that approves it and the one that declines it, and what the
// card says once the conversation's record holds the decision.
const proposals = [
    {
        card: 'Event proposal',
        at: '6',
        approve: /^Add to calendar: “Walk through the four bays”/u,
        decline: 'Decline putting “Walk through the four bays” in the calendar',
        done: 'Added to the calendar',
    },
    {
        card: 'Task proposal',
        at: '7',
        approve: /^Add the task “Confirm the bays in writing”/u,
        decline: 'Decline the task “Confirm the bays in writing”',
        done: 'Added to tasks',
    },
    {
        card: 'Draft',
        at: '8',
        approve: 'Send the draft to bays@example.invalid',
        decline: 'Discard the draft to bays@example.invalid',
        done: 'Sent',
    },
    {
        card: 'Suggested action',
        at: '9',
        approve: 'Do it: Mark a message to come back to',
        decline: 'Decline: Mark a message to come back to',
        done: 'Done',
    },
] as const;

test('follows a question asked of the agent from its status line to the answer, and lists the conversation', async ({
    page,
    deployment,
}) => {
    await page.clock.install();
    await openSignedIn(page, '/#/agent');
    await holdTheClock(page);

    deployment.startNextAnswerRunning();
    await ask(page, 'How many bays were confirmed?');

    // Composing: the question stands in the thread, the agent says what it is doing, and it can be stopped.
    const thread = threadOf(page);

    await expect(thread.getByText('How many bays were confirmed?')).toBeVisible();
    await expect(thread.getByText('Reading the confirmation')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Stop what the agent is doing' })).toBeVisible();
    await expect(conversations(page).getByRole('option', { name: /^How many bays were confirmed\?/u })).toBeVisible();

    expect(asked(deployment)).toStrictEqual([expect.objectContaining({ text: 'How many bays were confirmed?' })]);

    await page.clock.runFor(silentAnswerInterval);

    await expect(thread.getByText('Four bays were confirmed, held to the end of the week.')).toBeVisible();
    await expect(thread.getByText('Reading the confirmation')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Cancel — nothing is running' })).toBeVisible();
});

test('draws the answer’s Markdown as structure and fetches nothing the markup in it names', async ({ page }) => {
    const leftForTheMarkupHost: string[] = [];

    page.on('request', (request) => {
        if (new URL(request.url()).hostname === agent.answerMarkupHost) {
            leftForTheMarkupHost.push(request.url());
        }
    });

    await openSignedIn(page, '/#/agent');
    await conversations(page)
        .getByRole('option', { name: /^How many bays were confirmed/u })
        .click();

    // The innermost of the lists holding the first place, the answer's blocks being a list of their own around it.
    const answer = threadOf(page).getByRole('list').filter({ hasText: agent.answerListed[0] }).last();

    await expect(answer.getByRole('listitem')).toHaveText([...agent.answerListed]);

    // What the sender wrote as markup is read as the text it is, and the picture beside it draws nothing: neither
    // becomes an element that asks a host the reader never chose for anything.
    await expect(threadOf(page).getByText(agent.answerRawMarkup, { exact: false })).toBeVisible();
    await expect(threadOf(page).getByRole('img', { name: 'The bays' })).toHaveCount(0);

    expect(leftForTheMarkupHost).toStrictEqual([]);
});

for (const [act, decision, said] of [
    ['approves', 'accepted', 'done'],
    ['declines', 'declined', 'declined'],
] as const) {
    test(`${act} each kind of proposal, and the conversation still says so when it is opened again`, async ({
        page,
        deployment,
    }) => {
        await openSignedIn(page, '/#/agent');
        await ask(page, 'How many bays were confirmed?');

        const thread = threadOf(page);

        for (const proposal of proposals) {
            const card = thread.getByRole('article', { name: proposal.card, exact: true });

            await card
                .getByRole('button', { name: decision === 'accepted' ? proposal.approve : proposal.decline })
                .click();
            await expect(card.getByText(said, { exact: true })).toBeVisible();
        }

        expect(decided(deployment)).toStrictEqual(proposals.map(({ at }) => ({ at, decision })));

        // Another conversation and back again: what the cards say now is what a fresh read of the record answers.
        await conversations(page)
            .getByRole('option', { name: /^What is waiting on me today/u })
            .click();
        await expect(thread.getByText('What is waiting on me today?')).toBeVisible();
        await conversations(page)
            .getByRole('option', { name: /^How many bays were confirmed\?/u })
            .click();

        for (const proposal of proposals) {
            const card = thread.getByRole('article', { name: proposal.card, exact: true });

            if (decision === 'accepted') {
                await expect(card.getByText(proposal.done, { exact: true })).toBeVisible();
            } else {
                await expect(card.getByText('Declined — nothing was done')).toBeVisible();
            }

            await expect(card.getByRole('button', { name: proposal.decline })).toHaveCount(0);
        }
    });
}

test('archives a conversation and restores it, and the history agrees after each', async ({ page, deployment }) => {
    await openSignedIn(page, '/#/agent');

    const title = /^How many bays were confirmed/u;

    await conversations(page).getByRole('option', { name: title }).press('ContextMenu');
    await page.getByRole('menuitem', { name: 'Archive' }).click();

    await expect(conversations(page).getByRole('option', { name: title })).toHaveCount(0);

    const archive = page.getByRole('button', { name: 'Show the archived conversations' });

    await expect(archive).toHaveText('Archived (2)');
    await archive.click();

    const archived = page.getByRole('listbox', { name: 'Archived conversations' });

    await archived.getByRole('option', { name: title }).press('ContextMenu');
    await page.getByRole('menuitem', { name: 'Restore from archive' }).click();

    await expect(conversations(page).getByRole('option', { name: title })).toBeVisible();
    await expect(archived.getByRole('option', { name: title })).toHaveCount(0);

    const archiveRoute = `/agent/conversations/${agent.answeredConversationId}/archive`;

    expect(deployment.requests('PUT', archiveRoute)).toHaveLength(1);
    expect(deployment.requests('DELETE', archiveRoute)).toHaveLength(1);
});
