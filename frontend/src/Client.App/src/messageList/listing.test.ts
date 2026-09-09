// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { everything, type MailScope } from '../workspace/mailScope';
import {
    narrowed,
    narrowedByReading,
    narrowedToRange,
    narrowedToThisWeek,
    narrowedToView,
    narrowingsInForce,
    openingListing,
    queryFor,
    rowsPerPage,
    selectableRange,
    standingViews,
    type MailListing,
} from './listing';

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

    it('asks for the range as the two instants the reader’s own wall clock names', () => {
        const filters = { ...openingListing.filters, receivedFrom: '2026-08-01T09:30', receivedTo: '2026-09-01T18:00' };
        const query = queryFor(everything, { ...openingListing, filters }, null, 'forward');

        expect(query.receivedOnOrAfter).toBe(new Date(2026, 7, 1, 9, 30).toISOString());
        expect(query.receivedBefore).toBe(new Date(2026, 8, 1, 18, 0).toISOString());
    });

    it('asks for no range where the list is not narrowed by when mail arrived', () => {
        const query = queryFor(everything, openingListing, null, 'forward');

        expect(query.receivedOnOrAfter).toBeNull();
        expect(query.receivedBefore).toBeNull();
    });

    it('asks for no bound the date control never wrote, so nothing this client did not compose is sent', () => {
        const filters = { ...openingListing.filters, receivedFrom: 'the first of August' };

        expect(queryFor(everything, { ...openingListing, filters }, null, 'forward').receivedOnOrAfter).toBeNull();
    });

    it('asks for the reading a derivation must have left, which is the whole of what a standing view is', () => {
        const filters = { ...openingListing.filters, markAspect: 'Significance' as const };

        expect(queryFor(everything, { ...openingListing, filters }, null, 'forward').carriesMark).toBe('Significance');
    });

    it('asks for the due window as the two instants the reader’s own wall clock names', () => {
        const filters = {
            ...openingListing.filters,
            markAspect: 'Commitment' as const,
            markDueFrom: '2026-09-03T00:00',
            markDueTo: '2026-09-10T00:00',
        };
        const query = queryFor(everything, { ...openingListing, filters }, null, 'forward');

        expect(query.markDueOnOrAfter).toBe(new Date(2026, 8, 3).toISOString());
        expect(query.markDueBefore).toBe(new Date(2026, 8, 10).toISOString());
    });

    it('asks for no reading where nothing a derivation left is what the list is narrowed by', () => {
        const query = queryFor(everything, openingListing, null, 'forward');

        expect(query.carriesMark).toBeNull();
        expect(query.markDueOnOrAfter).toBeNull();
        expect(query.markDueBefore).toBeNull();
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

    it.each([{ receivedFrom: '2026-08-01T00:00' }, { receivedTo: '2026-09-01T00:00' }])(
        'reports a list narrowed by when mail arrived: %o',
        (range) => {
            expect(narrowed({ ...openingListing.filters, ...range })).toBe(true);
        },
    );

    it.each([{ markAspect: 'Sense' as const }, { markDueFrom: '2026-09-03T00:00' }, { markDueTo: '2026-09-10T00:00' }])(
        'reports a list narrowed by what a derivation read: %o',
        (criterion) => {
            expect(narrowed({ ...openingListing.filters, ...criterion })).toBe(true);
        },
    );
});

describe('narrowedByReading', () => {
    it('reports a list nobody has narrowed by anything a derivation left', () => {
        expect(narrowedByReading(openingListing.filters)).toBe(false);
    });

    it('does not read a received range as one, so an empty folder is not blamed on a derivation', () => {
        const filters = { ...openingListing.filters, receivedFrom: '2026-08-01T00:00' };

        expect(narrowedByReading(filters)).toBe(false);
    });

    it.each([
        { markAspect: 'Commitment' as const },
        { markDueFrom: '2026-09-03T00:00' },
        { markDueTo: '2026-09-10T00:00' },
    ])('reports %o', (criterion) => {
        expect(narrowedByReading({ ...openingListing.filters, ...criterion })).toBe(true);
    });
});

describe('narrowingsInForce', () => {
    it('counts nothing on a folder nobody has narrowed', () => {
        expect(narrowingsInForce(openingListing)).toBe(0);
    });

    it.each([{ unread: true }, { flagged: true }, { hasAttachments: true }])('counts %o as one', (filter) => {
        expect(narrowingsInForce({ ...openingListing, filters: { ...openingListing.filters, ...filter } })).toBe(1);
    });

    it('counts a range as one however many of its two ends are set', () => {
        const filters = { ...openingListing.filters, receivedFrom: '2026-08-01T00:00', receivedTo: '2026-09-01T00:00' };

        expect(narrowingsInForce({ ...openingListing, filters })).toBe(1);
    });

    it('counts what a derivation read as one however many of its three criteria are in force', () => {
        const filters = {
            ...openingListing.filters,
            markAspect: 'Commitment' as const,
            markDueFrom: '2026-09-03T00:00',
            markDueTo: '2026-09-10T00:00',
        };

        expect(narrowingsInForce({ ...openingListing, filters })).toBe(1);
    });

    it('counts reading the folder the other way round, which is why mail is not where somebody expected it', () => {
        expect(narrowingsInForce({ ...openingListing, order: 'oldestFirst' })).toBe(1);
    });

    it('counts reaching into junk as nothing, because it can never be why a folder looks emptier', () => {
        const filters = { ...openingListing.filters, includeJunk: true };

        expect(narrowingsInForce({ ...openingListing, filters })).toBe(0);
    });

    it('counts every narrowing the reader chose', () => {
        const listing: MailListing = {
            order: 'oldestFirst',
            filters: {
                unread: true,
                flagged: true,
                hasAttachments: true,
                includeJunk: true,
                dateRange: 'today',
                receivedFrom: '2026-09-03T00:00',
                receivedTo: null,
                markAspect: 'Commitment',
                markDueFrom: '2026-09-03T00:00',
                markDueTo: '2026-09-10T00:00',
            },
        };

        expect(narrowingsInForce(listing)).toBe(6);
    });
});

describe('narrowedToRange', () => {
    // Picked mid-afternoon on a Thursday, so that a span reckoned from the instant rather than from the start of the
    // reader's day would be visibly wrong rather than off by an hour.
    const pickedAt = new Date(2026, 8, 3, 15, 42);

    it.each([
        ['today', '2026-09-03T00:00'],
        ['lastSevenDays', '2026-08-28T00:00'],
        ['lastThirtyDays', '2026-08-05T00:00'],
        ['thisYear', '2026-01-01T00:00'],
    ] as const)('begins %s at %s in the reader’s own day', (range, expected) => {
        expect(narrowedToRange(openingListing.filters, range, pickedAt).receivedFrom).toBe(expected);
    });

    it('runs every offered span up to now rather than to an end of its own', () => {
        expect(narrowedToRange(openingListing.filters, 'today', pickedAt).receivedTo).toBeNull();
    });

    it('replaces a pair the reader typed, so a span and a typed range are never both in force', () => {
        const typed = { ...openingListing.filters, receivedFrom: '2026-01-05T09:00', receivedTo: '2026-01-06T09:00' };
        const inForce = narrowedToRange(typed, 'today', pickedAt);

        expect(inForce.dateRange).toBe('today');
        expect(inForce.receivedFrom).toBe('2026-09-03T00:00');
        expect(inForce.receivedTo).toBeNull();
    });

    it('takes the span off and leaves the list unnarrowed by date', () => {
        const inForce = narrowedToRange(openingListing.filters, 'thisYear', pickedAt);
        const taken = narrowedToRange(inForce, null, pickedAt);

        expect(taken.dateRange).toBeNull();
        expect(taken.receivedFrom).toBeNull();
        expect(taken.receivedTo).toBeNull();
    });

    it('leaves every other narrowing where it was', () => {
        const filters = { ...openingListing.filters, unread: true, includeJunk: true };
        const inForce = narrowedToRange(filters, 'today', pickedAt);

        expect(inForce.unread).toBe(true);
        expect(inForce.includeJunk).toBe(true);
    });
});

describe('selectableRange', () => {
    it.each([
        ['2026-08-01T00:00', '2026-09-01T00:00', true],
        ['2026-08-01T00:00', '2026-08-01T00:00', true],
        ['2026-09-01T00:00', '2026-08-01T00:00', false],
        [null, '2026-08-01T00:00', true],
        ['2026-08-01T00:00', null, true],
        [null, null, true],
    ])('reads %s to %s as a range that can select something: %s', (from, to, selects) => {
        expect(selectableRange(from, to)).toBe(selects);
    });
});

describe('narrowedToView', () => {
    // A Thursday mid-afternoon, so that a window reckoned from the instant rather than from the start of the reader's
    // day would be visibly wrong rather than off by an hour.
    const pickedAt = new Date(2026, 8, 3, 15, 42);

    it.each([
        ['needsDecision', 'Significance'],
        ['commitments', 'Commitment'],
        ['deadlinesThisWeek', 'Commitment'],
    ] as const)('stands %s on the %s a derivation recorded', (view, aspect) => {
        expect(narrowedToView(openingListing.filters, view, pickedAt).markAspect).toBe(aspect);
    });

    it('bounds the deadlines to the reader’s own week, opening today and closing on the eighth day', () => {
        const inForce = narrowedToView(openingListing.filters, 'deadlinesThisWeek', pickedAt);

        expect(inForce.markDueFrom).toBe('2026-09-03T00:00');
        expect(inForce.markDueTo).toBe('2026-09-10T00:00');
    });

    it.each(['needsDecision', 'commitments'] as const)('bounds %s by no due window at all', (view) => {
        const inForce = narrowedToView(openingListing.filters, view, pickedAt);

        expect(inForce.markDueFrom).toBeNull();
        expect(inForce.markDueTo).toBeNull();
    });

    it('replaces the window a previous view put in force, so two views are never half in force at once', () => {
        const week = narrowedToView(openingListing.filters, 'deadlinesThisWeek', pickedAt);
        const inForce = narrowedToView(week, 'needsDecision', pickedAt);

        expect(inForce.markAspect).toBe('Significance');
        expect(inForce.markDueFrom).toBeNull();
        expect(inForce.markDueTo).toBeNull();
    });

    it('leaves every narrowing the reader had already chosen where it was', () => {
        const filters = { ...openingListing.filters, unread: true, receivedFrom: '2026-08-01T00:00' };
        const inForce = narrowedToView(filters, 'commitments', pickedAt);

        expect(inForce.unread).toBe(true);
        expect(inForce.receivedFrom).toBe('2026-08-01T00:00');
    });

    it('writes what it puts in force into the filters the panel draws, so each criterion can be taken off there', () => {
        for (const view of standingViews) {
            expect(narrowedByReading(narrowedToView(openingListing.filters, view, pickedAt))).toBe(true);
        }
    });
});

describe('narrowedToThisWeek', () => {
    it('opens the window at the start of the reader’s day rather than at the instant they pressed', () => {
        const inForce = narrowedToThisWeek(openingListing.filters, new Date(2026, 8, 3, 23, 59));

        expect(inForce.markDueFrom).toBe('2026-09-03T00:00');
    });

    it('runs the window across a month’s end in the reader’s own calendar', () => {
        const inForce = narrowedToThisWeek(openingListing.filters, new Date(2026, 8, 28, 9, 0));

        expect(inForce.markDueFrom).toBe('2026-09-28T00:00');
        expect(inForce.markDueTo).toBe('2026-10-05T00:00');
    });

    it('leaves the reading the list is narrowed by where it was, being a window rather than a view', () => {
        const filters = { ...openingListing.filters, markAspect: 'Sense' as const };

        expect(narrowedToThisWeek(filters, new Date(2026, 8, 3)).markAspect).toBe('Sense');
    });
});
