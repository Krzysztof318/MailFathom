// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailEnrichment, MailTimelineEntry } from '@mailfathom/client-backend';
import { rowMark, rowWithFlags, sameRow } from './rowContents';

function message(over: Partial<MailTimelineEntry> = {}): MailTimelineEntry {
    return {
        id: 'message-1',
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: 'The analytical engine',
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: '2026-08-31T09:40:00+00:00',
        senderAddress: 'ada@example.invalid',
        senderDisplayName: 'Ada Lovelace',
        toAddresses: ['charles@example.invalid'],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 1_024,
        preview: null,
        enrichment: null,
        threadMessageCount: null,
        ...over,
    };
}

function derivation(text: string, over: Partial<MailEnrichment> = {}): MailEnrichment {
    return {
        derivedAt: '2026-08-31T10:00:00+00:00',
        marks: [
            {
                aspect: 'Sense',
                text,
                reason: 'The subject names it',
                dueAt: null,
                source: 'DeterministicRule',
                origin: 'subject-rule',
                evidence: [],
            },
        ],
        ...over,
    };
}

describe('rowMark', () => {
    it('reads a row the deployment merely wrote down again as the same row', () => {
        const drawn = rowMark(message());
        const again = rowMark(message());

        expect(sameRow(drawn, again)).toBe(true);
    });

    it('reads a message whose subject moved as a row that changed', () => {
        const drawn = rowMark(message());
        const again = rowMark(message({ subject: 'The difference engine' }));

        expect(sameRow(drawn, again)).toBe(false);
    });

    it.each<Partial<MailTimelineEntry>>([
        { senderDisplayName: 'A. Lovelace' },
        { senderAddress: 'ada@example.test' },
        { receivedAt: '2026-08-31T11:00:00+00:00' },
        { unread: true },
        { flagged: true },
        { answered: true },
        { hasAttachments: true },
        { attachmentCount: 2 },
        { threadMessageCount: 3 },
        { enrichment: derivation('It asks for the minutes') },
    ])('reads a row drawing %o differently as a row that changed', (moved) => {
        expect(sameRow(rowMark(message()), rowMark(message(moved)))).toBe(false);
    });

    it.each<Partial<MailTimelineEntry>>([
        { preview: 'Dear Charles, the engine now' },
        { sentAt: '2026-08-20T09:00:00+00:00' },
        { toAddresses: ['charles@example.invalid', 'luigi@example.invalid'] },
        { sizeOctets: 8_192 },
        { threadId: 'thread-4' },
    ])('reads a message whose %o moved as the same row, no row drawing it', (moved) => {
        expect(sameRow(rowMark(message()), rowMark(message(moved)))).toBe(true);
    });

    it('reads a derivation that only rewrote what no row draws as the same row', () => {
        const first = rowMark(message({ enrichment: derivation('It asks for the minutes') }));
        const second = rowMark(
            message({
                enrichment: derivation('It asks for the minutes', { derivedAt: '2026-09-01T10:00:00+00:00' }),
            }),
        );

        expect(sameRow(first, second)).toBe(true);
    });
});

describe('rowWithFlags', () => {
    it('takes the read mark the deployment stated', () => {
        const drawn = rowMark(message({ unread: true }));

        expect(rowWithFlags(drawn, { email: 'message-1', isSeen: true, isFlagged: null }).unread).toBe(false);
    });

    it('keeps a flag the statement is silent about, that being a flag nobody observed', () => {
        const drawn = rowMark(message({ flagged: true }));

        expect(rowWithFlags(drawn, { email: 'message-1', isSeen: false, isFlagged: null }).flagged).toBe(true);
    });

    it('comes out as the answer that page would next carry, so the row is washed once and not again', () => {
        const drawn = rowMark(message({ unread: true, flagged: false }));
        const stated = rowWithFlags(drawn, { email: 'message-1', isSeen: true, isFlagged: true });
        const read = rowMark(message({ unread: false, flagged: true }));

        expect(sameRow(stated, read)).toBe(true);
    });
});
