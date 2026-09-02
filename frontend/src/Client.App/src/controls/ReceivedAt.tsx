// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import { wordRecentInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';

// The instant a message was recorded, as the design project words it on a row: the time alone for today, the word for
// yesterday, and the day and month for anything older. The clock is read once, when the row is first drawn, because a
// render is pure and the clock is not — so a row that stays on the screen across midnight words yesterday as today
// until it is drawn again, which a windowed list does for every row it scrolls back to.

/** The instant a message was recorded, or nothing at all where the message carries none this client can read. */
export function ReceivedAt({ at }: { readonly at: string | null }) {
    const { locale } = useLocalization();
    const [now] = useState(() => Date.now());
    const when = wordRecentInstant(at, locale, now);

    if (at === null || when === null) {
        return null;
    }

    return (
        <time dateTime={at} className="shrink-0 text-xs tabular-nums text-faint">
            {when}
        </time>
    );
}
