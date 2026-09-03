// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { ReadMarkingProvider } from './ReadMarking';
import { drawnUnread, useReadMarking, type MessageOpened } from './useReadMarking';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const somebodyElse: ClientSession = { ...session, authorization: 'Basic b3RoZXI=' };

function opened(storedEmailId: string, message: Partial<MessageOpened> = {}): MessageOpened {
    return { storedEmailId, account: 'work', folder: 'INBOX', unread: true, ...message };
}

// The transport is the network boundary and the whole of what these tests fake. Every message named in a submission is
// answered as recorded unless a test says otherwise, which is what the deployment does with mail the list just drew.
function recording(outcomes: Readonly<Record<string, string>> = {}): {
    transport: MailFathomTransport;
    requests: ClientRequest[];
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const stated = JSON.parse(request.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({
                    results: stated.changes.map(({ storedEmailId }) => ({
                        storedEmailId,
                        outcome: outcomes[storedEmailId] ?? 'recorded',
                    })),
                }),
            });
        },
    };
}

function marking(
    transport: MailFathomTransport,
    { marking = true, asked }: { marking?: boolean; asked?: ClientSession | null } = {},
) {
    const signedIn = asked === undefined ? session : asked;

    return renderHook(() => useReadMarking(), {
        wrapper: ({ children }) => (
            <ReadMarkingProvider session={signedIn} transport={transport} marking={marking}>
                {children}
            </ReadMarkingProvider>
        ),
    });
}

function named(requests: readonly ClientRequest[]): readonly (readonly string[])[] {
    return requests.map((request) => {
        const stated = JSON.parse(request.body ?? '{}') as { changes: readonly { storedEmailId: string }[] };

        return stated.changes.map(({ storedEmailId }) => storedEmailId);
    });
}

describe('ReadMarkingProvider', () => {
    it('submits one change for a message whose body was drawn, and draws its row read at once', async () => {
        const { transport, requests } = recording();
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first'));
        });

        expect(drawnUnread(result.current, 'first', true)).toBe(false);

        await waitFor(() => {
            expect(named(requests)).toStrictEqual([['first']]);
        });
    });

    // The pane reports the body drawn rather than the selection moved, and asking for the sender's pictures re-reads
    // the same message — so the one thing this must never do is report a message opened twice.
    it('submits nothing a second time for a message already marked', async () => {
        const { transport, requests } = recording();
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first'));
        });

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        act(() => {
            result.current.markRead(opened('first'));
        });

        expect(named(requests)).toStrictEqual([['first']]);
    });

    it('submits nothing for a message the deployment already reports as read', () => {
        const { transport, requests } = recording();
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first', { unread: false }));
        });

        expect(requests).toStrictEqual([]);
        expect(result.current.marked.size).toBe(0);
    });

    // A conversation draws every message it shows, and each of them says so — so what the deployment is told is one
    // batch naming them all rather than one request per message.
    it('carries the bodies drawn together in one batch', async () => {
        const { transport, requests } = recording();
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first'));
            result.current.markRead(opened('second'));
        });

        await waitFor(() => {
            expect(named(requests)).toStrictEqual([['first', 'second']]);
        });
    });

    it('marks nothing where the reader turned it off or the credential may not write a flag', () => {
        const { transport, requests } = recording();
        const { result } = marking(transport, { marking: false });

        act(() => {
            result.current.markRead(opened('first'));
        });

        expect(requests).toStrictEqual([]);
        expect(drawnUnread(result.current, 'first', true)).toBe(true);
    });

    it('marks nothing where there is nobody to submit for', () => {
        const { transport, requests } = recording();
        const { result } = marking(transport, { asked: null });

        act(() => {
            result.current.markRead(opened('first'));
        });

        expect(requests).toStrictEqual([]);
        expect(result.current.marked.size).toBe(0);
    });

    // A row that stayed read against a mailbox nobody told is the defect this exists for: the deployment answering
    // anything but `recorded` puts the message back to what the list said about it.
    it('stops claiming a message read where the deployment did not write it down', async () => {
        const { transport } = recording({ first: 'message-not-found' });
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first'));
        });

        await waitFor(() => {
            expect(drawnUnread(result.current, 'first', true)).toBe(true);
        });
    });

    it('stops claiming a message read where the deployment could not be reached', async () => {
        const { result } = marking(() => Promise.reject(new Error('the connection was refused')));

        act(() => {
            result.current.markRead(opened('first'));
        });

        expect(drawnUnread(result.current, 'first', true)).toBe(false);

        await waitFor(() => {
            expect(drawnUnread(result.current, 'first', true)).toBe(true);
        });
    });

    it('says where each marked message was counted, so a folder’s count can answer for it', async () => {
        const { transport } = recording();
        const { result } = marking(transport);

        act(() => {
            result.current.markRead(opened('first', { account: 'personal', folder: 'ARCHIVE' }));
        });

        await waitFor(() => {
            expect(result.current.marked.get('first')).toStrictEqual({ account: 'personal', folder: 'ARCHIVE' });
        });
    });

    // Signing out and back in on one tab keeps this component mounted, and the previous person's markings would
    // otherwise be drawn over the next person's mail.
    it('holds nothing of the person who signed out, so the next one reads their own mailbox', async () => {
        const { transport } = recording();

        // Held beside the render rather than passed to it, because a wrapper receives no props of its own.
        let signedIn: ClientSession = session;

        const { result, rerender } = renderHook(() => useReadMarking(), {
            wrapper: ({ children }) => (
                <ReadMarkingProvider session={signedIn} transport={transport} marking>
                    {children}
                </ReadMarkingProvider>
            ),
        });

        act(() => {
            result.current.markRead(opened('first'));
        });

        await waitFor(() => {
            expect(result.current.marked.size).toBe(1);
        });

        signedIn = somebodyElse;
        rerender();

        expect(result.current.marked.size).toBe(0);
    });
});
