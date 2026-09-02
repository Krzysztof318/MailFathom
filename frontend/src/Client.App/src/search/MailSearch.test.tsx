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
    toAddresses: ['owner@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The invoice for August is attached.',
    snippets: ['The **invoice** for August'],
    matchedBy: 'LexicalRanking',
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

// What the caller renders in this column while nothing is being searched for. It stands for the folder's own list
// rather than being one, which is what lets this file prove the composition without mounting a second screen.
const mailInScope = 'The mail in this folder';

function searchUnder(transport: MailFathomTransport, scope: MailScope = everything): ReactElement {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <MailSearch session={session} transport={transport} scope={scope} accounts={[work]} online={true}>
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
        expect(asked[0]?.path).toContain('query=invoice');
    });

    it('searches the mailbox the client is looking at, and says which one that is', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport, { kind: 'account', accountId: 'work' }));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(asked[0]?.path).toContain('account=work');
        expect(screen.getByRole('list', { name: 'Filters this search is under' }).textContent).toContain('Work');
    });

    it('runs the search again with one filter taken off', async () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport, { kind: 'account', accountId: 'work' }));
        searchFor('invoice');

        await screen.findByRole('listbox', { name: 'What this search found' });
        fireEvent.click(screen.getByRole('button', { name: 'Remove the filter Mailbox: Work' }));

        await screen.findByRole('listbox', { name: 'What this search found' });

        expect(asked[1]?.path).not.toContain('account=work');
    });

    it('says what to do rather than running a search of nothing', () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        fireEvent.click(screen.getByRole('button', { name: 'Search' }));

        expect(screen.getByRole('alert').textContent).toContain('Type something to look for.');
        expect(asked).toStrictEqual([]);
        expect(screen.getByText(mailInScope)).toBeTruthy();
    });

    it('refuses text longer than this surface ranks against, and says so rather than sending it', () => {
        const { transport, asked } = recording(onePage);

        render(searchUnder(transport));
        searchFor('x'.repeat(513));

        expect(screen.getByRole('alert').textContent).toContain('longer than a search this deployment runs');
        expect(asked).toStrictEqual([]);
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

        expect(asked).toHaveLength(2);
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
