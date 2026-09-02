// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';

// When the last receiving hop recorded a message, as one row of a list draws it. It sits here rather than beside either
// of the two screens that draw it — the mail list and the conversation — because a screen may reach what is shared and
// what is shared reaches no screen, and a second copy of a date is how two lists of the same mail start disagreeing
// about when it arrived.
//
// The wording is `localization/instants.ts`'s, which is where the client's one rule about an instant lives: the
// reader's own language and the reader's own clock, and no screen naming a zone. The machine-readable form stays on the
// element beside it, which is what lets anything reading the document work with the instant rather than with somebody's
// local spelling of it.

/** The instant a message was recorded, or nothing at all where the message carries none this client can read. */
export function ReceivedAt({ at }: { readonly at: string | null }) {
    const { locale } = useLocalization();
    const when = wordInstant(at, locale, 'stamp');

    if (at === null || when === null) {
        return null;
    }

    return (
        <time dateTime={at} className="shrink-0 text-xs tabular-nums text-faint">
            {when}
        </time>
    );
}
