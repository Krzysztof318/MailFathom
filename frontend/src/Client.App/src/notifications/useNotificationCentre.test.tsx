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
    ClientSignal,
    MailFathomTransport,
    NotificationTarget,
} from '@mailfathom/client-backend';
import { deviceKeys } from '../device/deviceStore';
import { LocalizationProvider } from '../localization/Localization';
import { SystemNotifierContext, type NotificationRaised, type SystemNotifier } from '../shellOperations/systemNotifier';
import {
    SignalledChangesContext,
    nothingSignalled,
    type SignalListener,
    type SignalledChanges,
} from '../signals/signalledChanges';
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

/** What a transport answers with, named because a test that holds one back has to say what it is holding. */
interface Answer {
    status: number;
    headers: Record<string, string>;
    body: string;
}

function answer(body: string): Promise<Answer> {
    return Promise.resolve({ status: 200, headers: {}, body });
}

/** Where the last notification followed led, which is the frame's answer rather than the hook's. */
let led: NotificationTarget | null = null;

/** Every sentence the head this run is in was asked to raise, and what that head answered when it was asked. */
interface Head {
    readonly said: string[];
    offered: boolean;

    /** What this head answers when it is asked to raise one, which is three answers rather than two. */
    answers: NotificationRaised;

    /**
     * Whether this head reports somebody acting on one at all, which is the second thing a head either carries or does
     * not: the desktop one does, and the web one subscribes to nothing.
     */
    reportsActing: boolean;

    /** Somebody acting on the notification the operating system showed, or `null` where nothing is subscribed. */
    act: (() => void) | null;

    /** How many subscriptions the hook has left listening, which is what proves it stops one it started. */
    listening: number;
}

const head: Head = { said: [], offered: true, answers: 'raised', reportsActing: true, act: null, listening: 0 };

const shell: SystemNotifier = {
    get offered() {
        return head.offered;
    },
    raise: (said) => {
        if (head.answers !== 'raised') {
            return Promise.resolve(head.answers);
        }

        head.said.push(said);

        return Promise.resolve('raised');
    },
    whenActedOn: (act) => {
        if (!head.reportsActing) {
            return () => undefined;
        }

        head.act = act;
        head.listening += 1;

        return () => {
            head.act = null;
            head.listening -= 1;
        };
    },
};

/** Whether somebody is looking at the window, which is the one thing the raising rule turns on. */
function looking(at: boolean): void {
    vi.spyOn(document, 'hasFocus').mockReturnValue(at);
}

/** Who the tree is signed in as, read while the wrapper renders so one mounted tree can be handed a second credential. */
let signedInAs: ClientSession | null = session;

/** A deployment a test speaks for, so what a raised notification does to the bell is asserted rather than polled for. */
function deploymentSaying(): { changes: SignalledChanges; say: (signal: ClientSignal) => void } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
        },
        say: (signal) => {
            for (const listener of [...listeners]) {
                listener(signal);
            }
        },
    };
}

function centreOf(
    transport: MailFathomTransport,
    signedIn: ClientSession | null = session,
    changes: SignalledChanges = nothingSignalled,
): {
    result: { current: NotificationCentre };
    signInAsSomebodyElse: () => void;
    rerender: () => void;
    unmount: () => void;
} {
    signedInAs = signedIn;

    const view = renderHook(
        () =>
            useNotificationCentre(signedInAs, transport, true, (target) => {
                led = target;
            }),
        {
            wrapper: ({ children }: { readonly children: ReactNode }) => (
                <LocalizationProvider>
                    <ToastsProvider>
                        <SignalledChangesContext value={changes}>
                            <SystemNotifierContext value={shell}>{children}</SystemNotifierContext>
                        </SignalledChangesContext>
                    </ToastsProvider>
                </LocalizationProvider>
            ),
        },
    );

    return {
        result: view.result,
        rerender: view.rerender,
        unmount: view.unmount,
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

/**
 * Two messages and a calendar reminder arriving at a client nobody has opened the panel on.
 *
 * Two polls rather than one, because the first read a client makes is what it holds rather than what just happened and
 * announces nothing — so the arrival being tested is the second, which is also what the desktop head actually meets.
 */
async function threeArrive(): Promise<void> {
    const { transport, hold } = deployment({ unreadCount: 0, notifications: [] });

    centreOf(transport);
    await settled();
    hold(1, [mail]);
    await polled();
    hold(4, [{ ...mail, id: 'n-second' }, { ...mail, id: 'n-third' }, { ...meeting, read: false }, mail]);
    await polled();
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
    head.said.length = 0;
    head.offered = true;
    head.answers = 'raised';
    head.reportsActing = true;
    head.act = null;
    head.listening = 0;

    // Nobody is looking at the window unless a test says they are, which is the state a system notification exists for.
    looking(false);
});

afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    window.localStorage.removeItem(deviceKeys.systemNotifications);
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

    it('tells the operating system how many arrived and of what kind, and nothing a message carried', async () => {
        await threeArrive();

        expect(head.said).toEqual(['2 new messages and 1 calendar reminder']);
    });

    it('raises nothing while somebody is looking at the window, the client having said it on the screen already', async () => {
        looking(true);

        await threeArrive();

        expect(head.said).toEqual([]);
    });

    it('raises nothing where the head offered no such operation, which is the web head unchanged', async () => {
        head.offered = false;

        await threeArrive();

        expect(head.said).toEqual([]);
    });

    it('opens the centre where somebody acted on the notification the operating system showed', async () => {
        const { transport } = deployment({ unreadCount: 0, notifications: [] });
        const { result } = centreOf(transport);

        await settled();

        expect(result.current.shown).toBe(false);

        act(() => {
            head.act?.();
        });

        expect(result.current.shown).toBe(true);
    });

    it('subscribes to nothing where the head reports no acting, which is the web head unchanged', async () => {
        head.reportsActing = false;

        const { transport } = deployment({ unreadCount: 0, notifications: [] });
        const { result } = centreOf(transport);

        await settled();

        expect(head.act).toBeNull();
        expect(head.listening).toBe(0);
        expect(result.current.shown).toBe(false);
    });

    it('stops listening for one when the centre goes away', async () => {
        const { transport } = deployment({ unreadCount: 0, notifications: [] });
        const { unmount } = centreOf(transport);

        await settled();

        expect(head.listening).toBe(1);

        unmount();

        expect(head.listening).toBe(0);
    });

    it('raises nothing on a machine that was left not raising them', async () => {
        window.localStorage.setItem(deviceKeys.systemNotifications, 'false');

        await threeArrive();

        expect(head.said).toEqual([]);
    });

    it('leaves this machine not raising them once the operating system has refused one', async () => {
        head.answers = 'refused';

        await threeArrive();
        await settled();

        expect(window.localStorage.getItem(deviceKeys.systemNotifications)).toBe('false');
    });

    it('decides nothing on this machine where the head carries no such operation, nobody having been asked', async () => {
        head.answers = 'unavailable';

        await threeArrive();
        await settled();

        expect(window.localStorage.getItem(deviceKeys.systemNotifications)).toBeNull();
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

    // A read is cancelled by the controller its effect owns; a write cannot be, so what it does when it lands is the
    // only guard there is. Both markings are asserted rather than one, because each has a continuation of its own.
    it.each([
        [
            'one row',
            (centre: NotificationCentre) => {
                centre.markRead(['n-mail'], true);
            },
        ],
        [
            'everything',
            (centre: NotificationCentre) => {
                centre.markAllRead();
            },
        ],
    ])('drops the answer to a marking of %s that lands after somebody else has signed in', async (_named, mark) => {
        let landing: ((answered: Answer) => void) | null = null;
        const held = { unreadCount: 1 };

        const transport: MailFathomTransport = (request) => {
            if (request.path.endsWith('/read-state') || request.path.endsWith('/read')) {
                return new Promise<Answer>((resolve) => {
                    landing = resolve;
                });
            }

            return request.path.endsWith('/unread-count')
                ? answer(JSON.stringify({ unreadCount: held.unreadCount }))
                : answer(JSON.stringify({ notifications: [mail], nextCursor: null }));
        };

        const { result, signInAsSomebodyElse } = centreOf(transport);

        act(() => {
            result.current.show();
        });
        await settled();

        act(() => {
            mark(result.current);
        });

        held.unreadCount = 2;
        act(() => {
            signInAsSomebodyElse();
        });
        await settled();

        expect(result.current.unreadCount).toBe(2);

        // The previous person's mailbox answering, long after they signed out. It says nine, and nine is the one
        // number the person now reading must never be shown.
        act(() => {
            landing?.({
                status: 200,
                headers: {},
                body: JSON.stringify({ id: 'n-mail', read: true, markedRead: 1, unreadCount: 9 }),
            });
        });
        await settled();

        expect(result.current.unreadCount).toBe(2);
    });

    it('says why the page could not be read rather than drawing an empty centre', async () => {
        const { result } = centreOf(() => Promise.resolve({ status: 500, headers: {}, body: '' }));

        act(() => {
            result.current.show();
        });
        await settled();

        expect(result.current.failure).toBe('unavailable');
    });
    it('reads the count when the deployment says a notification was raised, without waiting out the interval', async () => {
        const signalling = deploymentSaying();
        const held = { unreadCount: 1, notifications: [mail] };
        const { transport, hold } = deployment(held);

        const { result } = centreOf(transport, session, signalling.changes);

        await settled();
        expect(result.current.unreadCount).toBe(1);

        hold(4);
        await act(async () => {
            signalling.say({
                kind: 'notification.raised',
                notificationKind: 'Mail',
                headline: 'Ada Lovelace wrote',
                secondLine: 'About the engine',
                unreadCount: 4,
            });
            await vi.advanceTimersByTimeAsync(0);
        });

        expect(result.current.unreadCount).toBe(4);
    });

    it('reads nothing about a signal that is not about a notification', async () => {
        const signalling = deploymentSaying();
        const held = { unreadCount: 1, notifications: [mail] };
        const { transport, requests } = deployment(held);

        centreOf(transport, session, signalling.changes);
        await settled();

        const before = requests.length;

        await act(async () => {
            signalling.say({ kind: 'account.state', account: 'work' });
            await vi.advanceTimersByTimeAsync(0);
        });

        expect(requests.length).toBe(before);
    });
});
