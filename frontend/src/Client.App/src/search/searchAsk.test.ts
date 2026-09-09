// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailSearchPhraseFilters, MailSearchPhraseReading } from '@mailfathom/client-backend';
import { everything } from '../workspace/mailScope';
import {
    addressFilter,
    askable,
    askFromPhrase,
    askIn,
    askKey,
    inForce,
    narrowings,
    queryFor,
    resultsPerPage,
    selectableRange,
    valueOf,
    widened,
    without,
    withoutCriterion,
} from './searchAsk';

const anywhere = askIn(everything, 'quarterly figures');

const noFilters: MailSearchPhraseFilters = {
    sender: null,
    recipient: null,
    receivedFrom: null,
    receivedTo: null,
    unread: false,
    flagged: false,
    hasAttachments: false,
};

function readingOf(reading: Partial<MailSearchPhraseReading>): MailSearchPhraseReading {
    return { read: true, filters: noFilters, criteria: [], unaccounted: null, ...reading };
}

describe('askIn', () => {
    it('searches every mailbox and folder where the client is looking at everything', () => {
        expect(anywhere).toStrictEqual(
            expect.objectContaining({ text: 'quarterly figures', account: null, folder: null, includeJunk: false }),
        );
    });

    it('searches the account somebody is looking at', () => {
        const ask = askIn({ kind: 'account', accountId: 'work' }, 'invoice');

        expect(ask).toStrictEqual(expect.objectContaining({ account: 'work', folder: null }));
    });

    it('searches the folders a role stands for across every account', () => {
        const ask = askIn({ kind: 'role', role: 'Sent' }, 'invoice');

        expect(ask).toStrictEqual(expect.objectContaining({ account: null, folder: 'role:Sent' }));
    });

    it('searches the one folder somebody opened, in the account that holds it', () => {
        const ask = askIn({ kind: 'folder', accountId: 'work', alias: 'Projects/Nordwind' }, 'invoice');

        expect(ask).toStrictEqual(expect.objectContaining({ account: 'work', folder: 'Projects/Nordwind' }));
    });

    // Junk is withheld from a read spanning folders, so a search inside junk would answer nothing at all unless it
    // asked — and having pointed at junk, asking reaches nothing else.
    it.each([
        [{ kind: 'folder', accountId: 'work', alias: 'Junk' } as const, true],
        [{ kind: 'role', role: 'Junk' } as const, true],
        [{ kind: 'role', role: 'Inbox' } as const, false],
        [everything, false],
    ])('asks for junk in %o: %s', (scope, asked) => {
        expect(askIn(scope, 'invoice').includeJunk).toBe(asked);
    });
});

describe('askFromPhrase', () => {
    // Every constraint a sentence stated lands on the same filter a person builds by hand, which is what makes each of
    // them a chip they can see and take off — the whole promise this screen makes about a typed sentence.
    it('writes what a sentence stated onto the filters a person could have set themselves', () => {
        const ask = askFromPhrase(
            everything,
            'unread mail from sales about racking last month',
            readingOf({
                filters: {
                    ...noFilters,
                    sender: 'sales@example.invalid',
                    receivedFrom: '2026-08-01',
                    receivedTo: '2026-08-31',
                    unread: true,
                },
                criteria: ['racking quotation'],
            }),
        );

        expect(ask).toStrictEqual(
            expect.objectContaining({
                sender: 'sales@example.invalid',
                receivedFrom: '2026-08-01',
                receivedTo: '2026-08-31',
                unread: true,
                flagged: null,
                hasAttachments: null,
            }),
        );
    });

    it('ranks by what the sentence left to rank by rather than by the sentence itself', () => {
        const ask = askFromPhrase(
            everything,
            'anything about the racking we ordered',
            readingOf({
                criteria: ['racking', 'order confirmation'],
            }),
        );

        expect(ask.text).toBe('racking order confirmation');
        expect(ask.criteria).toStrictEqual(['racking', 'order confirmation']);
        expect(ask.phrase).toBe('anything about the racking we ordered');
    });

    // Saying so is the difference between a person correcting an interpretation and a person doubting their mailbox.
    it('keeps the part of the sentence nothing was made of', () => {
        const ask = askFromPhrase(
            everything,
            'the racking quote, urgent',
            readingOf({
                criteria: ['racking quote'],
                unaccounted: 'urgent',
            }),
        );

        expect(ask.unaccounted).toBe('urgent');
    });

    // Somebody searching inside a folder has said where to look, and a sentence naming a sender says who wrote it.
    it('leaves the scope somebody was looking at exactly where it was', () => {
        const ask = askFromPhrase(
            { kind: 'folder', accountId: 'work', alias: 'Projects/Nordwind' },
            'unread mail',
            readingOf({
                filters: { ...noFilters, unread: true },
            }),
        );

        expect(ask).toStrictEqual(expect.objectContaining({ account: 'work', folder: 'Projects/Nordwind' }));
    });

    // A sentence is not unsearchable because nothing read it: the words are searched exactly as they are on a
    // deployment that reads none.
    it('searches the typed words where nothing read the sentence', () => {
        const ask = askFromPhrase(everything, 'quarterly figures', readingOf({ read: false, criteria: ['ignored'] }));

        expect(ask).toStrictEqual(askIn(everything, 'quarterly figures'));
    });

    // A sentence that was all constraint leaves nothing to rank by, and the route refuses a search with no text.
    it('ranks by the sentence where every part of it became a filter', () => {
        const ask = askFromPhrase(everything, 'unread mail', readingOf({ filters: { ...noFilters, unread: true } }));

        expect(ask.text).toBe('unread mail');
    });
});

describe('withoutCriterion', () => {
    it('takes one criterion off and ranks by what is left', () => {
        const ask = askFromPhrase(
            everything,
            'the racking we ordered',
            readingOf({
                criteria: ['racking', 'order confirmation'],
            }),
        );

        const narrowed = withoutCriterion(ask, 'order confirmation');

        expect(narrowed.criteria).toStrictEqual(['racking']);
        expect(narrowed.text).toBe('racking');
    });

    // A person who removed every criterion asked for a broader order rather than for no search, and the deployment
    // refuses a search carrying no text at all.
    it('leaves the sentence itself ranking once the last criterion is off', () => {
        const ask = askFromPhrase(everything, 'the racking we ordered', readingOf({ criteria: ['racking'] }));

        expect(withoutCriterion(ask, 'racking').text).toBe('the racking we ordered');
    });

    it('leaves every filter where it was, a criterion being no filter at all', () => {
        const ask = askFromPhrase(
            everything,
            'unread mail about racking',
            readingOf({
                filters: { ...noFilters, unread: true },
                criteria: ['racking'],
            }),
        );

        expect(withoutCriterion(ask, 'racking').unread).toBe(true);
    });
});

describe('narrowings', () => {
    it('reports nothing for a search over everything', () => {
        expect(narrowings(anywhere)).toStrictEqual([]);
    });

    it('reports every filter in force, in the order they are drawn', () => {
        const narrowed = { ...anywhere, sender: 'a@example.invalid', account: 'work', unread: true as const };

        expect(narrowings(narrowed)).toStrictEqual(['account', 'sender', 'unread']);
    });

    it('reports the junk a scope asked for as a filter that can be taken off', () => {
        expect(narrowings(askIn({ kind: 'role', role: 'Junk' }, 'invoice'))).toStrictEqual(['folder', 'includeJunk']);
    });
});

describe('valueOf', () => {
    it('answers what a filter holding a value is set to', () => {
        expect(valueOf({ ...anywhere, sender: 'a@example.invalid' }, 'sender')).toBe('a@example.invalid');
    });

    it('answers nothing for a filter that is on or off rather than set to something', () => {
        expect(valueOf({ ...anywhere, unread: true }, 'unread')).toBeNull();
    });
});

describe('without', () => {
    it.each([
        ['account', { account: 'work' }],
        ['folder', { folder: 'role:Inbox' }],
        ['sender', { sender: 'a@example.invalid' }],
        ['recipient', { recipient: 'b@example.invalid' }],
        ['receivedFrom', { receivedFrom: '2026-08-01' }],
        ['receivedTo', { receivedTo: '2026-08-31' }],
        ['unread', { unread: true as const }],
        ['flagged', { flagged: true as const }],
        ['hasAttachments', { hasAttachments: true as const }],
        ['includeJunk', { includeJunk: true }],
    ] as const)('takes %s off, one press at a time', (narrowing, narrowed) => {
        const ask = { ...anywhere, ...narrowed };

        expect(inForce(ask, narrowing)).toBe(true);
        expect(inForce(without(ask, narrowing), narrowing)).toBe(false);
    });

    it('leaves every other filter where it was', () => {
        const ask = { ...anywhere, account: 'work', sender: 'a@example.invalid' };

        expect(without(ask, 'account')).toStrictEqual({ ...ask, account: null });
    });
});

describe('widened', () => {
    it('keeps the words and takes every filter off', () => {
        const ask = { ...anywhere, account: 'work', folder: 'role:Inbox', unread: true as const, includeJunk: true };

        expect(widened(ask)).toStrictEqual(anywhere);
    });
});

describe('askKey', () => {
    it('separates two searches that differ only by a filter', () => {
        expect(askKey({ ...anywhere, unread: true })).not.toBe(askKey(anywhere));
    });

    it('separates two searches that differ only by their words', () => {
        expect(askKey({ ...anywhere, text: 'something else' })).not.toBe(askKey(anywhere));
    });

    it('reads two searches asked with the same thing as one search', () => {
        expect(askKey({ ...anywhere })).toBe(askKey(anywhere));
    });
});

describe('queryFor', () => {
    it('asks for the largest page the deployment serves, from the best-ranked results', () => {
        expect(queryFor(anywhere, null)).toStrictEqual(
            expect.objectContaining({ text: 'quarterly figures', pageSize: resultsPerPage, cursor: null }),
        );
    });

    it('continues a ranked list from the cursor the page before it answered with', () => {
        expect(queryFor(anywhere, 'AbCd').cursor).toBe('AbCd');
    });

    it('reaches back to the start of the first day in the reader’s own zone', () => {
        const query = queryFor({ ...anywhere, receivedFrom: '2026-08-15' }, null);

        expect(query.receivedOnOrAfter).toBe(new Date(2026, 7, 15).toISOString());
    });

    // The route's range excludes its end, so the day somebody named as the last one is included by ending at the start
    // of the day after it.
    it('reaches to the end of the last day rather than to its start', () => {
        const query = queryFor({ ...anywhere, receivedTo: '2026-08-31' }, null);

        expect(query.receivedBefore).toBe(new Date(2026, 8, 1).toISOString());
    });

    it('asks for no range where no day was named', () => {
        expect(queryFor(anywhere, null)).toStrictEqual(
            expect.objectContaining({ receivedOnOrAfter: null, receivedBefore: null }),
        );
    });

    it('asks for no range where the day is not one', () => {
        expect(queryFor({ ...anywhere, receivedFrom: 'the fifteenth' }, null).receivedOnOrAfter).toBeNull();
    });
});

describe('askable', () => {
    it.each([
        ['quarterly figures', true],
        ['  padded  ', true],
        ['', false],
        ['   ', false],
        ['x'.repeat(513), false],
        ['x'.repeat(512), true],
    ])('reads %j as a search that may be run: %s', (text, may) => {
        expect(askable(text, 512)).toBe(may);
    });
});

describe('addressFilter', () => {
    it.each([
        ['somebody@example.invalid', 'somebody@example.invalid'],
        ['  somebody@example.invalid  ', 'somebody@example.invalid'],
    ])('reads %j as the address %j', (typed, address) => {
        expect(addressFilter(typed)).toBe(address);
    });

    it.each([
        ['nordwind'],
        ['@example.invalid'],
        ['somebody@'],
        ['some body@example.invalid'],
        ['somebody@nordwind@example.invalid'],
        [''],
    ])('refuses %j, which could not be an address at all', (typed) => {
        expect(addressFilter(typed)).toBeNull();
    });
});

describe('selectableRange', () => {
    it.each([
        ['2026-08-01', '2026-08-31', true],
        ['2026-08-31', '2026-08-31', true],
        ['2026-09-01', '2026-08-31', false],
        [null, '2026-08-31', true],
        ['2026-08-01', null, true],
    ])('reads %s to %s as a range that can select something: %s', (from, to, selects) => {
        expect(selectableRange(from, to)).toBe(selects);
    });
});
