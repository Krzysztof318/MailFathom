// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { mostSuggestions, searchContacts, suggestionsFor } from './recipientSuggestions';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// Both halves of the book answer with the one person, which is what the deployment does for somebody held in both.
function answering(addresses: readonly string[], preferredAddress: string): MailFathomTransport {
    const body = JSON.stringify({
        contacts: [
            {
                id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                displayName: 'Anna Kowalska',
                addresses,
                preferredAddress,
                note: null,
                origin: 'Asserted',
                recordedAt: '2026-03-01T08:00:00+00:00',
                amendedAt: '2026-03-01T08:00:00+00:00',
            },
        ],
        nextCursor: null,
    });

    return () => Promise.resolve({ status: 200, body, headers: {} });
}

describe('searchContacts', () => {
    it('offers somebody found by one of their addresses at that address, which is the one being typed', async () => {
        const found = await searchContacts(
            session,
            answering(['anna@example.invalid', 'a.kowalska@work.example.invalid'], 'anna@example.invalid'),
            'work',
        );

        expect(found).toEqual({
            kind: 'found',
            suggestions: [
                { address: 'a.kowalska@work.example.invalid', name: 'Anna Kowalska' },
                { address: 'a.kowalska@work.example.invalid', name: 'Anna Kowalska' },
            ],
        });
    });

    it('offers somebody found by their name at the address they prefer', async () => {
        const found = await searchContacts(
            session,
            answering(['a.kowalska@work.example.invalid', 'anna@example.invalid'], 'anna@example.invalid'),
            'Anna K',
        );

        expect(found).toMatchObject({ kind: 'found', suggestions: [{ address: 'anna@example.invalid' }, {}] });
    });
});

describe('suggestionsFor', () => {
    it('lists no more than a glance takes in', () => {
        const many = Array.from({ length: mostSuggestions + 4 }, (_, at) => ({
            address: `reader${at.toFixed(0)}@example.invalid`,
            name: null,
        }));

        expect(suggestionsFor('reader', many, [], [])).toHaveLength(mostSuggestions);
    });

    it('lists one person once, however many places they were found in', () => {
        const offered = suggestionsFor(
            'anna',
            [{ address: 'anna@example.invalid', name: 'Anna' }],
            [
                { address: 'ANNA@example.invalid', name: 'Anna Kowalska' },
                { address: 'ANNA@example.invalid', name: 'Anna Kowalska' },
            ],
            [],
        );

        expect(offered).toStrictEqual([{ address: 'anna@example.invalid', name: 'Anna' }]);
    });
});
