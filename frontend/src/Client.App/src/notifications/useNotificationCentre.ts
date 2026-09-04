// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    markAllNotificationsRead,
    readNotifications,
    readUnreadNotificationCount,
    setNotificationRead,
    type ClientFailureReason,
    type ClientNotification,
    type ClientSession,
    type MailFathomTransport,
    type NotificationTarget,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { chooseSystemNotifications, systemNotificationsChosen } from '../preferences/systemNotifications';
import { useSystemNotifier } from '../shellOperations/systemNotifier';
import { useSignalledChanges } from '../signals/signalledChanges';
import { arrivalCounts } from './arrivalCounts';
import { notificationToastKinds, systemNotificationCounts } from './notificationKinds';
import { useToasts } from '../toasts/useToasts';

// What the client knows about the person's notification centre, and everything that changes it. It is one hook rather
// than a store beside the frame because all of it belongs to one credential and goes with it: the count on the bell,
// the page the panel draws, and the two ways of marking something read are one reading of one thing.
//
// **The count is asked for on an interval, and again whenever anything says it should be.** The interval is the floor:
// a deployment that raised a notification says so over the signal channel and the count is read then, and the window
// coming back to the front reads it as well — the moment a person is most likely to be looking at the bell and the
// least likely to have had a poll land. A deployment serving no channel, or one this client cannot reach, therefore
// behaves exactly as it did before there was one. The list is asked for when there is somewhere to draw it and when
// the count says something has arrived, never on the interval: a badge that cost a screenful of rows to draw would be
// the most expensive thing on a polling client's schedule.
//
// **A read state change goes out as it is made.** The row is redrawn from what was asked for and the badge from what
// the deployment answered, so one exchange settles both; a change that failed is said out loud and taken back rather
// than left on the screen as something that did not happen.

/** How long the client waits between asking how much stands unread. */
export const unreadCountInterval = 60_000;

/** How many notifications the panel asks for, which is the window it draws rather than the whole centre. */
export const notificationsShown = 50;

/** The most toasts one arrival raises, so a burst that landed between two polls does not bury the screen. */
export const mostArrivalsAnnounced = 3;

/** Which of the two tabs the panel is filtering by. */
export type NotificationFilter = 'all' | 'unread';

export interface NotificationCentre {
    /** How many of the person's notifications stand unread, which is the whole of what the bell draws. */
    readonly unreadCount: number;

    readonly shown: boolean;

    /** The window of the centre the panel draws, newest first. */
    readonly notifications: readonly ClientNotification[];

    /** Whether the page behind the panel is being read for the first time, which is what the panel says while it waits. */
    readonly reading: boolean;

    /** Why the page could not be read, or `null` where it was. */
    readonly failure: ClientFailureReason | null;

    readonly show: () => void;
    readonly hide: () => void;

    /** Puts one notification into the read state stated, which is what the row's own control does. */
    readonly markRead: (ids: readonly string[], read: boolean) => void;

    /** Marks every unread notification read, in one request. */
    readonly markAllRead: () => void;

    /** Reads one notification and goes where it leads, which is what a click on the row does. */
    readonly follow: (notification: ClientNotification) => void;
}

/** Which notifications have already been drawn or announced, and whose reading of the centre that was. */
interface Seen {
    readonly session: ClientSession;
    readonly ids: ReadonlySet<string>;
}

/** What the last count answered, and whose it was. */
interface Counted {
    readonly session: ClientSession;
    readonly count: number;
}

/**
 * Holds the centre for as long as one credential does.
 *
 * @param session Who is asking, or `null` where nobody is signed in or the credential may not read mail.
 * @param transport How a request reaches the deployment.
 * @param online Whether this machine has a network, which is what stops the interval rather than a failing read.
 * @param onFollow Where a notification leads, which is the frame's answer rather than this hook's.
 */
export function useNotificationCentre(
    session: ClientSession | null,
    transport: MailFathomTransport,
    online: boolean,
    onFollow: (target: NotificationTarget) => void,
): NotificationCentre {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const signalledChanges = useSignalledChanges();
    const notifier = useSystemNotifier();
    const [unreadCount, setUnreadCount] = useState(0);
    const [shown, setShown] = useState(false);
    const [notifications, setNotifications] = useState<readonly ClientNotification[]>([]);
    const [reading, setReading] = useState(false);
    const [failure, setFailure] = useState<ClientFailureReason | null>(null);

    // What a page read is asked for by, so that opening the panel and something having arrived are one mechanism
    // rather than two effects racing to read the same route.
    const [asked, setAsked] = useState(0);

    // What has already been drawn or announced, so an arrival is a notification this client has not seen rather than
    // one it has stopped showing. A ref because nothing on the screen is drawn from it, and it must not restart the
    // reads below when it grows.
    const known = useRef<Seen | null>(null);

    // What the last count read answered, so a rise is measurable without the count itself being a dependency of the
    // effect that reads it — which would restart the interval on every poll that changed anything.
    const counted = useRef<Counted | null>(null);

    // Everything held belongs to the credential that read it, so a sign-out and a sign-in as somebody else start from
    // nothing rather than showing the previous person's centre until the first read lands. It is adjusted while
    // rendering rather than from an effect, because a screen drawn once from the previous person's centre is the whole
    // of what this prevents — and the two refs carry whose they are for the same reason, which is what makes them
    // nothing to reset.
    const [credential, setCredential] = useState(session);

    // The same rule for a write that is still in flight. A read is cancelled by the controller its effect owns, but a
    // write's continuation belongs to the render that started it and cannot be cancelled — so it is asked whose it is
    // when it lands, against a ref that always holds the credential in force rather than the one that render closed
    // over. Without it, a mark-read answered after somebody signed out and somebody else signed in would write the
    // previous account's counted state over what the new one is being shown.
    const inForce = useRef(session);

    useEffect(() => {
        inForce.current = session;
    }, [session]);

    if (credential !== session) {
        setCredential(session);
        setUnreadCount(0);
        setNotifications([]);
        setShown(false);
        setFailure(null);
    }

    useEffect(() => {
        if (session === null || !online) {
            return;
        }

        const attempted = new AbortController();

        async function count(): Promise<void> {
            if (session === null) {
                return;
            }

            const answer = await readUnreadNotificationCount(session, transport);

            if (attempted.signal.aborted || answer.outcome === 'failed') {
                return;
            }

            const before = counted.current?.session === session ? counted.current.count : null;
            const rose = before !== null && answer.value > before;

            counted.current = { session, count: answer.value };
            setUnreadCount(answer.value);

            // Only a rise asks for the page. A count that fell is this client's own marking landing, and a count that
            // did not move is the ordinary poll — neither is anything a reader has to be told about.
            if (rose) {
                setAsked((token) => token + 1);
            }
        }

        void count();

        const polling = window.setInterval(() => {
            void count();
        }, unreadCountInterval);

        // Coming back to the window is when somebody looks at the bell, and it is the moment a poll is least likely to
        // have just landed — a machine that was asleep ran no interval at all.
        function returned(): void {
            if (document.visibilityState === 'visible') {
                void count();
            }
        }

        document.addEventListener('visibilitychange', returned);

        // Subscribed here rather than in an effect of its own, because what it does is exactly what the interval does
        // and the two are cleaned up by the same credential going away. Only the count is read: what the signal
        // carries is that something was raised, and how many stand unread is the deployment's answer rather than a
        // number this client keeps in step on its own.
        const listening = signalledChanges.listen((signal) => {
            if (signal.kind === 'notification.raised') {
                void count();
            }
        });

        return () => {
            attempted.abort();
            window.clearInterval(polling);
            document.removeEventListener('visibilitychange', returned);
            listening();
        };
    }, [session, transport, online, signalledChanges]);

    // Held steady, because the gestures that drive the panel on a phone subscribe to the document with these as their
    // dependencies: a pair rebuilt every render would resubscribe on every one of them.
    const show = useCallback((): void => {
        setShown(true);
    }, []);

    const hide = useCallback((): void => {
        setShown(false);
    }, []);

    const follow = useCallback(
        (notification: ClientNotification): void => {
            setShown(false);
            setNotifications((held) => held.map((row) => (row.id === notification.id ? { ...row, read: true } : row)));

            if (!notification.read) {
                setUnreadCount((standing) => Math.max(0, standing - 1));
            }

            if (session !== null && !notification.read) {
                void setNotificationRead(session, transport, notification.id, true);
            }

            onFollow(notification.target);
        },
        [session, transport, onFollow],
    );

    // What the operating system is told, which is the whole of the desktop head's half of an arrival. Three things
    // decide whether anything is said at all, and each of them is a different question: whether a shell offered the
    // operation, whether this machine was left raising them, and whether somebody is already looking at the window —
    // a notification raised over a window somebody is reading is the client interrupting itself.
    //
    // `document.hasFocus()` rather than `visibilityState`, and the difference is the case this exists for: a desktop
    // window standing behind another is visible and unfocused, which is exactly when nobody is looking at it.
    //
    // What is said is one sentence carrying how many arrived and of what kind, and never anything a message held —
    // `arrivalCounts.ts` is where that is enforced and where it is proven. `Intl.ListFormat` is what joins two kinds
    // into one sentence, so a language decides the conjunction rather than this line.
    const raiseWithTheSystem = useCallback(
        (arrived: readonly ClientNotification[]): void => {
            if (!notifier.offered || arrived.length === 0 || document.hasFocus() || !systemNotificationsChosen()) {
                return;
            }

            const counted = new Intl.PluralRules(locale);
            const said = arrivalCounts(arrived).map(({ kind, count }) =>
                translate(systemNotificationCounts[kind][counted.select(count)], {
                    count: new Intl.NumberFormat(locale).format(count),
                }),
            );

            void notifier.raise(new Intl.ListFormat(locale, { type: 'conjunction' }).format(said)).then((raised) => {
                // A refusal is permanent rather than something to ask about again: the operating system has answered,
                // and a client that kept asking would put its dialog in front of somebody once per arrival. Written on
                // the device, so the switch reads what the machine decided and the next start honours it.
                //
                // Only a refusal. An operation this head does not carry answers `unavailable`, which nobody decided —
                // writing *off* for that would leave somebody a switch they have to find and undo on a machine that
                // never asked them anything.
                if (raised === 'refused') {
                    chooseSystemNotifications(false);
                }
            });
        },
        [notifier, locale, translate],
    );

    // Held steady so the page effect below does not restart on every render, and so the toast an arrival raises can
    // open the notification it is about.
    const announce = useCallback(
        (arrived: readonly ClientNotification[]): void => {
            raiseWithTheSystem(arrived);

            for (const notification of arrived.slice(0, mostArrivalsAnnounced).reverse()) {
                toasts.raise({
                    kind: notificationToastKinds[notification.kind],
                    title: notification.title,
                    body: notification.body,
                    action: {
                        label: translate('notifications.show'),
                        take: () => {
                            follow(notification);
                        },
                    },
                });
            }
        },
        [toasts, translate, follow, raiseWithTheSystem],
    );

    // What an arrival is said with is held rather than depended on. The read below must be decided by the credential,
    // the panel, and what the count said — never by a caller that rebuilt its own callback while rendering, which
    // would otherwise read the whole page again on every render of the frame.
    const announcing = useRef(announce);

    useEffect(() => {
        announcing.current = announce;
    }, [announce]);

    useEffect(() => {
        if (session === null || !online || (!shown && asked === 0)) {
            return;
        }

        const attempted = new AbortController();

        setReading(known.current?.session !== session);

        void (async () => {
            const answer = await readNotifications(session, transport, notificationsShown);

            if (attempted.signal.aborted) {
                return;
            }

            setReading(false);

            if (answer.outcome === 'failed') {
                setFailure(answer.failure.reason);

                return;
            }

            const page = answer.value.notifications;
            const seen = known.current?.session === session ? known.current.ids : null;

            setFailure(null);
            setNotifications(page);
            known.current = { session, ids: new Set(page.map((notification) => notification.id)) };

            // The first read is what this client has, rather than what has just happened, so nothing is announced from
            // it: a person opening the client is not being told about seven things arriving at once.
            if (seen !== null) {
                announcing.current(page.filter((notification) => !notification.read && !seen.has(notification.id)));
            }
        })();

        return () => {
            attempted.abort();
        };
    }, [session, transport, online, shown, asked]);

    function markRead(ids: readonly string[], read: boolean): void {
        if (session === null || ids.length === 0) {
            return;
        }

        const changed = notifications.filter((row) => ids.includes(row.id) && row.read !== read);

        if (changed.length === 0) {
            return;
        }

        setNotifications((held) => held.map((row) => (ids.includes(row.id) ? { ...row, read } : row)));
        setUnreadCount((standing) => Math.max(0, standing + (read ? -changed.length : changed.length)));

        for (const row of changed) {
            void setNotificationRead(session, transport, row.id, read).then((answer) => {
                if (inForce.current !== session) {
                    return;
                }

                if (answer.outcome === 'read') {
                    counted.current = { session, count: answer.value.unreadCount };
                    setUnreadCount(answer.value.unreadCount);

                    return;
                }

                // Taken back rather than left on the screen as something that happened: the deployment still holds the
                // state it held, and a row drawn against a write that failed is the one thing worse than the failure.
                setNotifications((held) =>
                    held.map((held2) => (held2.id === row.id ? { ...held2, read: row.read } : held2)),
                );
                setUnreadCount((standing) => Math.max(0, standing + (read ? 1 : -1)));
                toasts.raise({ kind: 'error', title: translate('notifications.readStateNotChanged') });
            });
        }
    }

    function markAllRead(): void {
        if (session === null || unreadCount === 0) {
            return;
        }

        const held = notifications;

        setNotifications((rows) => rows.map((row) => ({ ...row, read: true })));
        setUnreadCount(0);

        void markAllNotificationsRead(session, transport).then((answer) => {
            if (inForce.current !== session) {
                return;
            }

            if (answer.outcome === 'read') {
                counted.current = { session, count: answer.value.unreadCount };
                setUnreadCount(answer.value.unreadCount);

                return;
            }

            setNotifications(held);
            setUnreadCount(held.filter((row) => !row.read).length);
            toasts.raise({ kind: 'error', title: translate('notifications.notAllMarkedRead') });
        });
    }

    return {
        unreadCount,
        shown,
        notifications,
        reading,
        failure,
        show,
        hide,
        markRead,
        markAllRead,
        follow,
    };
}
