// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailSearchAttachmentMatch, MailSearchResult } from '@mailfathom/client-backend';
import { citedFileOf, explainingFileOf } from './citedFile';

const document: MailSearchAttachmentMatch = {
    attachmentPosition: 2,
    fileName: 'terms.pdf',
    mediaType: 'application/pdf',
    source: 'Document',
    segmentKind: 'Page',
    segmentNumber: 4,
    extracts: ['**Terms** of the renewal'],
};

const picture: MailSearchAttachmentMatch = {
    attachmentPosition: 0,
    fileName: 'photo.jpg',
    mediaType: 'image/jpeg',
    source: 'ImageDescription',
    segmentKind: null,
    segmentNumber: null,
    extracts: ['A signed delivery note'],
};

function found(carried: Partial<MailSearchResult> = {}): MailSearchResult {
    return {
        id: 'message-1',
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: 'The renewal',
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: 'billing@example.invalid',
        senderDisplayName: 'Billing',
        toAddresses: ['user@example.invalid'],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: true,
        attachmentCount: 2,
        sizeOctets: 4_096,
        preview: 'The renewal falls due.',
        snippets: [],
        matchedBy: 'LexicalRanking',
        attachmentMatches: [],
        isDepictedMatch: false,
        ...carried,
    };
}

describe('citedFileOf', () => {
    it('answers with nothing where the query reached no file this message carries', () => {
        expect(citedFileOf(found())).toBeUndefined();
    });

    it('prefers the words somebody wrote over a description of a picture, whichever came first', () => {
        expect(citedFileOf(found({ attachmentMatches: [picture, document] }))).toStrictEqual(document);
    });

    it('names the described picture where it is the only file the query reached', () => {
        expect(citedFileOf(found({ attachmentMatches: [picture] }))).toStrictEqual(picture);
    });
});

describe('explainingFileOf', () => {
    it('names the file where the deployment cut no extract of the message own words', () => {
        expect(explainingFileOf(found({ attachmentMatches: [document] }))).toStrictEqual(document);
    });

    it('names no file where the message own extract is what the row shows', () => {
        const alsoInAFile = found({ snippets: ['The **renewal** falls due'], attachmentMatches: [document] });

        expect(explainingFileOf(alsoInAFile)).toBeUndefined();
    });
});
