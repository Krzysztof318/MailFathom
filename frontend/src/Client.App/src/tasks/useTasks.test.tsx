// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useTasks } from './useTasks';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The probe's own controls, named here rather than written into its markup: the localization rule holds over every
// `.tsx` under the packages, tests included, and a label a test invented is not a catalogue entry.
const asksForMore = 'Read more';
const asksAgain = 'Read again';

function task(id: string, title: string, dueOn: string | null): unknown {
    return { id, title, dueOn, origin: 'Asserted', completed: false, sourceMessageId: null };
}

function pageOf(tasks: readonly unknown[], nextCursor: string | null): string {
    return JSON.stringify({ tasks, nextCursor });
}

type HalfName = 'committed' | 'proposed';

// Both halves over one transport, so a test can say what each answered and how often it was asked. The proposed route
// is matched first because the committed one is a prefix of it.
function answering(answers: Readonly<Record<HalfName, readonly (string | number)[]>>): {
    readonly transport: MailFathomTransport;
    readonly asked: () => readonly string[];
} {
    const asked: string[] = [];
    const served: Record<HalfName, number> = { committed: 0, proposed: 0 };

    return {
        asked: () => asked,
        transport: ({ path }) => {
            asked.push(path);

            const half: HalfName = path.includes('/tasks/proposed') ? 'proposed' : 'committed';
            const pages = answers[half];
            const answer = pages[Math.min(served[half], pages.length - 1)];

            served[half] += 1;

            return Promise.resolve(
                typeof answer === 'number'
                    ? { status: answer, body: '', headers: {} }
                    : { status: 200, body: answer ?? pageOf([], null), headers: {} },
            );
        },
    };
}

function List({
    transport,
    asking = session,
}: {
    readonly transport: MailFathomTransport;
    readonly asking?: ClientSession | null;
}) {
    const held = useTasks(asking, transport);

    return (
        <div>
            <p>{held.reading ? 'reading' : 'read'}</p>
            <p>{held.paging ? 'paging' : 'settled'}</p>
            <p>{held.complete ? 'complete' : 'more to come'}</p>
            <p>{held.failure ?? 'no failure'}</p>
            <ol>
                {held.tasks.map((held) => (
                    <li key={held.id}>{held.title}</li>
                ))}
            </ol>
            <button onClick={held.readMore}>{asksForMore}</button>
            <button onClick={held.readAgain}>{asksAgain}</button>
        </div>
    );
}

function titlesDrawn(): (string | null)[] {
    return screen.getAllByRole('listitem').map((row) => row.textContent);
}

describe('useTasks', () => {
    it('reads both halves and draws them as one list in the order they are due', async () => {
        render(
            <List
                transport={
                    answering({
                        committed: [pageOf([task('a', 'Answer the tender', '2026-09-24')], null)],
                        proposed: [pageOf([task('b', 'Send the invoice', '2026-09-22')], null)],
                    }).transport
                }
            />,
        );

        await waitFor(() => {
            expect(titlesDrawn()).toStrictEqual(['Send the invoice', 'Answer the tender']);
        });
    });

    it('says it is reading until both halves have answered', () => {
        render(
            <List
                transport={answering({ committed: [pageOf([], null)], proposed: [pageOf([], null)] }).transport}
            />,
        );

        expect(screen.getByText('reading')).toBeDefined();
    });

    it('reads nothing at all where there is no credential to ask with', () => {
        const deployment = answering({ committed: [pageOf([], null)], proposed: [pageOf([], null)] });

        render(<List transport={deployment.transport} asking={null} />);

        expect(deployment.asked()).toStrictEqual([]);
        expect(screen.getByText('read')).toBeDefined();
    });

    // The one invariant this hook exists for: half a list drawn as a whole one is a person told they owe less than
    // they do.
    it('fails the whole read where one half failed, rather than drawing the half that answered', async () => {
        render(
            <List
                transport={
                    answering({
                        committed: [pageOf([task('a', 'Answer the tender', '2026-09-24')], null)],
                        proposed: [503],
                    }).transport
                }
            />,
        );

        await waitFor(() => {
            expect(screen.getByText('unavailable')).toBeDefined();
        });

        expect(screen.queryAllByRole('listitem')).toStrictEqual([]);
    });

    it('asks both halves for their next page, so neither is read further than the other', async () => {
        const deployment = answering({
            committed: [pageOf([task('a', 'Answer the tender', '2026-09-24')], 'more-committed'), pageOf([], null)],
            proposed: [pageOf([task('b', 'Send the invoice', '2026-09-22')], 'more-proposed'), pageOf([], null)],
        });

        render(<List transport={deployment.transport} />);

        await waitFor(() => {
            expect(screen.getByText('more to come')).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: asksForMore }));

        await waitFor(() => {
            expect(screen.getByText('complete')).toBeDefined();
        });

        expect(deployment.asked().filter((path) => path.includes('cursor=more-committed'))).toHaveLength(1);
        expect(deployment.asked().filter((path) => path.includes('cursor=more-proposed'))).toHaveLength(1);
    });

    it('leaves a half that has reached its end unasked while the other one goes on', async () => {
        const deployment = answering({
            committed: [pageOf([task('a', 'Answer the tender', '2026-09-24')], 'more-committed'), pageOf([], null)],
            proposed: [pageOf([task('b', 'Send the invoice', '2026-09-22')], null)],
        });

        render(<List transport={deployment.transport} />);

        await waitFor(() => {
            expect(screen.getByText('more to come')).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: asksForMore }));

        await waitFor(() => {
            expect(screen.getByText('complete')).toBeDefined();
        });

        expect(deployment.asked().filter((path) => path.includes('/tasks/proposed'))).toHaveLength(1);
    });

    it('appends a further page rather than replacing what is already read', async () => {
        const deployment = answering({
            committed: [pageOf([task('a', 'Answer the tender', '2026-09-22')], 'more-committed'), pageOf([task('c', 'File the return', '2026-09-25')], null)],
            proposed: [pageOf([], null)],
        });

        render(<List transport={deployment.transport} />);

        await waitFor(() => {
            expect(titlesDrawn()).toStrictEqual(['Answer the tender']);
        });

        fireEvent.click(screen.getByRole('button', { name: asksForMore }));

        await waitFor(() => {
            expect(titlesDrawn()).toStrictEqual(['Answer the tender', 'File the return']);
        });
    });

    it('reads again from the leading end rather than appending, which is what every write is followed by', async () => {
        const deployment = answering({
            committed: [
                pageOf([task('a', 'Answer the tender', '2026-09-22')], null),
                pageOf([task('c', 'File the return', '2026-09-25')], null),
            ],
            proposed: [pageOf([], null)],
        });

        render(<List transport={deployment.transport} />);

        await waitFor(() => {
            expect(titlesDrawn()).toStrictEqual(['Answer the tender']);
        });

        fireEvent.click(screen.getByRole('button', { name: asksAgain }));

        await waitFor(() => {
            expect(titlesDrawn()).toStrictEqual(['File the return']);
        });
    });

    it('does not ask for a page it has already asked for while that page is on its way', async () => {
        const deployment = answering({
            committed: [pageOf([task('a', 'Answer the tender', '2026-09-22')], 'more-committed'), pageOf([], null)],
            proposed: [pageOf([], null)],
        });

        render(<List transport={deployment.transport} />);

        await waitFor(() => {
            expect(screen.getByText('more to come')).toBeDefined();
        });

        const asksForTheNextPage = screen.getByRole('button', { name: asksForMore });

        fireEvent.click(asksForTheNextPage);
        fireEvent.click(asksForTheNextPage);

        await waitFor(() => {
            expect(screen.getByText('complete')).toBeDefined();
        });

        expect(deployment.asked().filter((path) => path.includes('cursor=more-committed'))).toHaveLength(1);
    });
});
