// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Locale } from '../localization/locale';

// How long ago an account last took mail in, worded rather than stamped. The whole wording is `Intl`'s under the active
// locale — Polish alone has three plural forms for most of these units, and a catalogue entry spelling any of them out
// would be a second copy of something the platform already gets right in both languages.

// The units an age is expressed in, largest last, each with how many of it go into the next one.
const divisions = [
    { unit: 'second', per: 60 },
    { unit: 'minute', per: 60 },
    { unit: 'hour', per: 24 },
    { unit: 'day', per: 7 },
    { unit: 'week', per: 4.34524 },
    { unit: 'month', per: 12 },
    { unit: 'year', per: Number.POSITIVE_INFINITY },
] as const;

/**
 * How long before `readAt` that instant was, worded under the active language.
 *
 * @param instant When the account last took mail in, as the deployment reported it.
 * @param readAt When the answer holding it was read, which is what the age is measured against rather than a clock a
 * component reads for itself.
 * @param locale The language the wording is asked for in.
 * @returns The age as a person reads it, or `null` where the deployment named no instant this client can read.
 */
export function ageOf(instant: string | null, readAt: Date, locale: Locale): string | null {
    if (instant === null) {
        return null;
    }

    const at = Date.parse(instant);

    if (Number.isNaN(at)) {
        return null;
    }

    const worded = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
    let elapsed = (at - readAt.getTime()) / 1000;

    for (const division of divisions) {
        if (Math.abs(elapsed) < division.per) {
            return worded.format(Math.round(elapsed), division.unit);
        }

        elapsed /= division.per;
    }

    return worded.format(Math.round(elapsed), 'year');
}
