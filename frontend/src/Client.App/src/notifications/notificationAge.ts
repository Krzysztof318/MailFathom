// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useSyncExternalStore } from 'react';
import type { Locale } from '../localization/locale';

// How long ago a notification happened, in the ladder the design project draws on a row: now, minutes, hours, the
// language's own word for yesterday, then days. It sits beside the centre rather than in `localization/instants.ts`
// because it is a different reading of an instant from the three that module words — a row here says *how long ago*
// where a message row says *when* — and the two would otherwise be one function with a mode.
//
// Every step is `Intl`'s wording rather than a catalogue entry. Polish needs three plural forms for minutes, hours,
// and days where English needs two, and "yesterday" is a word the platform already knows in both; a hand-written
// ladder here would be a second copy of all of it, wrong in one language first.

const aMinute = 60_000;
const anHour = 60 * aMinute;
const aDay = 24 * anHour;

/**
 * How long ago the instant was, worded for a reader of this language.
 *
 * @param instant What the service sent, or `null` where it sent nothing.
 * @param locale The language the wording is in.
 * @param now What the current instant is, which is a parameter so that a test pins it rather than the day the suite
 * happened to run on.
 * @returns The wording, or `null` where what arrived is not an instant this client can read.
 */
export function wordNotificationAge(instant: string, locale: Locale, now: number): string | null {
    const at = Date.parse(instant);

    if (Number.isNaN(at)) {
        return null;
    }

    // Ahead of the reader's own clock is a deployment and a machine that disagree by a few seconds rather than
    // something that has not happened yet, so it is worded as having just happened rather than as a countdown.
    const since = Math.max(0, now - at);
    // Short rather than long, because the design project draws the age beside a title on one line and a wording that
    // pushed the title into wrapping would be the row's own defect. It is the platform's short form in each language
    // rather than an abbreviation written here, which is the same rule the whole ladder is built on.
    const relative = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style: 'short' });

    if (since < aMinute) {
        return relative.format(0, 'second');
    }

    if (since < anHour) {
        return relative.format(-Math.floor(since / aMinute), 'minute');
    }

    if (since < aDay) {
        return relative.format(-Math.floor(since / anHour), 'hour');
    }

    return relative.format(-Math.floor(since / aDay), 'day');
}

// The clock that wording is measured against. A render is pure and a clock is not, so it is read the one way React
// sanctions for something outside itself: subscribed to, and answered with a value that does not change inside the
// minute it names — which is as fine as any of the wordings above ever get, and is what keeps a row from being worded
// against the instant the client started rather than against now.
const clock = {
    watch: (changed: () => void): (() => void) => {
        const ticking = window.setInterval(changed, aMinute);

        return () => {
            window.clearInterval(ticking);
        };
    },

    minute: (): number => Math.floor(Date.now() / aMinute) * aMinute,
};

/** The minute it currently is, kept current as it turns over. */
export function useCurrentMinute(): number {
    return useSyncExternalStore(clock.watch, clock.minute);
}
