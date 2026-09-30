// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { useReplyDraftingState } from './useReplyDraftingState';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };
const signedInAs = 'https://mail.example.invalid\nagata';
const answering = '00000000-0000-4000-8000-000000000042';

const drafted: ClientResponse = {
    status: 200,
    body: JSON.stringify({ drafted: true, body: 'We accept the terms.', claims: [], proposedRecipients: [] }),
    headers: {},
};

function Providers({ children }: { readonly children: ReactNode }) {
    return (
        <LocalizationProvider>
            <ToastsProvider>{children}</ToastsProvider>
        </LocalizationProvider>
    );
}

function drafting(transport: MailFathomTransport) {
    return renderHook(() => useReplyDraftingState(signedInAs, session, transport), { wrapper: Providers });
}

describe('useReplyDraftingState', () => {
    it('keeps nothing of an answer that arrives after the reply was let go', async () => {
        const waiting: (() => void)[] = [];
        const discarded = drafting(
            () =>
                new Promise((resolve) => {
                    waiting.push(() => {
                        resolve(drafted);
                    });
                }),
        );
        const alongside = drafting(() => Promise.resolve(drafted));

        act(() => {
            discarded.result.current.draftReply(answering, '', null);
        });
        await waitFor(() => {
            expect(waiting).toHaveLength(1);
        });
        act(() => {
            discarded.result.current.letGo();
        });
        act(() => {
            waiting[0]?.();
        });

        // The same route asked afterwards, beside it, whose answer is read through the same steps and so cannot be
        // drawn before the late one has been.
        act(() => {
            alongside.result.current.draftReply(answering, '', null);
        });
        await waitFor(() => {
            expect(alongside.result.current.draft?.body).toBe('We accept the terms.');
        });

        expect(discarded.result.current.draft).toBeNull();
    });
});
