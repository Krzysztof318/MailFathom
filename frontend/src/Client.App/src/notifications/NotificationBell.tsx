// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The one control in the navigation that is not a place to go. It draws two things: whether anything is unread, which
// is the symbol, and how much of it, which is the badge — and the design project caps the badge at *9+* because the
// difference between ten and eleven is not what a badge is for.
//
// **The count is said as well as drawn.** A badge reading `9+` is a picture of a number, so the control's own name
// carries the sentence a screen reader announces instead; a reader who cannot see the badge is told how many are
// waiting rather than that there is a bell.
//
// It is drawn the two ways the navigation around it is: an item in the bottom bar of a narrow window, with its name
// under it like every other item there, and a control of its own at the foot of the rail in a wide one, where the
// design project stands it above the account.

/** Where the badge stops counting, which is the design project's own cap. */
export const mostUnreadShown = 9;

// How many stand unread, in the forms a language has for the noun. Selected rather than spelled for the reason the
// selection bar gives: Polish needs three forms here and English hides that it needs two.
const unreadCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'notifications.unread.other',
    one: 'notifications.unread.one',
    two: 'notifications.unread.other',
    few: 'notifications.unread.few',
    many: 'notifications.unread.many',
    other: 'notifications.unread.other',
};

export function NotificationBell({
    unreadCount,
    shown,
    onPress,
}: {
    readonly unreadCount: number;

    /** Whether the panel is open, which the control says about itself rather than only by what is on the screen. */
    readonly shown: boolean;

    readonly onPress: () => void;
}) {
    const { locale, translate } = useLocalization();
    const unread = unreadCount > 0;
    const counted = new Intl.NumberFormat(locale).format(Math.min(unreadCount, mostUnreadShown));

    return (
        <button
            type="button"
            aria-expanded={shown}
            aria-label={
                unread
                    ? translate(unreadCounted[new Intl.PluralRules(locale).select(unreadCount)], {
                          count: new Intl.NumberFormat(locale).format(unreadCount),
                      })
                    : translate('notifications.title')
            }
            className={`flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-2xl px-0.5 py-1.75 text-2xs font-medium transition workspace:size-11.5 workspace:flex-none workspace:justify-center workspace:gap-0 workspace:rounded-3xl workspace:border workspace:px-0 workspace:py-0 ${
                shown
                    ? 'bg-accent-soft text-accent-deep workspace:border-accent-line'
                    : 'text-muted hover:bg-hover hover:text-text workspace:border-line workspace:bg-panel workspace:text-text-soft workspace:shadow-raised'
            }`}
            onClick={onPress}
        >
            <span className="relative flex items-center justify-center">
                <Icon name={unread ? 'notifications_active' : 'notifications'} className="size-6" />

                {/* Bounded rather than round, so `9+` is as legible as `3`. The ring is the rail's own colour, which is
                    what lifts the badge off the symbol underneath it at both widths. */}
                {unread ? (
                    <span
                        aria-hidden="true"
                        className="absolute -end-1.5 -top-1.5 flex h-5.25 min-w-5.25 items-center justify-center rounded-full border-2 border-rail bg-error px-1.25 text-2xs font-bold text-on-accent"
                    >
                        {unreadCount > mostUnreadShown ? `${counted}+` : counted}
                    </span>
                ) : null}
            </span>

            {/* The name is under the symbol in the bottom bar, where every item carries one, and gone in the rail,
                where the design draws this control as the symbol alone above the account. */}
            <span className="max-w-full truncate workspace:hidden">{translate('notifications.title')}</span>
        </button>
    );
}
