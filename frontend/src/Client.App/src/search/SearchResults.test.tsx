// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { everything } from '../workspace/mailScope';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { WorkspaceProvider } from '../workspace/Workspace';
import { askIn, resultsPerPage, type MailSearchAsk } from './searchAsk';
import { SearchResults } from './SearchResults';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const anywhere = askIn(everything, 'invoice');

function result(at: number, carried: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        id: `message-${String(at)}`,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: `Invoice ${String(at)}`,
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: `writer-${String(at)}@nordwind.example`,
        senderDisplayName: `Writer ${String(at)}`,
        toAddresses: ['owner@example.invalid'],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 1_024,
        preview: `The opening of message ${String(at)}.`,
        snippets: [`The **invoice** for August, number ${String(at)}`],
        matchedBy: 'LexicalRanking',
        ...carried,
    };
}

function pageOf(results: readonly unknown[], page: Record<string, unknown> = {}): string {
    return JSON.stringify({
        results,
        nextCursor: null,
        pageSize: resultsPerPage,
        retrievalMode: 'Hybrid',
        semanticSearch: 'Available',
        includedJunkMail: false,
        ...page,
    });
}

function answering(body: string, status = 200): MailFathomTransport {
    return () => Promise.resolve({ status, body, headers: {} });
}

function answeringInTurn(...bodies: readonly string[]): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            const body = bodies[asked.length] ?? bodies[bodies.length - 1] ?? pageOf([]);

            asked.push(request);

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

// What the results wrote, read back the way the reading pane beside them will read it.
function SelectionProbe() {
    const { workspace } = useWorkspace();

    return <output>{JSON.stringify(workspace)}</output>;
}

function carried(): Workspace {
    const probe = screen.getAllByRole('status').find((element) => element.textContent.startsWith('{'));

    return JSON.parse(probe?.textContent ?? '') as Workspace;
}

interface Drawn {
    readonly ask: MailSearchAsk;
    readonly online: boolean;
    readonly narrowed: boolean;
    readonly onWiden: () => void;
}

function resultsUnder(
    transport: MailFathomTransport,
    { ask = anywhere, online = true, narrowed = false, onWiden = () => undefined }: Partial<Drawn> = {},
): ReactElement {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <SearchResults
                    session={session}
                    transport={transport}
                    ask={ask}
                    online={online}
                    narrowed={narrowed}
                    onWiden={onWiden}
                />
                <SelectionProbe />
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

async function rows(): Promise<HTMLElement[]> {
    const list = await screen.findByRole('listbox', { name: 'What this search found' });

    return within(list).getAllByRole('option');
}

describe('SearchResults', () => {
    it('says it is searching from the moment the search starts', () => {
        render(resultsUnder(() => new Promise(() => undefined)));

        expect(screen.getByText('Searching your mail…')).toBeTruthy();
    });

    it('draws what the search found, best first', async () => {
        render(resultsUnder(answering(pageOf([result(1), result(2)]))));

        const found = await rows();

        expect(found).toHaveLength(2);
        expect(found[0]?.textContent).toContain('Invoice 1');
        expect(found[1]?.textContent).toContain('Invoice 2');
    });

    it('shows the extract around what matched, so a row says why it is in the list', async () => {
        render(resultsUnder(answering(pageOf([result(1)]))));

        const [found] = await rows();

        expect(found?.textContent).toContain('The invoice for August, number 1');
    });

    it('says a message matched by meaning where there is no extract of it that shows the words', async () => {
        const meaning = result(1, { snippets: [], matchedBy: 'SemanticRanking' });

        render(resultsUnder(answering(pageOf([meaning]))));

        const [found] = await rows();

        expect(found?.textContent).toContain('Found by what it means rather than by these words.');
    });

    it('says a message matched what it is about where the words are not in its text', async () => {
        const headers = result(1, { snippets: [], matchedBy: 'LexicalRanking' });

        render(resultsUnder(answering(pageOf([headers]))));

        const [found] = await rows();

        expect(found?.textContent).toContain('Matched what this message is about rather than anything in its text.');
    });

    it('says a message matched both ways where the words are not in its text either', async () => {
        const both = result(1, { snippets: [], matchedBy: 'BothRankings' });

        render(resultsUnder(answering(pageOf([both]))));

        const [found] = await rows();

        expect(found?.textContent).toContain('Matched these words and what this message is about.');
    });

    it('says a deployment that has activated no embedding profile searched by words alone', async () => {
        const lexical = pageOf([result(1)], { retrievalMode: 'Lexical', semanticSearch: 'Inactive' });

        render(resultsUnder(answering(lexical)));

        expect(await screen.findByText(/does not search by meaning/)).toBeTruthy();
    });

    it('separates a deployment whose meaning search is failing from one that does not offer it', async () => {
        const lexical = pageOf([result(1)], { retrievalMode: 'Lexical', semanticSearch: 'Degraded' });

        render(resultsUnder(answering(lexical)));

        expect(await screen.findByText(/is not working on this deployment/)).toBeTruthy();
    });

    it('says nothing about how the search was ranked where meaning took part', async () => {
        render(resultsUnder(answering(pageOf([result(1)]))));

        await rows();

        expect(screen.queryByText(/does not search by meaning/)).toBeNull();
    });

    it('continues the ranked list from the cursor the page before it answered with', async () => {
        const { transport, asked } = answeringInTurn(
            pageOf([result(1), result(2)], { nextCursor: 'AbCd' }),
            pageOf([result(3)]),
        );

        render(resultsUnder(transport));

        // Waited on by what the second page carries rather than by the list appearing, which the first page already
        // did: the question is whether the page after it arrived.
        await screen.findByText('Invoice 3');

        expect(await rows()).toHaveLength(3);
        expect(asked[1]?.path).toContain('cursor=AbCd');
    });

    it('says where the ranked list ends', async () => {
        render(resultsUnder(answering(pageOf([result(1)]))));

        expect(await screen.findByText('That is everything this search found.')).toBeTruthy();
    });

    it('renders a search that matched nothing as empty rather than as a blank pane', async () => {
        render(resultsUnder(answering(pageOf([]))));

        expect(await screen.findByText('No message matches this search.')).toBeTruthy();
    });

    it('offers to widen a narrowed search that matched nothing', async () => {
        const widen = vi.fn();

        render(resultsUnder(answering(pageOf([])), { narrowed: true, onWiden: widen }));

        fireEvent.click(await screen.findByRole('button', { name: 'Search all your mail instead' }));

        expect(widen).toHaveBeenCalledOnce();
    });

    it('offers no widening for a search that was already over everything', async () => {
        render(resultsUnder(answering(pageOf([]))));

        await screen.findByText('No message matches this search.');

        expect(screen.queryByRole('button', { name: 'Search all your mail instead' })).toBeNull();
    });

    it('opens a result without losing what the search found', async () => {
        render(resultsUnder(answering(pageOf([result(1), result(2)]))));

        const found = await rows();

        fireEvent.pointerDown(found[1] ?? document.body);

        expect(carried().selection).toBe('message-2');
        expect(await rows()).toHaveLength(2);
    });

    it('opens the result the keyboard is on', async () => {
        render(resultsUnder(answering(pageOf([result(1), result(2)]))));

        const list = await screen.findByRole('listbox', { name: 'What this search found' });

        fireEvent.keyDown(list, { key: 'ArrowDown' });
        fireEvent.keyDown(list, { key: 'Enter' });

        expect(carried().selection).toBe('message-2');
    });

    it('says what failed and offers the one way out of it', async () => {
        render(resultsUnder(answering('', 503)));

        expect((await screen.findByRole('alert')).textContent).toContain('This search could not be run: unavailable.');
        expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy();
    });

    it('offers no retry for a failure a second attempt would repeat', async () => {
        render(resultsUnder(answering('', 403)));

        await screen.findByRole('alert');

        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('reads a refusal of what this client composed as a defect rather than as something to retry', async () => {
        render(resultsUnder(answering('', 400)));

        expect((await screen.findByRole('alert')).textContent).toContain('This search could not be run: unreadable.');
    });

    it('says the machine is offline rather than that the search found nothing', () => {
        render(resultsUnder(answering(pageOf([])), { online: false }));

        expect(screen.getByText(/This machine is offline/)).toBeTruthy();
    });

    it('keeps what it found on the screen when a later page fails', async () => {
        const { transport } = answeringInTurn(pageOf([result(1)], { nextCursor: 'AbCd' }), '');

        render(resultsUnder(transport));

        expect((await screen.findByRole('alert')).textContent).toContain('Part of this search could not be read');
        expect(await rows()).toHaveLength(1);
    });
});
