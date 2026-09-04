// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, renderHook, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { PendingChangesProvider } from '../pendingChanges/PendingChanges';
import { ToastsProvider } from '../toasts/Toasts';
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

    // What became of a submission is read by the queue that follows changes, and what that queue says is said on the
    // toast surface. Both are above this provider wherever the client actually runs, so both are above it here: a
    // marking proven against a tree that follows nothing would be proven against an arrangement nobody ships.
    return renderHook(() => useReadMarking(), {
        wrapper: ({ children }) => (
            <LocalizationProvider>
                <ToastsProvider>
                    <PendingChangesProvider session={signedIn} transport={transport}>
                        <ReadMarkingProvider session={signedIn} transport={transport} marking={marking}>
                            {children}
                        </ReadMarkingProvider>
                    </PendingChangesProvider>
                </ToastsProvider>
            </LocalizationProvider>
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

    // Asking again is the queue handing the change back to whoever submitted it, and what this component owes that is
    // the marking exactly as it was — the same account and the same folder — before the request goes out a second
    // time. A restore that lost where the message was counted would leave a folder's unread count answering for a
    // message it no longer holds.
    it('restores where a message was counted when the person asks for the change again', async () => {
        const { result } = marking(() => Promise.reject(new Error('the connection was refused')));

        act(() => {
            result.current.markRead(opened('first', { account: 'personal', folder: 'ARCHIVE' }));
        });

        await waitFor(() => {
            expect(drawnUnread(result.current, 'first', true)).toBe(true);
        });

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(result.current.marked.get('first')).toStrictEqual({ account: 'personal', folder: 'ARCHIVE' });
        expect(drawnUnread(result.current, 'first', true)).toBe(false);
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
