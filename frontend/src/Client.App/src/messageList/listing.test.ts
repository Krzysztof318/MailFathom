// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { everything, type MailScope } from '../workspace/mailScope';
import { narrowed, openingListing, queryFor, rowsPerPage } from './listing';

describe('queryFor', () => {
    it('names neither an account nor a folder for every folder of every mailbox', () => {
        const query = queryFor(everything, openingListing, null, 'forward');

        expect(query.account).toBeNull();
        expect(query.folder).toBeNull();
    });

    it('names the role a scope spanning every mailbox stands for', () => {
        const query = queryFor({ kind: 'role', role: 'Inbox' }, openingListing, null, 'forward');

        expect(query.account).toBeNull();
        expect(query.folder).toBe('role:Inbox');
    });

    it('names the account a scope of one whole mailbox stands for', () => {
        const query = queryFor({ kind: 'account', accountId: 'work' }, openingListing, null, 'forward');

        expect(query.account).toBe('work');
        expect(query.folder).toBeNull();
    });

    it('names both for one folder of one mailbox', () => {
        const scope: MailScope = { kind: 'folder', accountId: 'work', alias: 'ARCHIVE-2024' };
        const query = queryFor(scope, openingListing, null, 'forward');

        expect(query.account).toBe('work');
        expect(query.folder).toBe('ARCHIVE-2024');
    });

    it('leaves junk out of a list spanning folders unless the reader asked for it', () => {
        expect(queryFor(everything, openingListing, null, 'forward').includeJunk).toBe(false);
    });

    it('asks for junk where a scope points at one folder, so the folder somebody opened is not shown empty', () => {
        const scope: MailScope = { kind: 'folder', accountId: 'work', alias: 'Spam' };

        expect(queryFor(scope, openingListing, null, 'forward').includeJunk).toBe(true);
    });

    it('asks for junk where the scope is the junk role, which the deployment withholds from anything wider', () => {
        expect(queryFor({ kind: 'role', role: 'Junk' }, openingListing, null, 'forward').includeJunk).toBe(true);
    });

    it.each(['Inbox', 'Sent', 'Archive', 'Trash'] as const)(
        'leaves junk out of the %s role, which spans folders the reader did not point at',
        (role) => {
            expect(queryFor({ kind: 'role', role }, openingListing, null, 'forward').includeJunk).toBe(false);
        },
    );

    it('carries the reader’s own narrowing', () => {
        const listing = { ...openingListing, filters: { ...openingListing.filters, unread: true, flagged: true } };
        const query = queryFor(everything, listing, null, 'forward');

        expect(query.unread).toBe(true);
        expect(query.flagged).toBe(true);
        expect(query.hasAttachments).toBeNull();
    });

    it('carries the cursor and the direction the page continues from', () => {
        const query = queryFor(everything, openingListing, 'held', 'backward');

        expect(query.cursor).toBe('held');
        expect(query.direction).toBe('backward');
    });

    it('asks for the largest page the deployment serves, because a page is one exchange', () => {
        expect(queryFor(everything, openingListing, null, 'forward').pageSize).toBe(rowsPerPage);
    });
});

describe('narrowed', () => {
    it('reports a list nobody has narrowed', () => {
        expect(narrowed(openingListing.filters)).toBe(false);
    });

    it.each([{ unread: true }, { flagged: true }, { hasAttachments: false }])(
        'reports a list narrowed by %o',
        (filter) => {
            expect(narrowed({ ...openingListing.filters, ...filter })).toBe(true);
        },
    );

    it('does not read reaching into junk as a narrowing, because it widens the list rather than narrowing it', () => {
        expect(narrowed({ ...openingListing.filters, includeJunk: true })).toBe(false);
    });
});
