// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailAccount, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { everything, type MailScope } from '../workspace/mailScope';
import { WorkspaceProvider } from '../workspace/Workspace';
import { MailSearch } from './MailSearch';
import { resultsPerPage } from './searchAsk';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const work: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const result = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'Invoice 4471',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'accounts@nordwind.example',
    senderDisplayName: 'Nordwind Accounting',
    toAddresses: ['user@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The invoice for August is attached.',
    snippets: ['The **invoice** for August'],
    matchedBy: 'LexicalRanking',
    attachmentMatches: [],
    isDepictedMatch: false,
};

const onePage = JSON.stringify({
    results: [result],
    nextCursor: null,
    pageSize: resultsPerPage,
    retrievalMode: 'Hybrid',
    semanticSearch: 'Available',
    includedJunkMail: false,
});

function recording(body: string): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

// This screen asks the deployment whether it reads a typed sentence before anybody types one, so the requests a test
// about searching means are the searches among what was asked.
function searches(asked: readonly ClientRequest[]): readonly ClientRequest[] {
    return asked.filter((request) => request.path.includes('/emails/search?'));
}

const phraseReading = JSON.stringify({
    read: true,
    filters: {
        sender: 'accounts@nordwind.example',
        recipient: null,
        receivedFrom: null,
        receivedTo: null,
        unread: true,
        flagged: false,
        hasAttachments: false,
    },
    criteria: ['invoice'],
    unaccounted: 'soon',
});

// A deployment answering each of the three questions this screen asks: whether it reads a sentence, what one sentence
// was read as, and the results. One transport rather than three, because what is being proved is the order the screen
// asks them in.
function deployment(options: {
    readonly readsPhrases: boolean;
    readonly reading?: string;
    readonly holdTheReading?: boolean;
}): { transport: MailFathomTransport; asked: ClientRequest[]; answerTheReading: () => void } {
    const asked: ClientRequest[] = [];
    let releaseTheReading = (): void => undefined;

    return {
        asked,
        answerTheReading: () => {
            releaseTheReading();
        },
        transport: (request) => {
            asked.push(request);

            if (request.path.includes('/emails/search/phrasing')) {
                if (request.method === 'GET') {
                    return Promise.resolve({
                        status: 200,
                        body: JSON.stringify({ readsPhrases: options.readsPhrases }),
                        headers: {},
                    });
                }

                const answer = { status: 200, body: options.reading ?? phraseReading, headers: {} };

                return options.holdTheReading === true
                    ? new Promise((resolve) => {
                          releaseTheReading = () => {
                              resolve(answer);
                          };
                      })
                    : Promise.resolve(answer);
            }

            return Promise.resolve({ status: 200, body: onePage, headers: {} });
        },
    };
}

// What the caller renders in this column while nothing is being searched for. It stands for the folder's own list
// rather than being one, which is what lets this file prove the composition without mounting a second screen.
const mailInScope = 'The mail in this folder';

// The clock a test that is not about the clock hands over. Declared once rather than defaulted inline, so a screen
// rendered twice is handed the same function and the effect that reads it does not restart.
const whenTheSuiteRuns = (): Date => new Date();

function searchUnder(
    transport: MailFathomTransport,
    scope: MailScope = everything,
    now: () => Date = whenTheSuiteRuns,
): ReactElement {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <MailSearch
                    session={session}
                    transport={transport}
                    scope={scope}
                    accounts={[work]}
                    online={true}
                    onOpen={() => undefined}
                    onOpenDraft={null}
                    now={now}
                >
                    <p>{mailInScope}</p>
                </MailSearch>
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

function searchFor(words: string): void {
    fireEvent.change(screen.getByLabelText('Find a message'), { target: { value: words } });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));
}

// The workspace keeps what was searched for, and the store is one per file rather than one per test.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('MailSearch', () => {
    it('stands above the mail in scope rather than in place of it', () => {
        render(searchUnder(recording(onePage).transport));

        expect(screen.getByLabelText('Find a message')).toBeTruthy();
        expect(screen.getByText(mailInScope)).toBeTruthy();
    });

    it('searches what was typed and draws what it found in place of the mail in scope', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        searchFor('invoice');

        expect(await screen.findByRole('listbox', { name: 'What this search found' })).toBeTruthy();
        expect(screen.queryByText(mailInScope)).toBeNull();
        expect(searches(asked)[0]?.path).toContain('query=invoice');
    });

    it('searches the mailbox the client is looking at, and says which one that is', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport, { kind: 'account', accountId: 'work' }));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(searches(asked)[0]?.path).toContain('account=work');
        expect(screen.getByRole('list', { name: 'Filters this search is under' }).textContent).toContain('Work');
    });

    it('runs the search again with one filter taken off', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport, { kind: 'account', accountId: 'work' }));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Remove the filter Mailbox: Work' }));

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(searches(asked)[1]?.path).not.toContain('account=work');
    });

    // A field offering to take a description over a deployment that can only match words fails a person at the one
    // moment they trusted it, so the promise follows what the deployment answered rather than what the client can do.
    it('offers to take a description only where the deployment reads one', async () => {
        render(searchUnder(deployment({ readsPhrases: true }).transport));

        expect(await screen.findByPlaceholderText('Search, or describe what you need')).toBeTruthy();
    });

    it('offers the word search where the deployment reads no sentence', async () => {
        render(searchUnder(deployment({ readsPhrases: false }).transport));

        expect(await screen.findByPlaceholderText('Words from the message you are looking for')).toBeTruthy();
    });

    it('turns a typed sentence into filters and criteria somebody can see, and searches with them', async () => {
        const { transport, asked } = deployment({ readsPhrases: true });

        render(searchUnder(transport));
        await screen.findByPlaceholderText('Search, or describe what you need');
        searchFor('unread mail from Nordwind about the invoice, soon');

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(screen.getByRole('list', { name: 'Filters this search is under' }).textContent).toContain(
            'accounts@nordwind.example',
        );
        expect(
            within(screen.getByRole('list', { name: 'What this search is ranked by' })).getAllByRole('listitem'),
        ).toHaveLength(1);
        expect(screen.getByText(/Nothing was made of “soon”/u)).toBeTruthy();

        const search = searches(asked)[0];

        expect(search?.path).toContain('query=invoice');
        expect(search?.path).toContain('unread=true');
    });

    // What *yesterday* means is decided where somebody is standing, so the day travels with the sentence rather than
    // being taken at the other end. A late evening is the hour that would resolve to the wrong day if it were.
    it('sends the day its caller is standing on rather than the day the deployment is', async () => {
        const { transport, asked } = deployment({ readsPhrases: true });

        render(searchUnder(transport, everything, () => new Date(2026, 2, 14, 23, 30)));
        await screen.findByPlaceholderText('Search, or describe what you need');
        searchFor('mail from Nordwind last week');

        await screen.findByRole('listbox', { name: 'What this search found' });

        const read = asked.find((request) => request.method === 'POST');

        expect(JSON.parse(read?.body ?? '{}')).toMatchObject({ askedOn: '2026-03-14' });
    });

    it('runs the search again with one criterion no longer ranking it', async () => {
        const { transport, asked } = deployment({ readsPhrases: true });

        render(searchUnder(transport));
        await screen.findByPlaceholderText('Search, or describe what you need');
        searchFor('unread mail from Nordwind about the invoice, soon');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Stop ranking by invoice' }));

        await screen.findByRole('listbox', { name: 'What this search found' });

        const ran = searches(asked);

        expect(ran).toHaveLength(2);
        expect(ran[1]?.path).toContain('unread=true');
        expect(ran[1]?.path).not.toContain('query=invoice');
    });

    // Nothing waits in silence, and reading a sentence is a provider call rather than an instant.
    it('says it is reading what was typed while it reads it', async () => {
        const held = deployment({ readsPhrases: true, holdTheReading: true });

        render(searchUnder(held.transport));
        await screen.findByPlaceholderText('Search, or describe what you need');
        searchFor('unread mail from Nordwind about the invoice, soon');

        expect(screen.getByRole('status').textContent).toContain('Reading what you wrote');

        held.answerTheReading();

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(screen.queryByText('Reading what you wrote…')).toBeNull();
    });

    // A sentence is not unsearchable because nothing read it: the words are searched exactly as a deployment with no
    // provider searches them, and nothing on the screen reports a failure.
    it('searches the typed words where the deployment reads no sentence', async () => {
        const { transport, asked } = deployment({ readsPhrases: false });

        render(searchUnder(transport));
        await screen.findByPlaceholderText('Words from the message you are looking for');
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(asked.some((request) => request.method === 'POST')).toBe(false);
        expect(searches(asked)[0]?.path).toContain('query=invoice');
    });

    it('says what to do rather than running a search of nothing', () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        fireEvent.click(screen.getByRole('button', { name: 'Search' }));

        expect(screen.getByRole('alert').textContent).toContain('Type something to look for.');
        expect(searches(asked)).toStrictEqual([]);
        expect(screen.getByText(mailInScope)).toBeTruthy();
    });

    it('refuses text longer than this surface ranks against, and says so rather than sending it', () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        searchFor('x'.repeat(513));

        expect(screen.getByRole('alert').textContent).toContain('longer than a search this deployment runs');
        expect(searches(asked)).toStrictEqual([]);
    });

    it('gives the mail in scope back when the search is stopped', async () => {
        render(searchUnder(recording(onePage).transport));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Stop searching' }));

        expect(screen.getByText(mailInScope)).toBeTruthy();
    });

    it('offers what was searched for before, once there is something to offer', async () => {
        render(searchUnder(recording(onePage).transport));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Stop searching' }));

        const offered = within(screen.getByRole('list', { name: 'Searched for before' })).getAllByRole('button');

        expect(offered.map((button) => button.textContent)).toStrictEqual(['invoice', 'Forget these']);
    });

    it('runs one of them again with a single press', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Stop searching' }));
        fireEvent.click(screen.getByRole('button', { name: 'invoice' }));

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(searches(asked)).toHaveLength(2);
    });

    it('forgets what was searched for when asked to', async () => {
        render(searchUnder(recording(onePage).transport));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Stop searching' }));
        fireEvent.click(screen.getByRole('button', { name: 'Forget these' }));

        expect(screen.queryByRole('list', { name: 'Searched for before' })).toBeNull();
    });

    it('offers nothing searched for before to somebody who has searched for nothing', () => {
        render(searchUnder(recording(onePage).transport));

        expect(screen.queryByRole('list', { name: 'Searched for before' })).toBeNull();
    });
});
