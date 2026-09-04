// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import type {
    ClientNotification,
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    NotificationTarget,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { unreadCountInterval, useNotificationCentre, type NotificationCentre } from './useNotificationCentre';

// The centre is driven the way the frame drives it — one credential, one transport, and a deployment that answers on
// the wire — because what is being proven is what goes out and what a person is told: which reads happen when, what a
// marking does before the deployment has answered it, and what an arrival says out loud.
//
// The clock is fake throughout, so the interval between counts is asserted rather than sat through.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const somebodyElse: ClientSession = { ...session, authorization: 'Basic b3RoZXI=' };

const mail = {
    id: 'n-mail',
    kind: 'Mail',
    title: 'Ada Lovelace wrote',
    body: 'About the engine',
    source: 'Inbox',
    target: { kind: 'Message', messageId: 'm-9' },
    occurredAt: '2026-09-04T11:55:00Z',
    read: false,
};

const meeting = { ...mail, id: 'n-meeting', kind: 'Calendar', title: 'Standing meeting moved', read: true };

/**
 * A deployment answering every route this hook asks for, from what it is told to hold.
 *
 * The count and the page are separate answers here exactly as they are on the surface, so a test can move one without
 * the other — which is the whole shape the hook is built around.
 */
function deployment(held: { unreadCount: number; notifications: readonly unknown[]; refuseMarking?: boolean }): {
    transport: MailFathomTransport;
    requests: ClientRequest[];
    hold: (unreadCount: number, notifications?: readonly unknown[]) => void;
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        hold: (unreadCount, notifications) => {
            held.unreadCount = unreadCount;

            if (notifications !== undefined) {
                held.notifications = notifications;
            }
        },
        transport: (request) => {
            requests.push(request);

            if (request.path.endsWith('/unread-count')) {
                return answer(JSON.stringify({ unreadCount: held.unreadCount }));
            }

            if (request.path.endsWith('/read')) {
                return held.refuseMarking === true
                    ? Promise.resolve({ status: 500, headers: {}, body: '' })
                    : answer(JSON.stringify({ markedRead: held.unreadCount, unreadCount: 0 }));
            }

            if (request.path.endsWith('/read-state')) {
                return held.refuseMarking === true
                    ? Promise.resolve({ status: 500, headers: {}, body: '' })
                    : answer(JSON.stringify({ id: 'n-mail', read: true, unreadCount: 0 }));
            }

            return answer(JSON.stringify({ notifications: held.notifications, nextCursor: null }));
        },
    };
}

function answer(body: string): Promise<{ status: number; headers: Record<string, string>; body: string }> {
    return Promise.resolve({ status: 200, headers: {}, body });
}

/** Where the last notification followed led, which is the frame's answer rather than the hook's. */
let led: NotificationTarget | null = null;

/** Who the tree is signed in as, read while the wrapper renders so one mounted tree can be handed a second credential. */
let signedInAs: ClientSession | null = session;

function centreOf(
    transport: MailFathomTransport,
    signedIn: ClientSession | null = session,
): { result: { current: NotificationCentre }; signInAsSomebodyElse: () => void; rerender: () => void } {
    signedInAs = signedIn;

    const view = renderHook(
        () =>
            useNotificationCentre(signedInAs, transport, true, (target) => {
                led = target;
            }),
        {
            wrapper: ({ children }: { readonly children: ReactNode }) => (
                <LocalizationProvider>
                    <ToastsProvider>{children}</ToastsProvider>
                </LocalizationProvider>
            ),
        },
    );

    return {
        result: view.result,
        rerender: view.rerender,
        signInAsSomebodyElse: () => {
            signedInAs = somebodyElse;
            view.rerender();
        },
    };
}

/** Lets everything already started settle, which a fake clock does not do on its own. */
async function settled(): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
    });
}

/** Waits out one poll, which is what makes a count that moved reach the bell. */
async function polled(): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(unreadCountInterval);
    });
}

/** The one notification the centre holds, named rather than indexed so a test that drew none says so. */
function only(centre: NotificationCentre): ClientNotification {
    const [held] = centre.notifications;

    if (held === undefined) {
        throw new Error('The centre holds no notification to act on.');
    }

    return held;
}

function pathsAsked(requests: readonly ClientRequest[]): readonly string[] {
    return requests.map((request) => new URL(request.path).pathname);
}

beforeEach(() => {
    vi.useFakeTimers();
    led = null;
    signedInAs = session;
});

afterEach(() => {
    vi.useRealTimers();
});

describe('useNotificationCentre', () => {
    it('asks how much stands unread as soon as there is a credential to ask with', async () => {
        const { transport, requests } = deployment({ unreadCount: 3, notifications: [] });
        const { result } = centreOf(transport);

        await settled();

        expect(pathsAsked(requests)).toEqual(['/api/client/notifications/unread-count']);
        expect(result.current.unreadCount).toBe(3);
    });

    it('asks nothing at all while nobody is signed in', async () => {
        const { transport, requests } = deployment({ unreadCount: 3, notifications: [] });

        centreOf(transport, null);
        await settled();

        expect(requests).toHaveLength(0);
    });

    it('asks again on its own interval, so a count that moved reaches the bell without anybody acting', async () => {
        const { transport, requests, hold } = deployment({ unreadCount: 0, notifications: [] });
        const { result } = centreOf(transport);

        await settled();
        hold(2);
        await polled();

        expect(result.current.unreadCount).toBe(2);
        expect(requests).toHaveLength(3);
    });

    it('reads the page when the panel is opened, rather than keeping one nobody is looking at', async () => {
        const { transport, requests } = deployment({ unreadCount: 1, notifications: [mail] });
        const { result } = centreOf(transport);

        await settled();

        expect(pathsAsked(requests)).not.toContain('/api/client/notifications');

        act(() => {
            result.current.show();
        });
        await settled();

        expect(pathsAsked(requests)).toContain('/api/client/notifications');
        expect(result.current.notifications.map((row) => row.id)).toEqual(['n-mail']);
    });

    it('reads the page when the count rises, which is what an arrival looks like to a client that polls', async () => {
        const { transport, requests, hold } = deployment({ unreadCount: 0, notifications: [] });

        centreOf(transport);
        await settled();
        hold(1, [mail]);
        await polled();

        expect(pathsAsked(requests)).toContain('/api/client/notifications');
    });

    it('says an arrival out loud, with a way to go straight to it', async () => {
        const { transport, hold } = deployment({ unreadCount: 0, notifications: [] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();
        hold(1, [mail]);
        await polled();

        expect(screen.getByText('Ada Lovelace wrote')).toBeDefined();
        expect(screen.getByText('About the engine')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Show' })).toBeDefined();
    });

    it('announces nothing on the first read, a client opening being told what it has rather than what happened', async () => {
        const { transport } = deployment({ unreadCount: 2, notifications: [mail, meeting] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        expect(screen.queryByText('Ada Lovelace wrote')).toBeNull();
    });

    it('redraws a row as it is marked, rather than after the deployment has agreed', async () => {
        const { transport } = deployment({ unreadCount: 1, notifications: [mail] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.markRead(['n-mail'], true);
        });

        expect(result.current.notifications[0]?.read).toBe(true);
        expect(result.current.unreadCount).toBe(0);
    });

    it('takes a marking back and says so where the deployment refused it', async () => {
        const { transport } = deployment({ unreadCount: 1, notifications: [mail], refuseMarking: true });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.markRead(['n-mail'], true);
        });
        await settled();

        expect(result.current.notifications[0]?.read).toBe(false);
        expect(screen.getByText('That notification could not be marked. It stands as it did.')).toBeDefined();
    });

    it('marks everything read in one request rather than one per row', async () => {
        const { transport, requests } = deployment({ unreadCount: 2, notifications: [mail, { ...mail, id: 'n-two' }] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.markAllRead();
        });
        await settled();

        expect(pathsAsked(requests).filter((path) => path.endsWith('/notifications/read'))).toHaveLength(1);
        expect(result.current.unreadCount).toBe(0);
        expect(result.current.notifications.every((row) => row.read)).toBe(true);
    });

    it('takes marking everything back and says so where the deployment refused it', async () => {
        const { transport } = deployment({ unreadCount: 1, notifications: [mail], refuseMarking: true });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.markAllRead();
        });
        await settled();

        expect(result.current.notifications[0]?.read).toBe(false);
        expect(screen.getByText('Your notifications could not be marked read. They stand as they did.')).toBeDefined();
    });

    it('reads a notification, closes the panel, and goes where it leads, all from one press', async () => {
        const { transport, requests } = deployment({ unreadCount: 1, notifications: [mail] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.follow(only(result.current));
        });
        await settled();

        expect(led).toStrictEqual({ kind: 'Message', storedEmailId: 'm-9' });
        expect(result.current.shown).toBe(false);
        expect(result.current.unreadCount).toBe(0);
        expect(pathsAsked(requests).some((path) => path.endsWith('/read-state'))).toBe(true);
    });

    it('marks nothing again where the notification followed already stood read', async () => {
        const { transport, requests } = deployment({ unreadCount: 0, notifications: [meeting] });
        const { result } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            result.current.follow(only(result.current));
        });
        await settled();

        expect(pathsAsked(requests).some((path) => path.endsWith('/read-state'))).toBe(false);
    });

    it('starts from nothing when somebody else signs in, rather than showing the last person’s centre', async () => {
        const { transport } = deployment({ unreadCount: 4, notifications: [mail] });
        const { result, signInAsSomebodyElse } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        expect(result.current.unreadCount).toBe(4);

        act(() => {
            signInAsSomebodyElse();
        });

        expect(result.current.unreadCount).toBe(0);
        expect(result.current.notifications).toEqual([]);
        expect(result.current.shown).toBe(false);
    });

    it('says why the page could not be read rather than drawing an empty centre', async () => {
        const { result } = centreOf(() => Promise.resolve({ status: 500, headers: {}, body: '' }));

        act(() => {
            result.current.show();
        });
        await settled();

        expect(result.current.failure).toBe('unavailable');
    });
});
