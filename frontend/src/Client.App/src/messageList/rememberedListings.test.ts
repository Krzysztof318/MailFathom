// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { everything, type MailScope } from '../workspace/mailScope';
import { openingListing } from './listing';
import {
    forgetListings,
    rememberedListing,
    rememberListing,
    neverOpenedListing,
    type RememberedListing,
} from './rememberedListings';

const storageKey = 'mailfathom.listings';
const deployment = 'https://mail.example.invalid';
const inbox: MailScope = { kind: 'folder', accountId: 'work', alias: 'INBOX' };

const kept: RememberedListing = {
    order: 'oldestFirst',
    filters: { unread: true, flagged: null, hasAttachments: null, includeJunk: false },
    cursor: 'the-cursor-that-page-was-read-with',
    readAs: 'backward',
    rowInPage: 17,
};

function stored(value: unknown): void {
    window.sessionStorage.setItem(storageKey, JSON.stringify(value));
}

function keyFor(scope: MailScope, address = deployment): string {
    return `${address}\n${scope.kind === 'folder' ? `folder:${scope.accountId}:${scope.alias}` : scope.kind}`;
}

// The store is one per file rather than one per test, so what one test kept would be what the next one read back.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('rememberListing', () => {
    it('keeps a folder’s position and how it was being read, and reads it back whole', () => {
        rememberListing(deployment, inbox, kept);

        expect(rememberedListing(deployment, inbox)).toStrictEqual(kept);
    });

    it('keeps a folder of one deployment apart from the same folder of another', () => {
        rememberListing(deployment, inbox, kept);

        expect(rememberedListing('https://other.example.invalid', inbox)).toStrictEqual(neverOpenedListing);
    });

    it('keeps one folder apart from another of the same deployment', () => {
        rememberListing(deployment, inbox, kept);

        expect(rememberedListing(deployment, everything)).toStrictEqual(neverOpenedListing);
    });

    it('drops the folder read longest ago once it is holding as many as it keeps', () => {
        for (let at = 0; at < 65; at += 1) {
            rememberListing(deployment, { kind: 'account', accountId: `account-${String(at)}` }, kept);
        }

        expect(rememberedListing(deployment, { kind: 'account', accountId: 'account-0' })).toStrictEqual(
            neverOpenedListing,
        );
        expect(rememberedListing(deployment, { kind: 'account', accountId: 'account-64' })).toStrictEqual(kept);
    });

    it('keeps a folder read again rather than dropping it as the oldest', () => {
        rememberListing(deployment, inbox, kept);

        for (let at = 0; at < 63; at += 1) {
            rememberListing(deployment, { kind: 'account', accountId: `account-${String(at)}` }, kept);
        }

        rememberListing(deployment, inbox, kept);
        rememberListing(deployment, { kind: 'account', accountId: 'one-more' }, kept);

        expect(rememberedListing(deployment, inbox)).toStrictEqual(kept);
    });
});

describe('rememberedListing', () => {
    it('opens a folder nobody has read at its leading end, newest first', () => {
        expect(rememberedListing(deployment, inbox)).toStrictEqual({
            ...openingListing,
            cursor: null,
            readAs: 'forward',
            rowInPage: 0,
        });
    });

    it.each([
        ['a store holding something that is not JSON', 'not json'],
        ['a store holding an array', JSON.stringify([])],
    ])('opens at the leading end for %s', (_, written) => {
        window.sessionStorage.setItem(storageKey, written);

        expect(rememberedListing(deployment, inbox)).toStrictEqual(neverOpenedListing);
    });

    it.each([
        ['an order this client never wrote', { ...kept, order: 'bySender' }],
        ['a direction this client never wrote', { ...kept, readAs: 'sideways' }],
        ['a cursor that is not text', { ...kept, cursor: 7 }],
        ['a cursor with nothing in it', { ...kept, cursor: '' }],
        ['a row that is not whole', { ...kept, rowInPage: 1.5 }],
        ['a row before the first', { ...kept, rowInPage: -1 }],
        ['a row past the page it names', { ...kept, rowInPage: 100 }],
        ['a filter that is neither answer nor both', { ...kept, filters: { ...kept.filters, unread: 'yes' } }],
        ['no junk answer at all', { ...kept, filters: { unread: null, flagged: null, hasAttachments: null } }],
        ['filters that are not a record', { ...kept, filters: [] }],
        ['a listing that is not a record', 'the inbox'],
    ])('opens at the leading end for a record carrying %s', (_, written) => {
        stored({ [keyFor(inbox)]: written });

        expect(rememberedListing(deployment, inbox)).toStrictEqual(neverOpenedListing);
    });

    it('refuses a store holding more folders than it keeps rather than reading part of it', () => {
        const crowd = Object.fromEntries(
            Array.from({ length: 65 }, (_, at) => [keyFor({ kind: 'account', accountId: String(at) }), kept]),
        );

        stored(crowd);

        expect(rememberedListing(deployment, { kind: 'account', accountId: '1' })).toStrictEqual(neverOpenedListing);
    });
});

describe('forgetListings', () => {
    it('drops every position, so where somebody was reading does not outlive their credential', () => {
        rememberListing(deployment, inbox, kept);

        forgetListings();

        expect(rememberedListing(deployment, inbox)).toStrictEqual(neverOpenedListing);
    });
});
