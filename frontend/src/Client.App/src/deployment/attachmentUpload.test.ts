// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest } from '@mailfathom/client-backend';
import { uploadAttachment } from './attachmentUpload';

const staging: ClientRequest = {
    method: 'POST',
    path: 'https://mail.example.invalid/api/client/drafts/d1/attachments?fileName=invoice.pdf',
    headers: { Authorization: 'Basic dGVzdA==', 'Content-Type': 'application/pdf' },
    longestAnswer: 64,
};

const file = new Blob(['0123'], { type: 'application/pdf' });

afterEach(() => {
    vi.unstubAllGlobals();
});

describe('uploadAttachment', () => {
    it('puts the octets on the wire as the whole of the request, under what the request states', async () => {
        const asked = vi.fn(() => Promise.resolve(new Response('{}', { status: 200 })));

        vi.stubGlobal('fetch', asked);

        const abandoning = new AbortController();
        const answer = await uploadAttachment(staging, file, abandoning.signal);

        expect(answer).toMatchObject({ status: 200, body: '{}' });
        expect(asked).toHaveBeenCalledWith(staging.path, {
            method: 'POST',
            headers: staging.headers,
            body: file,
            signal: abandoning.signal,
        });
    });

    it('answers nothing where nothing answered at all, which is what an abandoned upload also is', async () => {
        vi.stubGlobal('fetch', () => Promise.reject(new Error('The connection was refused.')));

        expect(await uploadAttachment(staging, file, new AbortController().signal)).toBeNull();
    });

    it('answers an empty body for one past what the request said it would read, which is refused as unreadable', async () => {
        vi.stubGlobal('fetch', () => Promise.resolve(new Response('x'.repeat(65), { status: 200 })));

        expect(await uploadAttachment(staging, file, new AbortController().signal)).toMatchObject({ body: '' });
    });
});
