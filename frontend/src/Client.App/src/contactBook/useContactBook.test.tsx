// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useContactBook, type ContactBookName } from './useContactBook';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The probe's own controls, named here rather than written into its markup: the localization rule holds over
// every `.tsx` under the packages, tests included, and a label a test invented is not a catalogue entry.
const asksForMore = 'Read more';
const asksAgain = 'Read again';

function contact(id: string, displayName: string): unknown {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        origin: 'Asserted',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

function pageOf(contacts: readonly unknown[], nextCursor: string | null): string {
    return JSON.stringify({ contacts, nextCursor });
}

// Both books over one transport, so a test can say what each answered and how often it was asked. The collected route
// is matched first because the asserted one is a prefix of it.
function answering(answers: Readonly<Record<ContactBookName, readonly string[]>>): {
    readonly transport: MailFathomTransport;
    readonly asked: () => readonly string[];
} {
    const asked: string[] = [];
    const served: Record<ContactBookName, number> = { own: 0, collected: 0 };

    return {
        asked: () => asked,
        transport: ({ path }) => {
            asked.push(path);

            const book: ContactBookName = path.includes('/contacts/collected') ? 'collected' : 'own';
            const pages = answers[book];
            const body = pages[Math.min(served[book], pages.length - 1)] ?? pageOf([], null);

            served[book] += 1;

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

function Book({
    transport,
    book = 'own',
    asking = session,
}: {
    readonly transport: MailFathomTransport;
    readonly book?: ContactBookName;
    readonly asking?: ClientSession | null;
}) {
    const held = useContactBook(asking, transport, book);

    return (
        <div>
            <ul>
                {held.contacts.map((person) => (
                    <li key={person.id}>{person.displayName}</li>
                ))}
            </ul>
            <p>{held.reading ? 'reading' : 'settled'}</p>
            <p>{held.paging ? 'paging' : 'not paging'}</p>
            <p>{held.failure ?? 'answered'}</p>
            <p>{held.complete ? 'complete' : 'more to read'}</p>
            <button type="button" onClick={held.readMore}>
                {asksForMore}
            </button>
            <button type="button" onClick={held.readAgain}>
                {asksAgain}
            </button>
        </div>
    );
}

describe('useContactBook', () => {
    it('reads the book it was asked for and holds the people it named', async () => {
        const { transport, asked } = answering({
            own: [pageOf([contact('anna', 'Anna Kowalska')], null)],
            collected: [],
        });

        render(<Book transport={transport} />);

        expect(await screen.findByText('Anna Kowalska')).toBeDefined();
        await waitFor(() => {
            expect(asked()[0]).toContain('/contacts?');
        });
        expect(screen.getByText('complete')).toBeDefined();
    });

    it('appends the page after the ones held rather than replacing what has been read', async () => {
        const { transport } = answering({
            own: [
                pageOf([contact('anna', 'Anna Kowalska')], 'after-anna'),
                pageOf([contact('bartek', 'Bartek Nowak')], null),
            ],
            collected: [],
        });

        render(<Book transport={transport} />);
        expect(await screen.findByText('Anna Kowalska')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: asksForMore }));

        expect(await screen.findByText('Bartek Nowak')).toBeDefined();
        expect(screen.getByText('Anna Kowalska')).toBeDefined();
    });

    it('reads the other book from the leading end, so the tab pressed never draws the book left behind', async () => {
        const { transport } = answering({
            own: [pageOf([contact('anna', 'Anna Kowalska')], null)],
            collected: [pageOf([contact('celina', 'Celina Wrona')], null)],
        });

        const drawn = render(<Book transport={transport} />);
        expect(await screen.findByText('Anna Kowalska')).toBeDefined();

        drawn.rerender(<Book transport={transport} book="collected" />);

        expect(screen.queryByText('Anna Kowalska')).toBeNull();
        expect(await screen.findByText('Celina Wrona')).toBeDefined();
    });

    it('reports why the deployment did not answer, and reads again when asked to', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve(
                attempts === 1
                    ? { status: 503, body: '', headers: {} }
                    : { status: 200, body: pageOf([contact('anna', 'Anna Kowalska')], null), headers: {} },
            );
        };

        render(<Book transport={transport} />);
        expect(await screen.findByText('unavailable')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: asksAgain }));

        expect(await screen.findByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('answered')).toBeDefined();
    });

    it('reads nothing at all where there is no credential to ask with', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve({ status: 200, body: pageOf([], null), headers: {} });
        };

        render(<Book transport={transport} asking={null} />);

        expect(await screen.findByText('settled')).toBeDefined();
        expect(attempts).toBe(0);
    });
});
