// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { sendToDeployment } from './sendToDeployment';

// What the transport puts on the request, which is the one thing about this module no constructed `Response` can
// answer: the credentials mode is a property of what went out rather than of what came back. So `fetch` itself is what
// is watched here, and it is watched rather than replaced by a fake network — `frontend/tests/AGENTS.md` § *What is
// faked, and where* refuses the second, and this is the module that calls it. What an answer amounts to is asked of an
// answer this file could construct, exactly as `attachmentUpload.ts` beside it splits the same question.

afterEach(() => {
    vi.restoreAllMocks();
});

describe('sendToDeployment', () => {
    it('omits the credentials, so a challenge reaches the client rather than the browser own dialog', async () => {
        const sent = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }));

        await sendToDeployment(new AbortController().signal)({
            method: 'GET',
            path: 'https://mail.example/api/client/session',
            headers: { Accept: 'application/json' },
        });

        expect(sent).toHaveBeenCalledWith(
            'https://mail.example/api/client/session',
            expect.objectContaining({ credentials: 'omit' }),
        );
    });
});
