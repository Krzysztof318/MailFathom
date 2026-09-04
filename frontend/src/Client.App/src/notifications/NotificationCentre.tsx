// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import type { ClientFailureReason, ClientNotification } from '@mailfathom/client-backend';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useCurrentMinute } from './notificationAge';
import { NotificationRow } from './NotificationRow';
import { NotificationRowMenu } from './NotificationRowMenu';
import type { NotificationCentre as Centre, NotificationFilter } from './useNotificationCentre';
import type { PanelSwipe } from './usePanelSwipe';

// The notification centre itself: a panel entering from beside the rail in a wide window and rising from the foot of a
// narrow one, over a scrim that closes it. It is the platform's own modal dialog, which is what makes four of its
// obligations somebody else's — the page behind it is inert, focus moves into it and is held there, Escape leaves it,
// and leaving puts focus back on the bell that opened it.
//
// **Escape goes through the client's own state rather than round it.** The dialog's cancel is refused and the centre
// is asked to close instead, so leaving by the keyboard is the same act as leaving by the scrim or by the close
// control — and travels the same way off the screen rather than vanishing.
//
// **The selection is this list's own**, which is what the design project says of all seven of its lists: what is
// picked out here is nothing to what is picked out in the mail list, and it goes when the panel does.
//
// **What is drawn is what the deployment answered**, less whatever the reader has marked since. Nothing here filters
// a row out from under somebody: the unread tab is read at the moment it is drawn, so marking a row read on that tab
// takes it out, which is the design project's own list and what a reader marking things read expects to see happen.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// How many are picked out, in the forms a language has for the noun — the same sentence the mail list's own bar says,
// because a count of things picked out is one shape rather than one per list.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

// How many arrived since the centre was last cleared, in the forms a language has for the adjective. Polish inflects
// it and English does not, which is exactly the difference one entry could not express.
const newCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'notifications.new.other',
    one: 'notifications.new.one',
    two: 'notifications.new.other',
    few: 'notifications.new.few',
    many: 'notifications.new.many',
    other: 'notifications.new.other',
};

export function NotificationCentre({
    centre,
    swipe,
}: {
    readonly centre: Centre;

    /** The two gestures a finger drives the panel with, which the frame holds because the navigation carries half of them. */
    readonly swipe: PanelSwipe;
}) {
    const { locale, translate } = useLocalization();
    const now = useCurrentMinute();
    const panel = useRef<HTMLDialogElement>(null);
    const tabs = useId();
    const [filter, setFilter] = useState<NotificationFilter>('all');
    const [selected, setSelected] = useState<readonly string[]>([]);
    const [menu, setMenu] = useState<{ readonly notification: ClientNotification; readonly at: MenuPoint } | null>(
        null,
    );

    // The one thing the platform will not do from a value: a dialog is modal because something called for it to be,
    // so this is the imperative API the state is synchronized with. How it travels on and off the screen is the
    // stylesheet's, which is why nothing here times anything.
    useEffect(() => {
        const shown = panel.current;

        if (shown === null) {
            return;
        }

        if (centre.shown && !shown.open) {
            shown.showModal();
        }

        if (!centre.shown && shown.open) {
            shown.close();
        }
    }, [centre.shown]);

    // It covers the screen it was opened over, so the back gesture closes it before it navigates anywhere and taking
    // the navigation to another destination leaves it behind. What closing it does is the panel's own way out, which
    // clears what was picked and any menu standing on it as well.
    useScreenLayer(centre.shown, () => {
        leave();
    });

    const drawn = filter === 'unread' ? centre.notifications.filter((row) => !row.read) : centre.notifications;
    const picked = selected.filter((id) => centre.notifications.some((row) => row.id === id));

    function clear(): void {
        setSelected([]);
    }

    function leave(): void {
        clear();
        setMenu(null);
        centre.hide();
    }

    function toggleSelected(id: string): void {
        setSelected((held) => (held.includes(id) ? held.filter((chosen) => chosen !== id) : [...held, id]));
    }

    return (
        <dialog
            ref={(element) => {
                panel.current = element;
                swipe.attachPanel(element);
            }}
            aria-label={translate('notifications.title')}
            style={swipe.offset === null ? undefined : { transform: `translateY(${String(swipe.offset)}px)` }}
            // The two compositions are the design project's own: a sheet that stops above the bottom navigation in a
            // narrow window, and a panel standing beside the rail in a wide one. Where it comes from differs with it,
            // which is what the two motions and the two closed positions below say.
            // The panel is in the platform's top layer, where the frame's safe-area padding is not around it: the top
            // inset is padded away inside it, and the bottom one is a margin because what the sheet stops above is the
            // bottom navigation, which the frame has already lifted clear of the gesture bar.
            className={`fixed inset-x-0 top-0 bottom-navigation m-0 mb-safe-bottom hidden h-auto max-h-none w-auto max-w-none open:flex flex-col overflow-visible rounded-t-4xl border-line bg-panel pt-safe-top text-text shadow-dialog backdrop:bg-scrim workspace:end-auto workspace:bottom-0 workspace:start-rail workspace:w-notifications workspace:rounded-none workspace:border-e ${
                swipe.dragging
                    ? 'transition-none'
                    : swipe.springing
                      ? 'motion-spring'
                      : 'motion-sheet workspace:motion-panel'
            } translate-y-full open:translate-y-0 starting:open:translate-y-full workspace:-translate-x-full workspace:open:translate-x-0 workspace:starting:open:-translate-x-full`}
            onPointerDown={swipe.onPanelPointerDown}
            onCancel={(event) => {
                // Refused so that leaving by the keyboard is the same act as leaving by the scrim: the centre is asked
                // to close, and the panel travels off the screen rather than being taken off it.
                event.preventDefault();
                leave();
            }}
            onClick={(event) => {
                if (event.target === event.currentTarget) {
                    leave();
                }
            }}
            onClose={clear}
        >
            {/* The handle the design project draws at the top of the sheet, which says the panel is something a finger
                can push away. It is drawn where that gesture exists and nowhere else. */}
            <span
                aria-hidden="true"
                className="flex shrink-0 justify-center pt-2 pb-0.5 pointer-fine:hidden workspace:hidden"
            >
                <span className="h-1 w-9.5 rounded-xs bg-line-strong" />
            </span>

            <div className="flex shrink-0 items-center gap-2.5 border-b border-line ps-4.5 pe-3 pt-3.5 pb-3.25">
                <Icon name="notifications" className="size-5.25 text-muted" />

                <h2 className="min-w-0 flex-1 text-xl font-semibold">{translate('notifications.title')}</h2>

                {centre.unreadCount > 0 ? (
                    <span className="shrink-0 rounded-xl bg-error-soft px-2.25 py-0.75 text-xs font-semibold text-error-text">
                        {translate(newCounted[new Intl.PluralRules(locale).select(centre.unreadCount)], {
                            count: new Intl.NumberFormat(locale).format(centre.unreadCount),
                        })}
                    </span>
                ) : null}

                <Control label={translate('notifications.close')} icon="close" shape="symbol" onPress={leave} />
            </div>

            <fieldset className="flex shrink-0 items-center gap-1.75 border-b border-line-soft bg-sunken px-4 py-2.75">
                <legend className="sr-only">{translate('notifications.filter')}</legend>

                <ChoiceSegment
                    shape="filter"
                    name={tabs}
                    value="all"
                    chosen={filter === 'all'}
                    onChoose={() => {
                        setFilter('all');
                    }}
                >
                    {translate('notifications.all')}
                </ChoiceSegment>

                <ChoiceSegment
                    shape="filter"
                    name={tabs}
                    value="unread"
                    chosen={filter === 'unread'}
                    onChoose={() => {
                        setFilter('unread');
                    }}
                >
                    {centre.unreadCount > 0
                        ? translate('notifications.unreadWithCount', {
                              count: new Intl.NumberFormat(locale).format(centre.unreadCount),
                          })
                        : translate('notifications.unreadTab')}
                </ChoiceSegment>

                <span className="min-w-1 flex-1" />

                {centre.unreadCount > 0 ? (
                    <Control
                        label={translate('notifications.markAll')}
                        className="text-accent-deep"
                        onPress={centre.markAllRead}
                    />
                ) : null}
            </fieldset>

            {picked.length === 0 ? null : (
                <div
                    role="toolbar"
                    aria-label={translate('notifications.selectionBar')}
                    className="flex shrink-0 items-center gap-0.5 overflow-x-auto bg-accent px-3.5 py-2 shadow-raised"
                >
                    <Control label={translate('select.clear')} icon="close" shape="onAccentSymbol" onPress={clear} />

                    <p role="status" className="me-2 ps-0.5 text-base font-semibold text-balance text-on-accent">
                        {translate(selectionCounted[new Intl.PluralRules(locale).select(picked.length)], {
                            count: new Intl.NumberFormat(locale).format(picked.length),
                        })}
                    </p>

                    <Control
                        label={translate('notifications.markRead')}
                        icon="mark_email_read"
                        shape="onAccentSymbol"
                        onPress={() => {
                            centre.markRead(picked, true);
                            clear();
                        }}
                    />

                    <Control
                        label={translate('notifications.markUnread')}
                        icon="mark_email_unread"
                        shape="onAccentSymbol"
                        onPress={() => {
                            centre.markRead(picked, false);
                            clear();
                        }}
                    />
                </div>
            )}

            <ul
                ref={(element) => {
                    swipe.attachList(element);
                }}
                className="min-h-0 flex-1 overflow-y-auto overscroll-contain"
            >
                {drawn.map((notification) => (
                    <NotificationRow
                        key={notification.id}
                        notification={notification}
                        selected={picked.includes(notification.id)}
                        selecting={picked.length > 0}
                        now={now}
                        onOpen={() => {
                            clear();
                            centre.follow(notification);
                        }}
                        onSelect={() => {
                            toggleSelected(notification.id);
                        }}
                        onToggleRead={() => {
                            centre.markRead([notification.id], !notification.read);
                        }}
                        onPress={(at) => {
                            setMenu({ notification, at });
                        }}
                    />
                ))}
            </ul>

            {drawn.length === 0 ? <Nothing reading={centre.reading} failure={centre.failure} /> : null}

            {menu === null ? null : (
                <NotificationRowMenu
                    notification={menu.notification}
                    at={menu.at}
                    onSelect={() => {
                        toggleSelected(menu.notification.id);
                    }}
                    onToggleRead={() => {
                        centre.markRead([menu.notification.id], !menu.notification.read);
                    }}
                    onOpen={() => {
                        clear();
                        centre.follow(menu.notification);
                    }}
                    onClose={() => {
                        setMenu(null);
                    }}
                />
            )}
        </dialog>
    );
}

// The three things an empty list can mean, each said as what it is rather than as an absence: a read still in flight, a
// read that did not answer, and a centre with nothing in it. The last is the design project's own, and it says the
// thing a reader actually wants to know — that nothing is waiting — rather than that a list is empty.
function Nothing({ reading, failure }: { readonly reading: boolean; readonly failure: ClientFailureReason | null }) {
    const { translate } = useLocalization();

    if (reading) {
        return (
            <p role="status" className="flex flex-1 items-center justify-center px-6 py-13 text-base text-muted">
                {translate('notifications.reading')}
            </p>
        );
    }

    if (failure !== null) {
        return (
            <p role="alert" className="flex flex-1 items-center justify-center px-6 py-13 text-base text-error-text">
                {translate('notifications.failed', { reason: translate(failureLabels[failure]) })}
            </p>
        );
    }

    return (
        <div className="flex flex-1 flex-col items-center justify-center gap-2.25 px-6 py-13 text-center">
            <Icon name="notifications_off" className="size-8.5 text-faint" />
            <p className="text-base text-pretty text-muted">{translate('notifications.empty')}</p>
        </div>
    );
}
